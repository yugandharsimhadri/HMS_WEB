using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record AllocateRequest(Guid ProductId, int Units);

/// <summary>The bill header the counter may set. Every amount on the saved
/// Sale is recomputed server-side from the lines, so none of them appear
/// here — and neither do TenantId or the audit columns, for the same reason
/// as <see cref="SavePatientRequest"/>.</summary>
public record SaleHeaderRequest(
    Guid? PatientId, Guid? VisitId, string CustomerName, string? DoctorName,
    PaymentMode PaymentMode, string? TransactionNo, bool IsTaxInvoice);

public record SaveSaleRequest(SaleHeaderRequest Sale, List<SaleLine> Lines);

/// <summary>What the medicine editor may set — the catalogue fields and
/// nothing else.</summary>
public record SaveProductRequest(
    Guid? Id, string Name, string? GenericName, string? Manufacturer, string? Composition,
    string? Storage, string? PackSize, string HsnCode, decimal GstRate, DrugSchedule Schedule,
    string? RackLocation, int ReorderLevel, bool IsActive, int UnitsPerPack,
    bool AllowLooseSale, DispensingUnit DispensingUnit);
public record QuickAddStockRequest(int Packs, decimal Mrp, string? BatchNo, DateTime? Expiry, decimal PurchaseRate);

/// <summary>One delivery line going onto the shelf — what a delivery note
/// actually says.</summary>
public record ReceiveStockRequest(
    Guid ProductId, string BatchNo, DateTime ExpiryDate, int Packs, int FreePacks,
    decimal PurchaseRate, decimal Mrp, string? SupplierName, string? SupplierInvoiceNo);

public record AdjustStockRequest(Guid BatchId, int CorrectedQuantity, AdjustmentReason Reason, string? Notes);

public record RepackRequest(int UnitsPerPack);

/// <summary>
/// The pharmacy counter: product search, batch allocation and billing.
/// Stock intake (ReceiveStockAsync), adjustments and the vendor-bill
/// importer are deferred to a later pass — this is the counter's own
/// day-to-day path.
/// </summary>
[ApiController]
[Authorize]
[Route("api/pharmacy")]
public class PharmacyController(PharmacyService pharmacy) : ControllerBase
{
    [HttpGet("products")]
    public async Task<ActionResult<List<Product>>> SearchProducts([FromQuery] string? term, [FromQuery] int take = 50)
        => Ok(await pharmacy.SearchProductsAsync(term, take));

    /// <summary>One product by id — so the counter does not fetch the whole
    /// catalogue to look up a single row.</summary>
    [HttpGet("products/{id:guid}")]
    public async Task<ActionResult<Product>> Product(Guid id)
    {
        var product = await pharmacy.GetProductAsync(id);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>The catalogue for a picker: names and prices, no batches.</summary>
    [HttpGet("catalogue")]
    public async Task<ActionResult<List<CatalogueEntry>>> Catalogue()
        => Ok(await pharmacy.GetCatalogueAsync());

    [HttpGet("products/{productId:guid}/batches")]
    public async Task<ActionResult<List<Batch>>> Batches(Guid productId)
        => Ok(await pharmacy.GetSellableBatchesAsync(productId));

    [HttpPost("products")]
    public async Task<IActionResult> SaveProduct(SaveProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("The brand name is required.");

        // Loaded rather than attached, so the client cannot write TenantId,
        // the audit stamps or SearchKey — the last of which is what the
        // duplicate-medicine index is built on.
        var existing = request.Id is { } id
            ? (await pharmacy.SearchProductsAsync(null, 5000)).FirstOrDefault(p => p.Id == id)
            : null;

        var product = existing ?? new Product();

        product.Name = request.Name.Trim();
        product.GenericName = NullIfBlank(request.GenericName);
        product.Manufacturer = NullIfBlank(request.Manufacturer);
        product.Composition = NullIfBlank(request.Composition);
        product.Storage = NullIfBlank(request.Storage);
        product.PackSize = NullIfBlank(request.PackSize);
        product.HsnCode = string.IsNullOrWhiteSpace(request.HsnCode) ? "3004" : request.HsnCode.Trim();
        product.GstRate = request.GstRate;
        product.Schedule = request.Schedule;
        product.RackLocation = NullIfBlank(request.RackLocation);
        product.ReorderLevel = request.ReorderLevel;
        product.IsActive = request.IsActive;
        product.UnitsPerPack = Math.Max(1, request.UnitsPerPack);
        product.AllowLooseSale = request.AllowLooseSale;
        product.DispensingUnit = request.DispensingUnit;

        try
        {
            await pharmacy.SaveProductAsync(product);
            return NoContent();
        }
        catch (DuplicateMedicineException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>Puts stock on the shelf for a medicine that is physically
    /// there but not yet in the system - the counter's own path, distinct
    /// from receiving a full supplier consignment.</summary>
    [HttpPost("products/{productId:guid}/quick-add-stock")]
    public async Task<ActionResult<Batch>> QuickAddStock(Guid productId, QuickAddStockRequest request)
    {
        try
        {
            var batch = await pharmacy.QuickAddStockAsync(
                productId, request.Packs, request.Mrp, request.BatchNo, request.Expiry, request.PurchaseRate);
            return Ok(batch);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Works out which batches (nearest expiry first) fill a
    /// requested quantity, so the counter can show what will actually be
    /// taken off the shelf before the line is added to the bill.</summary>
    [HttpPost("allocate")]
    public async Task<IActionResult> Allocate(AllocateRequest request)
    {
        var (allocations, shortfall) = await pharmacy.AllocateAsync(request.ProductId, request.Units);
        return Ok(new
        {
            allocations = allocations.Select(a => new { batch = a.Batch, units = a.Units }),
            shortfall
        });
    }

    [HttpPost("sales")]
    public async Task<ActionResult<Sale>> SaveSale(SaveSaleRequest request)
    {
        var header = request.Sale;

        var sale = new Sale
        {
            // The bill's own date is the server's, not the client's: a bill
            // is a statutory document and its date decides which day's book
            // it lands in.
            BillDate = DateTime.Now,
            PatientId = header.PatientId,
            VisitId = header.VisitId,
            CustomerName = string.IsNullOrWhiteSpace(header.CustomerName) ? "Guest" : header.CustomerName.Trim(),
            DoctorName = NullIfBlank(header.DoctorName),
            PaymentMode = header.PaymentMode,
            TransactionNo = NullIfBlank(header.TransactionNo),
            IsTaxInvoice = header.IsTaxInvoice,
        };

        try
        {
            return Ok(await pharmacy.SaveSaleAsync(sale, request.Lines));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("sales/{id:guid}")]
    public async Task<ActionResult<Sale>> GetSale(Guid id)
    {
        var sale = await pharmacy.GetSaleAsync(id);
        return sale is null ? NotFound() : Ok(sale);
    }

    [HttpGet("sales")]
    public async Task<ActionResult<List<Sale>>> SearchSales([FromQuery] string? term, [FromQuery] int take = 100)
        => Ok(await pharmacy.SearchSalesAsync(term, take));

    /// <summary>Every medicine bill for a patient, newest first — the
    /// history panel, and where a months-old bill gets reprinted from.</summary>
    [HttpGet("sales/by-patient/{patientId:guid}")]
    public async Task<ActionResult<List<Sale>>> SalesByPatient(Guid patientId)
        => Ok(await pharmacy.GetSalesByPatientAsync(patientId));

    // ── Inventory ──────────────────────────────────────────────────────────

    /// <summary>Every batch on the shelf for one medicine, nearest expiry
    /// first — what Inventory lists and what a correction picks from.</summary>
    [HttpGet("products/{productId:guid}/all-batches")]
    public async Task<ActionResult<List<Batch>>> AllBatches(Guid productId)
        => Ok(await pharmacy.GetSellableBatchesAsync(productId));

    /// <summary>Receives a supplier consignment. The only way stock enters
    /// the system other than the counter's quick-add and the bill importer.</summary>
    [HttpPost("receive-stock")]
    public async Task<ActionResult<StockEntry>> ReceiveStock(ReceiveStockRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BatchNo))
            return BadRequest("Batch number is printed on the pack and has to appear on the bill.");
        if (request.Packs <= 0 && request.FreePacks <= 0)
            return BadRequest("Enter how many packs arrived.");
        if (request.Mrp <= 0)
            return BadRequest("Enter the MRP printed on the pack — the counter prices from it.");
        if (request.ExpiryDate.Date <= DateTime.Today)
            return BadRequest("Expiry must be in the future.");

        var products = await pharmacy.SearchProductsAsync(null, 5000);
        var product = products.FirstOrDefault(p => p.Id == request.ProductId);
        if (product is null) return NotFound("That medicine no longer exists.");

        var entry = new StockEntry
        {
            EntryDate = DateTime.Today,
            SupplierName = string.IsNullOrWhiteSpace(request.SupplierName) ? null : request.SupplierName.Trim(),
            SupplierInvoiceNo = string.IsNullOrWhiteSpace(request.SupplierInvoiceNo) ? null : request.SupplierInvoiceNo.Trim(),
        };

        var item = new StockEntryItem
        {
            ProductId = product.Id,
            BatchNo = request.BatchNo.Trim(),
            ExpiryDate = request.ExpiryDate,
            Quantity = request.Packs,
            FreeQuantity = request.FreePacks,
            UnitsPerPack = product.UnitsPerPack,
            PurchaseRate = request.PurchaseRate,
            Mrp = request.Mrp,
        };

        try
        {
            return Ok(await pharmacy.ReceiveStockAsync(entry, [item]));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>Corrects a shelf count and writes the audit row with it —
    /// stock otherwise only moves by receiving or selling, and both leave a
    /// document.</summary>
    [HttpPost("adjust-stock")]
    public async Task<ActionResult<StockAdjustment>> AdjustStock(AdjustStockRequest request)
    {
        try
        {
            return Ok(await pharmacy.AdjustStockAsync(
                request.BatchId, request.CorrectedQuantity, request.Reason, request.Notes));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("adjustments")]
    public async Task<ActionResult<List<StockAdjustment>>> Adjustments([FromQuery] int take = 100)
        => Ok(await pharmacy.GetAdjustmentsAsync(take));

    /// <summary>What re-counting this medicine's batches at a new
    /// units-per-pack would do, without doing it.</summary>
    [HttpPost("products/{productId:guid}/repack-preview")]
    public async Task<ActionResult<RepackPreview>> RepackPreview(Guid productId, RepackRequest request)
        => Ok(await pharmacy.PreviewRepackAsync(productId, request.UnitsPerPack));

    [HttpPost("products/{productId:guid}/repack")]
    public async Task<ActionResult<int>> Repack(Guid productId, RepackRequest request)
    {
        try
        {
            var by = User.Identity?.Name;
            return Ok(await pharmacy.RepackAsync(productId, request.UnitsPerPack, by));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<Product>>> LowStock() => Ok(await pharmacy.GetLowStockAsync());

    [HttpGet("expiring")]
    public async Task<ActionResult<List<Batch>>> Expiring([FromQuery] int withinDays = 90)
        => Ok(await pharmacy.GetExpiringAsync(withinDays));

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
