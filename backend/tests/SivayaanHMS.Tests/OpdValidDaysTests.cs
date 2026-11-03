using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SivayaanHMS.Core;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Tests;

/// <summary>
/// The OPD validity window: one consultation fee covering a doctor for a set
/// number of days, which is how most Indian clinics actually charge. Ported
/// from the desktop's OpdValidDaysTests, against a real PostgreSQL database.
///
/// The arithmetic is day-wise and deliberately done once — at the moment the
/// money is taken — rather than recomputed from the doctor's current setting on
/// every later visit. These tests pin both halves of that: the window a payment
/// opens, and what counts as being inside it.
///
/// The desktop's tests backdated rows to reach the edges of a window. These
/// move the clock instead, which is the same thing without a test that writes
/// to the table it is testing.
/// </summary>
public sealed class OpdValidDaysTests : IAsyncLifetime
{
    private readonly TestDb _testDb = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly SettableClock _clock = new(DateTime.Today.AddHours(11));
    private readonly Guid _patientId = Guid.NewGuid();
    private readonly Guid _otherPatientId = Guid.NewGuid();
    private readonly Guid _doctorId = Guid.NewGuid();
    private readonly Guid _otherDoctorId = Guid.NewGuid();
    private OpdService _opd = null!;

    private static DateTime Today => DateTime.Today;

    public async Task InitializeAsync()
    {
        await _testDb.MigrateAsync();
        _opd = new OpdService(_testDb.CreateFactory(_tenant), _clock, NullLogger<OpdService>.Instance);

        await using var db = _testDb.CreateContext(_tenant);

        db.Patients.Add(new Patient { Id = _patientId, PatientNo = "P00001", Name = "Ramesh Kumar", Phone = "9000000001", Age = 41 });
        db.Patients.Add(new Patient { Id = _otherPatientId, PatientNo = "P00002", Name = "Sita Devi", Phone = "9000000002", Age = 35 });

        // Seven-day cover, the common arrangement.
        db.Doctors.Add(new Doctor
        {
            Id = _doctorId, Name = "Dr Priya Nair", Speciality = "Paediatrics",
            Qualification = "MBBS, MD (Paediatrics)", ConsultationFee = 300m, OpdValidDays = 7,
        });

        // No cover at all — every visit charged, which is the default and what
        // every doctor already on file gets when this upgrade is applied.
        db.Doctors.Add(new Doctor
        {
            Id = _otherDoctorId, Name = "Dr Anand Rao", Speciality = "General",
            ConsultationFee = 250m, OpdValidDays = 0,
        });

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _testDb.Dispose();
        return Task.CompletedTask;
    }

    private Task<Visit> BookAsync(Guid? patientId = null, Guid? doctorId = null,
                                  DateTime? on = null, decimal fee = 300m, Guid? waivedAgainst = null)
        => _opd.BookVisitAsync(patientId ?? _patientId, doctorId ?? _doctorId,
                               on ?? Today.AddHours(10), "Fever", fee,
                               feeWaivedAgainstVisitId: waivedAgainst);

    private async Task<Visit> ReloadAsync(Guid id)
    {
        await using var db = _testDb.CreateContext(_tenant);
        return await db.Visits.FirstAsync(v => v.Id == id);
    }

    // ── The window a payment opens ────────────────────────────────────────

    [Fact]
    public async Task Taking_the_fee_opens_a_window_of_the_doctors_own_length()
    {
        var visit = await BookAsync();
        await _opd.CollectFeeAsync(visit.Id);

        // Seven days from the day the money arrived, inclusive of that seventh
        // day — "paid on the 1st, free until the 8th".
        Assert.Equal(Today.AddDays(7), (await ReloadAsync(visit.Id)).FreeFollowUpUntil);
    }

    [Fact]
    public async Task A_doctor_with_no_window_opens_none()
    {
        var visit = await BookAsync(doctorId: _otherDoctorId, fee: 250m);
        await _opd.CollectFeeAsync(visit.Id);

        Assert.Null((await ReloadAsync(visit.Id)).FreeFollowUpUntil);
    }

    /// <summary>The window is dated from the payment, not the booking. A patient
    /// who books on Monday and pays on Thursday was told "seven days" on Thursday.</summary>
    [Fact]
    public async Task The_window_runs_from_the_day_the_money_arrived()
    {
        var visit = await BookAsync(on: Today.AddDays(-3).AddHours(10));
        await _opd.CollectFeeAsync(visit.Id);

        Assert.Equal(Today.AddDays(7), (await ReloadAsync(visit.Id)).FreeFollowUpUntil);
    }

    /// <summary>A doctor changing seven days to fifteen later must not move a
    /// window a patient was already told about — it is a promise, not a setting.</summary>
    [Fact]
    public async Task Changing_the_doctors_setting_later_does_not_move_a_window_already_given()
    {
        var visit = await BookAsync();
        await _opd.CollectFeeAsync(visit.Id);

        await using (var db = _testDb.CreateContext(_tenant))
        {
            var doctor = await db.Doctors.FirstAsync(d => d.Id == _doctorId);
            doctor.OpdValidDays = 15;
            await db.SaveChangesAsync();
        }

        Assert.Equal(Today.AddDays(7), (await ReloadAsync(visit.Id)).FreeFollowUpUntil);
    }

    // ── Who is inside it ──────────────────────────────────────────────────

    [Fact]
    public async Task A_return_inside_the_window_is_covered()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        var cover = await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(3));

        Assert.NotNull(cover);
        Assert.Equal(first.Id, cover!.Id);
    }

    /// <summary>The seventh day is inside a seven-day window; the eighth is not.
    /// This is the boundary the desk argues about, so it is pinned.</summary>
    [Fact]
    public async Task The_last_day_is_covered_and_the_day_after_is_not()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        Assert.NotNull(await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(7)));
        Assert.Null(await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(8)));
    }

    /// <summary>Day-wise, not clock-wise: a fee paid late in the evening still
    /// covers the whole of the final day, down to its last minute.</summary>
    [Fact]
    public async Task The_time_of_day_never_enters_into_it()
    {
        var first = await BookAsync();
        _clock.Now = Today.AddHours(21).AddMinutes(45);   // paid at 9:45 pm
        await _opd.CollectFeeAsync(first.Id);

        var lastDayMorning = Today.AddDays(7).AddHours(8);
        var lastDayNight = Today.AddDays(7).AddHours(23).AddMinutes(59);

        Assert.NotNull(await _opd.FindFeeCoverAsync(_patientId, _doctorId, lastDayMorning));
        Assert.NotNull(await _opd.FindFeeCoverAsync(_patientId, _doctorId, lastDayNight));
        Assert.Null(await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(8)));
    }

    [Fact]
    public async Task A_fee_paid_to_one_doctor_does_not_cover_another()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        Assert.Null(await _opd.FindFeeCoverAsync(_patientId, _otherDoctorId, Today));
    }

    [Fact]
    public async Task One_patients_fee_does_not_cover_another_patient()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        Assert.Null(await _opd.FindFeeCoverAsync(_otherPatientId, _doctorId, Today));
    }

    [Fact]
    public async Task An_unpaid_visit_covers_nothing()
    {
        await BookAsync();

        Assert.Null(await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today));
    }

    /// <summary>The web-only half of isolation: a payment in another clinic never
    /// covers a patient in this one, even with the very same ids.</summary>
    [Fact]
    public async Task Another_clinics_payment_never_covers_this_one()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        var stranger = new OpdService(_testDb.CreateFactory(Guid.NewGuid()), _clock, NullLogger<OpdService>.Instance);

        Assert.Null(await stranger.FindFeeCoverAsync(_patientId, _doctorId, Today));
    }

    /// <summary>
    /// The rule that stops the scheme running away: only a payment opens a
    /// window. If a free review re-anchored it, a patient returning on day six
    /// would be covered to day thirteen, then day twenty, and never pay again.
    /// </summary>
    [Fact]
    public async Task A_review_does_not_extend_the_window()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);
        var paidUntil = (await ReloadAsync(first.Id)).FreeFollowUpUntil;

        var review = await BookAsync(on: Today.AddDays(6).AddHours(10), fee: 0m, waivedAgainst: first.Id);

        var reloaded = await ReloadAsync(review.Id);
        Assert.True(reloaded.IsReview);
        Assert.Null(reloaded.FreeFollowUpUntil);

        // Day nine is past the original window and nothing has moved it.
        Assert.Equal(Today.AddDays(7), paidUntil);
        Assert.Null(await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(9)));
    }

    /// <summary>A second payment inside a live window restarts the clock from
    /// the new payment — the desk charged, so the patient gets the cover.</summary>
    [Fact]
    public async Task Paying_again_inside_the_window_restarts_it()
    {
        var first = await BookAsync();
        _clock.Now = Today.AddDays(-5).AddHours(11);
        await _opd.CollectFeeAsync(first.Id);

        var second = await BookAsync();
        _clock.Now = Today.AddHours(11);
        await _opd.CollectFeeAsync(second.Id);

        var cover = await _opd.FindFeeCoverAsync(_patientId, _doctorId, Today.AddDays(6));

        // The older payment expired two days ago; the newer one answers.
        Assert.NotNull(cover);
        Assert.Equal(second.Id, cover!.Id);
    }

    // ── What the server decides at booking ────────────────────────────────

    /// <summary>The booking endpoint never takes the paying visit from a client —
    /// it asks this, so a free visit cannot be claimed that is not owed.</summary>
    [Fact]
    public async Task At_booking_a_zero_fee_inside_the_window_resolves_to_the_paying_visit()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        var cover = await _opd.FindCoverForBookingAsync(_patientId, _doctorId, Today.AddDays(2), fee: 0m);

        Assert.Equal(first.Id, cover?.Id);
    }

    /// <summary>A fee typed back in is the desk saying this one is chargeable — a
    /// return inside the window for a genuinely new complaint.</summary>
    [Fact]
    public async Task At_booking_a_fee_typed_back_in_is_charged_even_inside_the_window()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        Assert.Null(await _opd.FindCoverForBookingAsync(_patientId, _doctorId, Today.AddDays(2), fee: 300m));
    }

    [Fact]
    public async Task At_booking_a_doctor_with_no_window_never_gives_cover()
    {
        // Even if a stale paid visit somehow carried a window for this doctor.
        var first = await BookAsync(doctorId: _otherDoctorId, fee: 250m);
        await _opd.CollectFeeAsync(first.Id);
        await using (var db = _testDb.CreateContext(_tenant))
        {
            (await db.Visits.FirstAsync(v => v.Id == first.Id)).FreeFollowUpUntil = Today.AddDays(5);
            await db.SaveChangesAsync();
        }

        Assert.Null(await _opd.FindCoverForBookingAsync(_patientId, _otherDoctorId, Today, fee: 0m));
    }

    // ── What the visit then reads as ──────────────────────────────────────

    [Fact]
    public async Task A_review_is_settled_without_being_paid()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        var review = await BookAsync(fee: 0m, waivedAgainst: first.Id);
        var reloaded = await ReloadAsync(review.Id);

        Assert.True(reloaded.IsReview);
        Assert.True(reloaded.FeeSettled);
        Assert.False(reloaded.FeePaid);
        Assert.False(reloaded.CanCollectFee);
        Assert.Equal("Review", reloaded.VisitKind);
        Assert.Equal("Review · no fee", reloaded.FeeBadge);
    }

    [Fact]
    public async Task An_ordinary_paid_visit_reads_as_new()
    {
        var visit = await BookAsync();
        await _opd.CollectFeeAsync(visit.Id);

        var paid = await ReloadAsync(visit.Id);
        Assert.Equal("New", paid.VisitKind);
        Assert.False(paid.IsReview);
        Assert.True(paid.FeeSettled);
    }

    /// <summary>The server refuses, not just the screen: taking money on a review
    /// would burn a receipt number against a visit the register records as free.</summary>
    [Fact]
    public async Task Collecting_a_fee_on_a_review_is_refused()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);
        var review = await BookAsync(fee: 0m, waivedAgainst: first.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _opd.CollectFeeAsync(review.Id));
        Assert.Contains("no fee is due", ex.Message);

        var reloaded = await ReloadAsync(review.Id);
        Assert.False(reloaded.FeePaid);
        Assert.Null(reloaded.FeeReceiptNo);
    }

    /// <summary>A review is still just a booking with no money on it, so it can
    /// be called off — unlike the payment it rides on.</summary>
    [Fact]
    public async Task A_review_can_still_be_cancelled()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);

        var review = await BookAsync(fee: 0m, waivedAgainst: first.Id);
        Assert.True((await ReloadAsync(review.Id)).CanCancel);

        await _opd.SetStatusAsync(review.Id, VisitStatus.Cancelled);
        Assert.Equal(VisitStatus.Cancelled, (await ReloadAsync(review.Id)).Status);
    }

    [Fact]
    public void The_doctors_degrees_print_beside_the_name()
    {
        var withDegrees = new Doctor { Name = "Dr Priya Nair", Qualification = "MBBS, MD (Paediatrics)" };
        Assert.Equal("Dr Priya Nair, MBBS, MD (Paediatrics)", withDegrees.NameWithQualification);

        // Left blank it must not leave a dangling comma on the receipt.
        Assert.Equal("Dr Anand Rao", new Doctor { Name = "Dr Anand Rao" }.NameWithQualification);
        Assert.Equal("Dr Anand Rao", new Doctor { Name = "Dr Anand Rao", Qualification = "  " }.NameWithQualification);
    }

    // ── What is printed ───────────────────────────────────────────────────

    /// <summary>A review prints a real page, loading the payment it rides on — the
    /// same data path the print endpoint uses.</summary>
    [Fact]
    public async Task A_review_slip_and_the_receipt_that_opened_the_window_both_print()
    {
        var first = await BookAsync();
        await _opd.CollectFeeAsync(first.Id);
        var review = await BookAsync(on: Today.AddDays(2).AddHours(10), fee: 0m, waivedAgainst: first.Id);

        var clinic = new ClinicProfile { Name = "Twinkle Children's Hospital" };

        var receipt = FeeReceiptDocument.Generate((await _opd.GetVisitAsync(first.Id))!, clinic, new DocumentTheme());
        var slip = FeeReceiptDocument.Generate((await _opd.GetVisitAsync(review.Id))!, clinic, new DocumentTheme());

        Assert.Equal("%PDF"u8.ToArray(), receipt.Take(4).ToArray());
        Assert.Equal("%PDF"u8.ToArray(), slip.Take(4).ToArray());

        // GetVisitAsync must have brought the paying visit with it, or the slip
        // cannot quote the receipt number the money was actually taken on.
        Assert.Equal(first.Id, (await _opd.GetVisitAsync(review.Id))!.FeeWaivedAgainstVisit?.Id);
    }

    private sealed class SettableClock(DateTime now) : IClock
    {
        public DateTime Now { get; set; } = now;
    }
}
