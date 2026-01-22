using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/stores")]
    public class StoresController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public StoresController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> GetStores(int? userId, string? sortBy, string? sortDir)
        {
            try
            {
                var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!, string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
                var map = new Dictionary<string, System.Linq.Expressions.Expression<Func<Models.Store, object?>>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = s => s.StoreId,
                    ["name"] = s => s.Name ?? string.Empty,
                    ["address"] = s => s.Address ?? string.Empty,
                    ["email"] = s => s.Email ?? string.Empty,
                    ["phone"] = s => s.PhoneNumber ?? string.Empty,
                    ["lastUpdatedBy"] = s => s.LastModifiedUserId
                };

                IQueryable<Models.Store> storeQuery = _db.Stores.AsNoTracking();
                storeQuery = sortSpec == null ? storeQuery.OrderBy(s => s.StoreId) : storeQuery.ApplySort(sortSpec, map, s => s.StoreId);

                var items = await (from s in storeQuery
                                   join u in _db.AxUsers.AsNoTracking() on s.LastModifiedUserId equals u.Id into uu
                                   from u in uu.DefaultIfEmpty()
                                   select new
                                   {
                                       id = s.StoreId,
                                       name = s.Name,
                                       address = s.Address,
                                       phone = s.PhoneNumber,
                                       email = s.Email,
                                       hours = s.WorkingHours,
                                       lastUpdatedBy = u != null ? u.Code : null
                                   }).ToListAsync();

                return JsonResultHelper.StableJson(_env, new { items });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Stores endpoint failed", ex.ToString(), userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>() });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateStore(int id, Dtos.StoreUpdateDto dto, int? userId)
        {
            try
            {
                var store = await _db.Stores.FirstOrDefaultAsync(s => s.StoreId == id);
                if (store == null) return NotFound(new { message = "Store not found. Please verify the store identifier and try again." });

                var name = dto.Name?.Trim();
                var address = dto.Address?.Trim();
                var phone = dto.PhoneNumber?.Trim();
                var email = dto.Email?.Trim();
                var hours = dto.WorkingHours?.Trim();

                if (name is not null) store.Name = name;
                if (address is not null) store.Address = address;
                if (phone is not null) store.PhoneNumber = phone;
                if (email is not null) store.Email = email;
                if (hours is not null) store.WorkingHours = hours;

                store.LastModifiedUserId = userId ?? 2;

                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Information, "Store details updated", $"StoreId={store.StoreId}, Name={store.Name}", userId ?? 2);
                return Ok(new { success = true, message = "Store details have been saved successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Update store failed", ex.ToString(), userId ?? 2);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
