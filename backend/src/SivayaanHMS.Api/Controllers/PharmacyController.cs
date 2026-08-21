using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SivayaanHMS.Core;
using SivayaanHMS.Data;

namespace SivayaanHMS.Api.Controllers;

public record AllocateRequest(Guid ProductId, int Units);
public record SaveSaleRequest(Sale Sale, List<SaleLine> Lines);
public record QuickAddStockRequest(int Packs, decimal Mrp, string? BatchNo, DateTime? Expiry, decimal PurchaseRate);

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

    [HttpGet("products/{productId:guid}/batches")]
    public async Task<ActionResult<List<Batch>>> Batches(Guid productId)
        => Ok(await pharmacy.GetSellableBatchesAsync(productId));

    [HttpPost("products")]
    public async Task<IActionResult> SaveProduct(Product product)
    {
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
        try
        {
            return Ok(await pharmacy.SaveSaleAsync(request.Sale, request.Lines));
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

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<Product>>> LowStock() => Ok(await pharmacy.GetLowStockAsync());

    [HttpGet("expiring")]
    public async Task<ActionResult<List<Batch>>> Expiring([FromQuery] int withinDays = 90)
        => Ok(await pharmacy.GetExpiringAsync(withinDays));
}
