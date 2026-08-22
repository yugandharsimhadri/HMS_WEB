using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SivayaanHMS.Data;
using SivayaanHMS.Printing;

namespace SivayaanHMS.Api.Controllers;

/// <summary>
/// Every printable document, as a PDF. Returned inline so the browser's own
/// viewer opens it — that viewer already has print and save, which is what
/// the desktop's print-preview window existed to provide.
///
/// Each endpoint re-reads the record rather than trusting anything posted in:
/// a receipt or a bill is a statutory document, and what it says has to come
/// from what was actually saved.
/// </summary>
[ApiController]
[Authorize]
[Route("api/print")]
public class PrintController(
    OpdService opd, PharmacyService pharmacy, SettingsService settings,
    DiagnosticsService diagnostics, PediatricsService pediatrics, ProcedureBillsService procedureBills,
    DentistService dentist, IDbContextFactory<AppDbContext> factory) : ControllerBase
{
    /// <summary>
    /// Receipt for one dental instalment. The case is re-read rather than
    /// taken from the request, because the balance printed on it is the
    /// whole point and has to be the balance as stored.
    /// </summary>
    [HttpGet("dental-receipt/{paymentId:guid}")]
    public async Task<IActionResult> DentalReceipt(Guid paymentId, [FromQuery] bool reprint = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var payment = await db.DentalPayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId);
        if (payment is null) return NotFound();

        var dentalCase = await dentist.GetCaseAsync(payment.DentalCaseId);
        if (dentalCase is null) return NotFound();

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(DentalReceiptDocument.Generate(payment, dentalCase, clinic, theme, reprint),
                      $"dental-receipt-{payment.ReceiptNo}.pdf");
    }

    [HttpGet("procedure-bill/{billId:guid}")]
    public async Task<IActionResult> ProcedureBill(Guid billId, [FromQuery] bool reprint = false)
    {
        var bill = await procedureBills.GetBillAsync(billId);
        if (bill is null) return NotFound();

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(ProcedureBillDocument.Generate(bill, clinic, theme, reprint),
                      $"procedure-bill-{bill.BillNo}.pdf");
    }

    /// <summary>
    /// The card a parent carries. Printed even with no doses on it — an
    /// empty card is a real answer, and refusing would send them away with
    /// nothing rather than with proof there is nothing.
    /// </summary>
    [HttpGet("vaccination-history/{patientId:guid}")]
    public async Task<IActionResult> VaccinationHistory(Guid patientId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId);
        if (patient is null) return NotFound();

        var records = await pediatrics.GetVaccinationHistoryAsync(patientId);
        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(VaccinationHistoryDocument.Generate(patient, records, clinic, theme),
                      $"vaccination-record-{patient.PatientNo}.pdf");
    }

    [HttpGet("diagnostic-bill/{billId:guid}")]
    public async Task<IActionResult> DiagnosticBill(Guid billId, [FromQuery] bool reprint = false)
    {
        var bill = await diagnostics.GetBillAsync(billId);
        if (bill is null) return NotFound();

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(DiagnosticBillDocument.Generate(bill, clinic, theme, reprint),
                      $"diagnostic-bill-{bill.BillNo}.pdf");
    }

    /// <summary>
    /// The appointment slip. Reachable for any appointment, not only at the
    /// moment of booking as on the desktop — a patient who loses the slip is
    /// the obvious case, and re-reading the row is what every other document
    /// here already does.
    /// </summary>
    [HttpGet("appointment/{appointmentId:guid}")]
    public async Task<IActionResult> AppointmentSlip(Guid appointmentId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (appointment is null) return NotFound();

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(AppointmentSlipDocument.Generate(appointment, clinic, theme),
                      $"appointment-{appointment.AppointmentNo}.pdf");
    }

    [HttpGet("prescription/{visitId:guid}")]
    public async Task<IActionResult> Prescription(Guid visitId)
    {
        var visit = await opd.GetVisitAsync(visitId);
        if (visit is null) return NotFound();

        if (visit.Prescription.Count == 0 && visit.DiagnosticRequests.Count == 0)
            return BadRequest($"Token {visit.TokenNo} has no prescription yet.");

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(PrescriptionDocument.Generate(visit, clinic, theme), $"prescription-{visit.VisitNo}.pdf");
    }

    [HttpGet("receipt/{visitId:guid}")]
    public async Task<IActionResult> Receipt(Guid visitId, [FromQuery] bool reprint = false)
    {
        var visit = await opd.GetVisitAsync(visitId);
        if (visit is null) return NotFound();

        if (!visit.FeePaid)
            return BadRequest($"Token {visit.TokenNo} has not paid yet — there is no receipt to print.");

        var clinic = await settings.GetClinicAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(FeeReceiptDocument.Generate(visit, clinic, theme, reprint),
                      $"receipt-{visit.FeeReceiptNo}.pdf");
    }

    [HttpGet("bill/{saleId:guid}")]
    public async Task<IActionResult> Bill(Guid saleId, [FromQuery] bool reprint = false)
    {
        var sale = await pharmacy.GetSaleAsync(saleId);
        if (sale is null) return NotFound();

        var profile = await settings.GetPharmacyAsync();
        var theme = await settings.GetDocumentThemeAsync();

        return Inline(PharmacyInvoiceDocument.Generate(sale, profile, theme, reprint), $"bill-{sale.BillNo}.pdf");
    }

    private FileContentResult Inline(byte[] pdf, string fileName)
    {
        Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
        return File(pdf, "application/pdf");
    }
}
