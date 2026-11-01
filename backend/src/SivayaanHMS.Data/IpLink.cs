using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Core;

namespace SivayaanHMS.Data;

/// <summary>
/// The rules for putting a counter bill (pharmacy, diagnostics, lab, procedures) on an
/// admitted patient's IP bill instead of taking payment for it at the counter.
///
/// It is the clinic's choice twice over: the feature is a setting, and each bill is still
/// "pay now" unless the counter ticks "charge to IP bill". A linked bill is never copied
/// into the IP bill - the running bill reads it live, so editing the counter bill fixes the
/// IP bill too. Once the IP final bill is made the link is frozen: nothing new is added and
/// nothing already on it changes until that final bill is cancelled.
/// </summary>
public static class IpLink
{
    /// <summary>The admission to charge the bill to, or null for a bill paid at the counter.</summary>
    public static async Task<Guid?> ResolveAsync(AppDbContext db, SettingsService settings, Guid? patientId, bool chargeToIp)
    {
        if (!chargeToIp) return null;

        var general = await settings.GetGeneralAsync();
        if (!general.InpatientEnabled || !general.IpLinkedBilling)
            throw new InvalidOperationException("Charging to the IP bill is switched off. Turn it on in Settings → In-patients.");

        if (patientId is null)
            throw new InvalidOperationException("Choose the admitted patient to put this on their IP bill.");

        var admission = await db.Admissions.AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Status == AdmissionStatus.Admitted)
            .Select(a => new { a.Id, a.IpNo })
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("This patient is not admitted, so there is no IP bill to charge. Take payment instead.");

        if (await HasFinalBillAsync(db, admission.Id))
            throw new InvalidOperationException(
                $"The final bill for {admission.IpNo} is already made. Take payment here, or cancel that final bill first.");

        return admission.Id;
    }

    /// <summary>Refuses a change to a bill that sits on a final IP bill.</summary>
    public static async Task EnsureOpenAsync(AppDbContext db, Guid? admissionId)
    {
        if (admissionId is not { } id) return;

        if (await HasFinalBillAsync(db, id))
        {
            var ipNo = await db.Admissions.AsNoTracking().Where(a => a.Id == id).Select(a => a.IpNo).FirstOrDefaultAsync();
            throw new InvalidOperationException(
                $"This bill is on the final IP bill for {ipNo}. Cancel that final bill before changing it.");
        }
    }

    private static Task<bool> HasFinalBillAsync(AppDbContext db, Guid admissionId)
        => db.IpBills.AnyAsync(b => b.AdmissionId == admissionId && b.Status == IpBillStatus.Final);
}
