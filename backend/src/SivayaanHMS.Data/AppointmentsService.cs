using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// A row on the shared Reminders screen, assembled fresh from whichever
/// modules are switched on — not itself a stored entity. A source becomes
/// "actioned" by writing a <see cref="ReminderLog"/> row against it, at
/// which point it stops appearing here.
/// </summary>
public class ReminderItem
{
    public ReminderSourceKind SourceKind { get; init; }
    public Guid SourceId { get; init; }
    public Guid PatientId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string? PatientPhone { get; init; }
    public DateTime DueOn { get; init; }

    /// <summary>What the row is actually about — "Follow-up for Aarav Rao",
    /// "Appointment with Dr. Iyer at 10:30" — built by whichever source
    /// contributed it, since each source's own record shapes it differently.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Whether a <see cref="ReminderLog"/> already exists for this
    /// source. A reminded row stays on the list rather than disappearing —
    /// the front desk can see at a glance what's left to do and flip a
    /// mistaken mark back off.</summary>
    public bool IsReminded { get; init; }

    /// <summary>What the status badge on the Reminders grid reads.</summary>
    public string StatusLabel => IsReminded ? "Reminded" : "Not reminded";
}

/// <summary>
/// Generic booking infrastructure every clinical module can use. Deliberately
/// small: an appointment is a patient, a doctor, a time and which module it
/// is for — the module itself owns everything that happens once the patient
/// actually arrives (see <see cref="MarkCheckedInAsync"/>).
/// </summary>
public class AppointmentsService(IDbContextFactory<AppDbContext> factory, SettingsService settings, IClock clock)
{
    // ── Booking ────────────────────────────────────────────────────────────

    /// <summary>
    /// Books a new appointment. Refuses a <see cref="AppointmentModuleContext"/>
    /// whose own module is switched off — the booking screen should only ever
    /// offer contexts that are actually on, but this is the same
    /// defence-in-depth every status-gated save in this codebase already has.
    /// </summary>
    public async Task<Appointment> BookAsync(Appointment appointment)
    {
        if (appointment.PatientId == Guid.Empty)
            throw new InvalidOperationException("Pick a patient before booking.");
        if (appointment.DoctorId == Guid.Empty)
            throw new InvalidOperationException("Pick a doctor before booking.");

        await EnsureModuleEnabledAsync(appointment.ModuleContext);

        await using var db = await factory.CreateDbContextAsync();

        var entity = new Appointment
        {
            Id = appointment.Id == Guid.Empty ? Guid.NewGuid() : appointment.Id,
            AppointmentNo = await NumberService.NextAsync(db, NumberService.Appointment),
            PatientId = appointment.PatientId,
            PatientName = appointment.PatientName,
            PatientPhone = appointment.PatientPhone,
            DoctorId = appointment.DoctorId,
            DoctorName = appointment.DoctorName,
            ScheduledOn = appointment.ScheduledOn,
            DurationMinutes = appointment.DurationMinutes > 0 ? appointment.DurationMinutes : 15,
            ModuleContext = appointment.ModuleContext,
            Status = AppointmentStatus.Scheduled,
            Reason = appointment.Reason,
            Notes = appointment.Notes
        };

        db.Appointments.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    private async Task EnsureModuleEnabledAsync(AppointmentModuleContext context)
    {
        var general = await settings.GetGeneralAsync();
        var enabled = context switch
        {
            AppointmentModuleContext.General => general.OpdEnabled,
            AppointmentModuleContext.Pediatrics => general.PediatricsEnabled,
            AppointmentModuleContext.Dentist => general.DentistEnabled,
            AppointmentModuleContext.PathologyLab => general.PathologyLabEnabled,
            _ => false
        };

        if (!enabled)
            throw new InvalidOperationException($"The {context} module is switched off — turn it on under Settings → Features before booking against it.");
    }

    // ── Cancellation and rescheduling ─────────────────────────────────────

    /// <summary>Marks an appointment cancelled. Never deletes the row — a
    /// cancellation is still a fact worth keeping, the same reasoning
    /// <c>StockAdjustment</c> writes a document rather than silently
    /// correcting a number.</summary>
    public async Task CancelAsync(Guid appointmentId, string reason)
    {
        await using var db = await factory.CreateDbContextAsync();
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment is null) return;

        if (appointment.Status is AppointmentStatus.CheckedIn)
            throw new InvalidOperationException(
                "This appointment has already been checked in and cannot be cancelled — cancel the record it turned into instead.");

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.Notes = string.IsNullOrWhiteSpace(appointment.Notes)
            ? $"Cancelled: {reason}"
            : $"{appointment.Notes}\nCancelled: {reason}";

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves a slot to a new time by creating a new appointment linked back
    /// to this one, and marking this one <see cref="AppointmentStatus.Rescheduled"/>
    /// — never by mutating <see cref="Appointment.ScheduledOn"/> in place, so
    /// the original time stays on record instead of being silently
    /// overwritten. See the class doc on <see cref="Appointment.RescheduledFromId"/>.
    /// </summary>
    public async Task<Appointment> RescheduleAsync(Guid appointmentId, DateTime newScheduledOn, int? newDurationMinutes = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var existing = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (existing is null)
            throw new InvalidOperationException("That appointment no longer exists.");

        if (existing.Status is AppointmentStatus.CheckedIn)
            throw new InvalidOperationException(
                "This appointment has already been checked in and cannot be rescheduled.");

        var replacement = new Appointment
        {
            PatientId = existing.PatientId,
            PatientName = existing.PatientName,
            PatientPhone = existing.PatientPhone,
            DoctorId = existing.DoctorId,
            DoctorName = existing.DoctorName,
            ScheduledOn = newScheduledOn,
            DurationMinutes = newDurationMinutes ?? existing.DurationMinutes,
            ModuleContext = existing.ModuleContext,
            Status = AppointmentStatus.Scheduled,
            Reason = existing.Reason,
            AppointmentNo = await NumberService.NextAsync(db, NumberService.Appointment),
            RescheduledFromId = existing.Id
        };

        existing.Status = AppointmentStatus.Rescheduled;

        db.Appointments.Add(replacement);
        await db.SaveChangesAsync();

        return replacement;
    }

    // ── Daily list ─────────────────────────────────────────────────────────

    /// <summary>Every appointment for one day, still pending or already
    /// checked in — what the daily appointments screen lists.</summary>
    public async Task<List<Appointment>> GetForDateAsync(DateTime date, Guid? doctorId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var from = date.Date;
        var to = from.AddDays(1);

        var query = db.Appointments.AsNoTracking()
            .Where(a => !a.IsDeleted && a.ScheduledOn >= from && a.ScheduledOn < to
                     && a.Status != AppointmentStatus.Rescheduled);

        if (doctorId is { } id) query = query.Where(a => a.DoctorId == id);

        return await query.OrderBy(a => a.ScheduledOn).ToListAsync();
    }

    // ── Check-in ───────────────────────────────────────────────────────────

    /// <summary>
    /// Marks an appointment checked in once its module has created its own
    /// working record — a <c>Visit</c> for <see cref="AppointmentModuleContext.General"/>,
    /// a <c>DentalCase</c> for Dentist, a <c>LabOrder</c> for Pathology Lab.
    /// This method does not create that record itself — each module's own
    /// service does, then calls this to close the loop. See
    /// <see cref="Appointment.LinkedRecordId"/>.
    /// </summary>
    public async Task MarkCheckedInAsync(Guid appointmentId, Guid linkedRecordId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment is null) return;

        if (appointment.Status is not AppointmentStatus.Scheduled)
            throw new InvalidOperationException("This appointment is no longer pending check-in.");

        appointment.Status = AppointmentStatus.CheckedIn;
        appointment.LinkedRecordId = linkedRecordId;

        await db.SaveChangesAsync();
    }

    // ── Reminders ──────────────────────────────────────────────────────────

    /// <summary>
    /// Everything due for a reminder, from every source currently wired in —
    /// OPD follow-ups and upcoming appointments today. Pediatrics'
    /// vaccine-due and Dentist's next-sitting-due sources plug in here the
    /// same way once those modules exist, each contributing its own query
    /// rather than this method reaching into their tables.
    ///
    /// Includes reminders already actioned, each carrying
    /// <see cref="ReminderItem.IsReminded"/> — the front desk needs to see
    /// what's already been done, not just what's still outstanding, so a
    /// reminded row stays on the list instead of vanishing. Not-yet-reminded
    /// rows sort first.
    /// </summary>
    public async Task<List<ReminderItem>> GetDueRemindersAsync(int leadDays)
    {
        await using var db = await factory.CreateDbContextAsync();
        var cutoff = clock.Now.Date.AddDays(leadDays + 1);

        var actioned = await db.ReminderLogs.AsNoTracking()
            .Where(r => r.ActionedOn != null)
            .Select(r => new { r.SourceKind, r.SourceId })
            .ToListAsync();
        var isActioned = actioned.Select(a => (a.SourceKind, a.SourceId)).ToHashSet();

        var items = new List<ReminderItem>();

        var followUps = await db.Visits.AsNoTracking()
            .Include(v => v.Patient)
            .Where(v => !v.IsDeleted && v.FollowUpOn != null && v.FollowUpOn < cutoff)
            .ToListAsync();

        items.AddRange(followUps
            .Select(v => new ReminderItem
            {
                SourceKind = ReminderSourceKind.FollowUp,
                SourceId = v.Id,
                PatientId = v.PatientId,
                PatientName = v.Patient.Name,
                PatientPhone = v.Patient.Phone,
                DueOn = v.FollowUpOn!.Value,
                Description = $"Follow-up for {v.Patient.Name}",
                IsReminded = isActioned.Contains((ReminderSourceKind.FollowUp, v.Id))
            }));

        var upcoming = await db.Appointments.AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .Where(a => !a.IsDeleted && a.Status == AppointmentStatus.Scheduled && a.ScheduledOn < cutoff)
            .ToListAsync();

        items.AddRange(upcoming
            .Select(a => new ReminderItem
            {
                SourceKind = ReminderSourceKind.Appointment,
                SourceId = a.Id,
                PatientId = a.PatientId,
                PatientName = a.PatientName,
                PatientPhone = a.PatientPhone,
                DueOn = a.ScheduledOn,
                Description = $"Appointment with {a.DoctorName} at {a.ScheduledOn:HH:mm}",
                IsReminded = isActioned.Contains((ReminderSourceKind.Appointment, a.Id))
            }));

        return items.OrderBy(i => i.IsReminded).ThenBy(i => i.DueOn).ToList();
    }

    /// <summary>Marks one reminder actioned — on screen today, "sent" once
    /// WhatsApp/email exist.</summary>
    public async Task MarkReminderActionedAsync(
        ReminderSourceKind sourceKind, Guid sourceId, Guid patientId, DateTime dueOn, string? actionedBy)
    {
        await using var db = await factory.CreateDbContextAsync();

        var log = await db.ReminderLogs.FirstOrDefaultAsync(
            r => r.SourceKind == sourceKind && r.SourceId == sourceId && r.ActionedOn == null);

        if (log is null)
        {
            log = new ReminderLog
            {
                SourceKind = sourceKind, SourceId = sourceId, PatientId = patientId,
                DueOn = dueOn, Channel = ReminderChannel.OnScreen
            };
            db.ReminderLogs.Add(log);
        }

        log.ActionedOn = clock.Now;
        log.ActionedBy = actionedBy;

        await db.SaveChangesAsync();
    }

    /// <summary>Undoes <see cref="MarkReminderActionedAsync"/> — the front
    /// desk marked something reminded by mistake, or it turns out the
    /// reminder still needs sending after all.</summary>
    public async Task UnmarkReminderActionedAsync(ReminderSourceKind sourceKind, Guid sourceId)
    {
        await using var db = await factory.CreateDbContextAsync();

        var log = await db.ReminderLogs.FirstOrDefaultAsync(
            r => r.SourceKind == sourceKind && r.SourceId == sourceId && r.ActionedOn != null);
        if (log is null) return;

        db.ReminderLogs.Remove(log);
        await db.SaveChangesAsync();
    }
}
