using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/product-variants")]
    public class ProductVariantsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ProductVariantsController(AppDbContext db, IHostEnvironment env)
        {
            _db = db; _env = env;
        }

        private bool CanManageProducts() => UserContextHelper.CanManageProducts(HttpContext);
        private int CurrentUserId() => UserContextHelper.GetCurrentUserId(HttpContext);

        // Variant queries moved from Program.cs
        [HttpGet("/api/products/{productId:int}/variants")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDistinctSizesColors([FromServices] Services.ProductQueryService svc, int productId)
        {
            var resp = await svc.GetDistinctSizesColorsAsync(productId, HttpContext.RequestAborted);
            return JsonResultHelper.StableJson(_env, resp);
        }

        [HttpGet("/api/variants/by-selection")]
        [AllowAnonymous]
        public async Task<IActionResult> GetVariantBySelection([FromServices] Services.ProductQueryService svc, int productId, string size, string color)
        {
            var row = await svc.GetVariantBySelectionAsync(productId, size, color, HttpContext.RequestAborted);
            if (row == null) return JsonResultHelper.StableJson(_env, new { message = "Variant not found." }, StatusCodes.Status404NotFound);
            return JsonResultHelper.StableJson(_env, row);
        }

        [HttpGet("/api/variants/{variantId:int}/availability")]
        [AllowAnonymous]
        public async Task<IActionResult> GetAvailability([FromServices] Services.ProductQueryService svc, int variantId)
        {
            var list = await svc.GetAvailabilityAsync(variantId, HttpContext.RequestAborted);
            return JsonResultHelper.StableJson(_env, list);
        }

        [HttpPost("{id:int}/activate")]
        public async Task<IActionResult> Activate(int id)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                var v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (v == null) return NotFound(new { message = "Inventory row not found. It may have been removed." });
                var parentProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
                if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
                {
                    return BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
                }
                var uid = CurrentUserId();
                v.IsActive = true;
                v.LastModifiedById = uid;
                v.LastUpdatedDt = DateTime.Now;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Inventory activated", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
                return Ok(new { success = true, message = "Product Inventory has been successfully activated." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Activate inventory failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPost("{id:int}/deactivate")]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                var v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (v == null) return NotFound(new { message = "Inventory row not found. It may have been removed." });
                var parentProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
                if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
                {
                    return BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
                }
                var uid = CurrentUserId();
                v.IsActive = false;
                v.LastModifiedById = uid;
                v.LastUpdatedDt = DateTime.Now;
                var inventories = await _db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted).ToListAsync();
                foreach (var inv in inventories)
                {
                    inv.IsActive = false;
                    inv.LastModifiedById = uid;
                    inv.LastUpdatedDt = DateTime.Now;
                }
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Inventory deactivated", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
                return Ok(new { success = true, message = "Product Inventory has been successfully deactivated." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Deactivate inventory failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }

        [HttpPost("{id:int}/delete")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (v == null) return NotFound(new { message = "The inventory row was not found. It may have been removed." });
                var parentProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.ProductId);
                if (parentProduct == null || !parentProduct.IsActive || parentProduct.IsDeleted)
                {
                    return BadRequest(new { success = false, message = "Product Inventory for this product cannot be changed because this product is deactivated or deleted." });
                }
                var uid = CurrentUserId();
                v.IsActive = false;
                v.IsDeleted = true;
                v.LastModifiedById = uid;
                v.LastUpdatedDt = DateTime.Now;

                var inventories = await _db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted).ToListAsync();
                foreach (var inv in inventories)
                {
                    inv.IsActive = false;
                    inv.IsDeleted = true;
                    inv.LastModifiedById = uid;
                    inv.LastUpdatedDt = DateTime.Now;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Inventory deleted", $"VariantId={v.Id}; ProductId={v.ProductId}", uid);
                return Ok(new { success = true, message = "Product Inventory has been successfully deleted." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Delete inventory failed", ex.ToString(), CurrentUserId());
                return StatusCode(500);
            }
        }
    }
}