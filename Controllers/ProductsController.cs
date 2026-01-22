using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Infrastructure;
using OnlineContract.Models;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/products")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ProductsController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private bool CanManageProducts() => UserContextHelper.CanManageProducts(HttpContext);
        private int CurrentUserId() => UserContextHelper.GetCurrentUserId(HttpContext);

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetProducts([FromQuery] string? q, [FromQuery] int? storeId, [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] string? sortBy, [FromQuery] string? sortDir)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                IQueryable<Product> products = _db.Products.AsNoTracking().Where(p => p.Id > 0 && !p.IsDeleted);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    products = products.Where(p => EF.Functions.Like(p.Name ?? "", $"%{s}%"));
                }

                var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
                var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<Product, object?>>> {
                    { "id", p => p.Id },
                    { "name", p => p.Name },
                    { "inputDt", p => p.InputDt },
                    { "isActive", p => p.IsActive }
                };

                IQueryable<Product> orderedProducts;
                if (sortSpec == null)
                {
                    orderedProducts = products.OrderBy(p => p.Id);
                }
                else if (string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) || string.Equals(sortSpec.By, "qtyStore2", StringComparison.OrdinalIgnoreCase))
                {
                    var storeIdSort = string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                    if (sortSpec.Desc)
                    {
                        orderedProducts = products.OrderByDescending(p => (
                            from v in _db.ProductVariants.AsNoTracking()
                            where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                            join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                            where !i.IsDeleted && i.StoreId == storeIdSort && i.IsActive
                            select (int?)i.QtyOnHand).Sum() ?? 0).ThenBy(p => p.Id);
                    }
                    else
                    {
                        orderedProducts = products.OrderBy(p => (
                            from v in _db.ProductVariants.AsNoTracking()
                            where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                            join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                            where !i.IsDeleted && i.StoreId == storeIdSort && i.IsActive
                            select (int?)i.QtyOnHand).Sum() ?? 0).ThenBy(p => p.Id);
                    }
                }
                else
                {
                    orderedProducts = products.ApplySort(sortSpec, sortMap, p => p.Id);
                }

                var baseQuery =
                    from p in products
                    let qty1 = (from v in _db.ProductVariants.AsNoTracking()
                                 where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                                 join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                 where !i.IsDeleted && i.StoreId == 1 && i.IsActive
                                 select (int?)i.QtyOnHand).Sum()
                    let qty2 = (from v in _db.ProductVariants.AsNoTracking()
                                 where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                                 join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                 where !i.IsDeleted && i.StoreId == 2 && i.IsActive
                                 select (int?)i.QtyOnHand).Sum()
                    select new
                    {
                        p.Id,
                        p.Name,
                        p.InputDt,
                        p.IsActive,
                        QtyStore1 = qty1 ?? 0,
                        QtyStore2 = qty2 ?? 0,
                        Stamp = p.Stamp
                    };

                if (storeId.HasValue && storeId.Value > 0)
                {
                    if (storeId.Value == 1) baseQuery = baseQuery.Where(x => x.QtyStore1 > 0);
                    else if (storeId.Value == 2) baseQuery = baseQuery.Where(x => x.QtyStore2 > 0);
                    else
                    {
                        var sid = storeId.Value;
                        baseQuery = from r in baseQuery
                                    let qtySelected = (from v in _db.ProductVariants.AsNoTracking()
                                                       where !v.IsDeleted && v.ProductId == r.Id && v.IsActive
                                                       join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                                       where !i.IsDeleted && i.StoreId == sid && i.IsActive
                                                       select (int?)i.QtyOnHand).Sum()
                                    where (qtySelected ?? 0) > 0
                                    select r;
                    }
                }

                var totalCount = await baseQuery.CountAsync();

                var proj =
                    from p in orderedProducts
                    let qty1 = (from v in _db.ProductVariants.AsNoTracking()
                                 where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                                 join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                 where !i.IsDeleted && i.StoreId == 1 && i.IsActive
                                 select (int?)i.QtyOnHand).Sum()
                    let qty2 = (from v in _db.ProductVariants.AsNoTracking()
                                 where !v.IsDeleted && v.ProductId == p.Id && v.IsActive
                                 join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                 where !i.IsDeleted && i.StoreId == 2 && i.IsActive
                                 select (int?)i.QtyOnHand).Sum()
                    select new
                    {
                        p.Id,
                        p.Name,
                        p.InputDt,
                        p.IsActive,
                        QtyStore1 = qty1 ?? 0,
                        QtyStore2 = qty2 ?? 0,
                        Stamp = p.Stamp
                    };

                if (storeId.HasValue && storeId.Value > 0)
                {
                    if (storeId.Value == 1) proj = proj.Where(x => x.QtyStore1 > 0);
                    else if (storeId.Value == 2) proj = proj.Where(x => x.QtyStore2 > 0);
                    else
                    {
                        var sid = storeId.Value;
                        proj = proj.Where(r => ((from v in _db.ProductVariants.AsNoTracking()
                                                 where !v.IsDeleted && v.ProductId == r.Id && v.IsActive
                                                 join i in _db.ProductInventories.AsNoTracking() on v.Id equals i.ProductVariantId
                                                 where !i.IsDeleted && i.StoreId == sid && i.IsActive
                                                 select (int?)i.QtyOnHand).Sum() ?? 0) > 0);
                    }
                }

                var rows = await proj.Skip(Math.Max(0, (pageIndex - 1) * size)).Take(size).ToListAsync();
                var items = rows.Select(r => new { id = r.Id, name = r.Name ?? "", inputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"), qtyStore1 = r.QtyStore1, qtyStore2 = r.QtyStore2, isActive = r.IsActive, stamp = r.Stamp });

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Products fetch failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetProduct(int id, [FromQuery] string? sortBy, [FromQuery] string? sortDir)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            if (id <= 0) return NotFound(new { message = "Product not found. Please verify the product ID and try again." });

            var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
            if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });

            var inputUserCode = "";
            if ((p.InputUserId ?? 0) > 0)
            {
                inputUserCode = await _db.AxUsers.AsNoTracking().Where(u => u.Id == p.InputUserId).Select(u => u.Code).FirstOrDefaultAsync() ?? "";
            }
            var lastModifiedByCode = "";
            if ((p.LastModifiedById ?? 0) > 0)
            {
                lastModifiedByCode = await _db.AxUsers.AsNoTracking().Where(u => u.Id == p.LastModifiedById).Select(u => u.Code).FirstOrDefaultAsync() ?? "";
            }

            var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new SortSpec(sortBy!, (sortDir ?? "").ToLowerInvariant() == "desc");
            var variantsQuery = _db.ProductVariants.AsNoTracking().Where(v => v.ProductId == id && !v.IsDeleted);
            var variantMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<ProductVariant, object?>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = v => v.Id,
                ["size"] = v => v.Size ?? "",
                ["color"] = v => v.Color ?? "",
                ["amount"] = v => v.Amount,
                ["isActive"] = v => v.IsActive,
                ["stamp"] = v => v.Stamp
            };
            if (sortSpec == null) variantsQuery = variantsQuery.OrderBy(v => v.Id);
            else if (string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) || string.Equals(sortSpec.By, "qtyStore2", StringComparison.OrdinalIgnoreCase))
            {
                var storeIdSort = string.Equals(sortSpec.By, "qtyStore1", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                if (sortSpec.Desc)
                {
                    var ordered = variantsQuery.OrderByDescending(v => _db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted && i.StoreId == storeIdSort).Select(i => (int?)i.QtyOnHand).Sum() ?? 0);
                    variantsQuery = System.Linq.Queryable.ThenBy((IOrderedQueryable<ProductVariant>)ordered, v => v.Id);
                }
                else
                {
                    var ordered = variantsQuery.OrderBy(v => _db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted && i.StoreId == storeIdSort).Select(i => (int?)i.QtyOnHand).Sum() ?? 0);
                    variantsQuery = System.Linq.Queryable.ThenBy((IOrderedQueryable<ProductVariant>)ordered, v => v.Id);
                }
            }
            else variantsQuery = variantsQuery.ApplySort(sortSpec, variantMap, v => v.Id);

            var variants = await variantsQuery.ToListAsync();
            var variantIds = variants.Select(v => v.Id).ToList();
            var inv = await _db.ProductInventories.AsNoTracking().Where(i => variantIds.Contains(i.ProductVariantId) && !i.IsDeleted).ToListAsync();
            var invQtyMap = inv.GroupBy(i => new { i.ProductVariantId, i.StoreId }).ToDictionary(g => (g.Key.ProductVariantId, g.Key.StoreId), g => g.Sum(x => x.QtyOnHand));
            var invStampMap = inv.GroupBy(i => new { i.ProductVariantId, i.StoreId }).ToDictionary(g => (g.Key.ProductVariantId, g.Key.StoreId), g => g.OrderByDescending(x => x.LastUpdatedDt ?? x.InputDt).FirstOrDefault()?.Stamp ?? 0);
            var vDtos = variants.Select(v => new
            {
                id = v.Id,
                size = v.Size ?? "",
                color = v.Color ?? "",
                amount = v.Amount,
                isActive = v.IsActive,
                sizeKey = v.SizeKey,
                colorKey = v.ColorKey,
                photoFileName = v.PhotoFileName,
                qtyStore1 = invQtyMap.TryGetValue((v.Id, 1), out var q1) ? q1 : 0,
                qtyStore1Stamp = invStampMap.TryGetValue((v.Id, 1), out var s1) ? s1 : 0,
                qtyStore2 = invQtyMap.TryGetValue((v.Id, 2), out var q2) ? q2 : 0,
                qtyStore2Stamp = invStampMap.TryGetValue((v.Id, 2), out var s2) ? s2 : 0,
                stamp = v.Stamp
            });

            return JsonResultHelper.StableJson(_env, new
            {
                id = p.Id,
                name = p.Name ?? "",
                inputDt = p.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                inputUserId = p.InputUserId,
                inputUserCode,
                lastModifiedById = p.LastModifiedById,
                lastModifiedByCode,
                lastUpdatedDt = p.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss"),
                isActive = p.IsActive,
                isDeleted = p.IsDeleted,
                stamp = p.Stamp,
                variants = vDtos
            });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateProduct(Dtos.ProductCreateDto dto)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                var name = (dto.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Product name is required. Please enter a name and try again." });

                var uid = CurrentUserId();
                var p = new Product
                {
                    Name = name,
                    IsActive = dto.IsActive,
                    IsDeleted = false,
                    InputDt = DateTime.Now,
                    InputUserId = uid,
                    LastModifiedById = uid,
                    LastUpdatedDt = DateTime.Now,
                    Stamp = 0
                };
                _db.Products.Add(p);
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product created", $"ProductId={p.Id}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, id = p.Id, message = "Product has been created successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Create product failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to create the product. Please try again later." });
            }
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateProduct(int id, Dtos.ProductUpdateDto dto)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. It may have been deleted by another user." });

                if (!dto.Stamp.HasValue || dto.Stamp.Value != p.Stamp)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product update conflict - stamp mismatch", $"ProductId={id}", CurrentUserId());
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                if (dto.Name != null) p.Name = dto.Name.Trim();
                if (dto.IsActive.HasValue) p.IsActive = dto.IsActive.Value;

                var uid = CurrentUserId();
                p.LastModifiedById = uid;
                p.LastUpdatedDt = DateTime.Now;
                p.Stamp = p.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product updated", $"ProductId={p.Id}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "Product has been updated successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Update product failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to update the product. Please try again later." });
            }
        }

        [HttpPut("{id:int}/details")]
        [Authorize]
        public async Task<IActionResult> SaveDetails(int id)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);

            var jsonOptions = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            await using var tx = await _db.Database.BeginTransactionAsync();

            static string? NormalizePhotoFileName(string? value)
            {
                var s = (value ?? "").Trim();
                if (string.IsNullOrWhiteSpace(s)) return null;
                s = Path.GetFileName(s);
                if (string.IsNullOrWhiteSpace(s)) return null;
                foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
                var ext = (Path.GetExtension(s) ?? "").ToLowerInvariant();
                var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
                if (!allowedExt.Contains(ext)) throw new InvalidOperationException("Invalid image format. Allowed formats: PNG, JPG/JPEG, GIF, WEBP.");
                return s;
            }

            try
            {
                var dto = await Request.ReadFromJsonAsync<Dtos.ProductDetailsUpdateDto>(jsonOptions);
                if (dto is null) return BadRequest(new { success = false, message = "Invalid JSON payload. Please check your request format and try again." });

                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });

                int uid = CurrentUserId();
                try { await LoggerHelper.LogEventAsync(_db, EventType.Information, "ProductDetails received", $"ProductId={id}; Variants={(dto.Variants?.Count ?? 0)}; DeletedIds={(dto.DeletedVariantIds?.Count ?? 0)}; StampPresent={dto.Stamp.HasValue}", uid); } catch { }

                if (!dto.Stamp.HasValue)
                {
                    if (p.Stamp != 0)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product details save conflict - missing stamp", $"ProductId={id}", uid);
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                }
                else if (dto.Stamp.Value != p.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product details save conflict - stamp mismatch", $"ProductId={id}", uid);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                if (!p.IsActive && !(dto.IsActive.HasValue && dto.IsActive.Value))
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product details save blocked - product inactive", $"ProductId={id}", uid);
                    return BadRequest(new { success = false, message = "This product is deactivated or deleted. Activate the product before modifying its variants, inventories or notes." });
                }

                if (dto.Name is not null) p.Name = dto.Name.Trim();
                if (dto.IsActive.HasValue) p.IsActive = dto.IsActive.Value;
                p.LastModifiedById = uid; p.LastUpdatedDt = DateTime.Now;

                var deletedIds = (dto.DeletedVariantIds ?? new List<int>()).Where(x => x > 0).ToList();
                if (deletedIds.Count > 0)
                {
                    var toDelete = await _db.ProductVariants.Where(v => v.ProductId == id && deletedIds.Contains(v.Id)).ToListAsync();
                    foreach (var v in toDelete)
                    {
                        v.IsDeleted = true; v.IsActive = false; v.LastModifiedById = uid; v.LastUpdatedDt = DateTime.Now;
                        var invs = await _db.ProductInventories.Where(i => i.ProductVariantId == v.Id && !i.IsDeleted).ToListAsync();
                        foreach (var inv in invs)
                        {
                            inv.IsDeleted = true; inv.IsActive = false; inv.LastModifiedById = uid; inv.LastUpdatedDt = DateTime.Now;
                        }
                    }
                }

                foreach (var vd in dto.Variants ?? Enumerable.Empty<Dtos.ProductVariantDto>())
                {
                    if (vd.IsDeleted == true) continue;

                    int vId = vd.Id.GetValueOrDefault(0);
                    string size = vd.Size ?? ""; string color = vd.Color ?? ""; decimal amount = vd.Amount; string? photo = NormalizePhotoFileName(vd.PhotoFileName); bool isActive = vd.IsActive; int qty1 = Math.Max(0, vd.QtyStore1); int qty2 = Math.Max(0, vd.QtyStore2);
                    ProductVariant? v;
                    if (vId <= 0)
                    {
                        v = new ProductVariant
                        {
                            ProductId = id,
                            Size = size,
                            Color = color,
                            Amount = amount,
                            PhotoFileName = photo,
                            IsActive = isActive,
                            IsDeleted = false,
                            InputDt = DateTime.Now,
                            InputUserId = uid,
                            LastModifiedById = uid,
                            LastUpdatedDt = DateTime.Now,
                            Stamp = 0
                        };
                        _db.ProductVariants.Add(v);
                        await _db.SaveChangesAsync();
                        try { await LoggerHelper.LogEventAsync(_db, EventType.Information, "Variant created", $"ProductId={id}; VariantId={v.Id}", uid); } catch { }
                    }
                    else
                    {
                        v = await _db.ProductVariants.FirstOrDefaultAsync(x => x.Id == vId && x.ProductId == id);
                        if (v == null) continue;
                        if (vd.Stamp.HasValue && vd.Stamp.Value != v.Stamp)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Variant save conflict - stamp mismatch", $"ProductId={id}; VariantId={vId}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Variant {vId} was changed by another user. Reload and try again." });
                        }

                        v.IsDeleted = false; v.Size = size; v.Color = color; v.Amount = amount; v.PhotoFileName = photo; v.IsActive = isActive; v.LastModifiedById = uid; v.LastUpdatedDt = DateTime.Now; v.Stamp = v.Stamp + 1;
                    }

                    static int Clamp(int n) => n < 0 ? 0 : n;

                    async Task UpsertInvAsync(int storeId, int qty, int? expectedStamp)
                    {
                        var inv = await _db.ProductInventories.FirstOrDefaultAsync(x => x.ProductVariantId == v!.Id && x.StoreId == storeId);
                        if (inv == null)
                        {
                            inv = new ProductInventory
                            {
                                ProductVariantId = v!.Id,
                                StoreId = storeId,
                                QtyOnHand = Clamp(qty),
                                IsActive = true,
                                IsDeleted = false,
                                InputDt = DateTime.Now,
                                InputUserId = uid,
                                LastModifiedById = uid,
                                LastUpdatedDt = DateTime.Now,
                                Stamp = 0
                            };
                            _db.ProductInventories.Add(inv);
                        }
                        else
                        {
                            if (expectedStamp.HasValue && expectedStamp.Value != inv.Stamp)
                            {
                                await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Inventory save conflict - stamp mismatch", $"ProductId={id}; VariantId={v!.Id}; StoreId={storeId}", uid);
                                throw new InvalidOperationException("Inventory stamp mismatch");
                            }
                            inv.IsDeleted = false; inv.QtyOnHand = Clamp(qty); inv.IsActive = true; inv.LastModifiedById = uid; inv.LastUpdatedDt = DateTime.Now; inv.Stamp = inv.Stamp + 1;
                        }
                    }

                    await UpsertInvAsync(1, qty1, vd.QtyStore1Stamp);
                    await UpsertInvAsync(2, qty2, vd.QtyStore2Stamp);
                    try { await LoggerHelper.LogEventAsync(_db, EventType.Information, "Variant inventory upserted", $"ProductId={id}; VariantTmpId={vId}; VariantRealId={v?.Id}; Qty1={qty1}; Qty2={qty2}", uid); } catch { }
                }

                await _db.SaveChangesAsync();

                if (dto.Notes != null)
                {
                    var notesDto = dto.Notes;
                    foreach (var add in notesDto.Add ?? new List<Dtos.NoteCreateDto>())
                    {
                        var text = (add.Comment ?? "").Trim(); if (string.IsNullOrWhiteSpace(text)) continue;
                        await _db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, dbo.GetLocalTime(), {CurrentUserId()}, {CurrentUserId()}, dbo.GetLocalTime(), 0);");
                    }

                    var updatedIds = (notesDto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                    foreach (var upd in notesDto.Update ?? new List<Dtos.NoteUpdateDto>())
                    {
                        var existing = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ProductId == id && !x.IsDeleted);
                        if (existing == null) continue;
                        var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                        var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                        var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                        var newIsMain = (notesDto.SetMainId.HasValue && notesDto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);
                        if (!upd.Stamp.HasValue)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Note save conflict - missing stamp for update (details)", $"ProductId={id}; NoteId={upd.Id}", CurrentUserId());
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }

                        var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
                        var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                        var pDeleted = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
                        var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
                        var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
                        var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = CurrentUserId() };
                        var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
                        var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                        var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
                        var affected = await _db.Database.ExecuteSqlRawAsync(sql, pComment, pDeleted, pActive, pMain, pUid, pNid, pPid, pStamp);
                        if (affected == 0)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Note save conflict - update (details)", $"ProductId={id}; NoteId={upd.Id}", CurrentUserId());
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }
                    }

                    var delItems = (notesDto.Delete ?? new List<Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
                    if (delItems.Count > 0)
                    {
                        var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
                        foreach (var did in delItems)
                        {
                            if (!did.Stamp.HasValue)
                            {
                                await tx.RollbackAsync();
                                await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Note save conflict - missing stamp for delete (details)", $"ProductId={id}; NoteId={did.Id}", CurrentUserId());
                                return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                            }
                            var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = CurrentUserId() };
                            var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                            var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                            var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                            var affected = await _db.Database.ExecuteSqlRawAsync(sql, pUid, pPid, pNid, pStamp);
                            if (affected == 0)
                            {
                                await tx.RollbackAsync();
                                await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Note save conflict - delete (details)", $"ProductId={id}; NoteId={did.Id}", CurrentUserId());
                                return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                            }
                        }
                    }

                    if (notesDto.SetMainId.HasValue && notesDto.SetMainId.Value > 0)
                    {
                        var targetId = notesDto.SetMainId.Value;
                        var updatedIds2 = (notesDto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                        var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
                        if (updatedIds2 != null && updatedIds2.Count > 0)
                        {
                            unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds2) + ")";
                        }
                        var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = CurrentUserId() };
                        var pPidU = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                        var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
                        await _db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pPidU, pTargetU);

                        if (!(updatedIds2?.Contains(targetId) ?? false))
                        {
                            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {CurrentUserId()}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
                        }
                    }
                }

                p.Stamp = p.Stamp + 1;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product details saved", $"ProductId={p.Id}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "Product details were successfully saved." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                try { _db.ChangeTracker.Clear(); } catch { }
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Save product details failed", ex.ToString(), CurrentUserId());
                var msg = ex is InvalidOperationException ? ex.Message : "Product details could not be saved. Please try again.";
                return JsonResultHelper.StableJson(_env, new { success = false, message = msg });
            }
        }

        [HttpPost("upload-photo")]
        [Authorize]
        public async Task<IActionResult> UploadPhoto()
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                if (!Request.HasFormContentType) return JsonResultHelper.StableJson(_env, new { success = false, message = "Expected multipart/form-data. Please submit the form with file upload." }, StatusCodes.Status400BadRequest);
                var form = await Request.ReadFormAsync(); var file = form.Files.FirstOrDefault();
                if (file == null || file.Length <= 0) return JsonResultHelper.StableJson(_env, new { success = false, message = "No image was uploaded. Please choose an image file and try again." }, StatusCodes.Status400BadRequest);

                var uploadDir = @"C:\\Projects\\Build\\InstallDocs"; Directory.CreateDirectory(uploadDir);
                var originalName = Path.GetFileName(file.FileName ?? "").Trim(); if (string.IsNullOrWhiteSpace(originalName)) originalName = "upload.bin";
                var ext = (Path.GetExtension(originalName) ?? "").ToLowerInvariant(); var allowedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
                if (!allowedExt.Contains(ext)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid image format. Allowed formats are PNG, JPG/JPEG, GIF, and WEBP." }, StatusCodes.Status400BadRequest);
                var ct = (file.ContentType ?? "").ToLowerInvariant(); if (!ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid file type. Please upload a valid image file." }, StatusCodes.Status400BadRequest);
                foreach (var ch in Path.GetInvalidFileNameChars()) originalName = originalName.Replace(ch, '_');
                var targetPath = Path.Combine(uploadDir, originalName);
                if (System.IO.File.Exists(targetPath))
                {
                    var nameNoExt = Path.GetFileNameWithoutExtension(originalName); var existingExt = Path.GetExtension(originalName); var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff"); originalName = $"{nameNoExt}_{stamp}{existingExt}"; targetPath = Path.Combine(uploadDir, originalName);
                }
                await using (var fs = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await file.CopyToAsync(fs); }
                var uid = CurrentUserId(); await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product photo uploaded", $"File={originalName}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, fileName = originalName, uploadPath = uploadDir });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Upload product photo failed", ex.ToString(), CurrentUserId());
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Image upload failed. Please try again later." });
            }
        }

        [HttpPost("{id:int}/deactivate")]
        [Authorize]
        public async Task<IActionResult> Deactivate(int id, [FromQuery] int? stamp, [FromBody] int? stampBody)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });
                var effectiveStamp = stampBody.HasValue ? stampBody : stamp;
                if (!effectiveStamp.HasValue || effectiveStamp.Value != p.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product deactivate conflict - stamp mismatch", $"ProductId={id}", CurrentUserId());
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                var uid = CurrentUserId(); var now = DateTime.Now;
                int updatedVariants = 0, updatedInventories = 0, updatedNotes = 0;
                if (_db.Database.IsSqlite())
                {
                    p.IsActive = false; p.LastModifiedById = uid; p.LastUpdatedDt = now; p.Stamp = p.Stamp + 1;
                    var variants = await _db.ProductVariants.Where(v => v.ProductId == id && !v.IsDeleted).ToListAsync();
                    foreach (var v in variants) { v.IsActive = false; v.LastModifiedById = uid; v.LastUpdatedDt = now; v.Stamp = v.Stamp + 1; }
                    updatedVariants = variants.Count;
                    var vIds = variants.Select(v => v.Id).ToList();
                    var inventories = await _db.ProductInventories.Where(i => vIds.Contains(i.ProductVariantId) && !i.IsDeleted).ToListAsync();
                    foreach (var i in inventories) { i.IsActive = false; i.LastModifiedById = uid; i.LastUpdatedDt = now; i.Stamp = i.Stamp + 1; }
                    updatedInventories = inventories.Count;
                    var notes = await _db.Notes.Where(n => n.ProductId == id && !n.IsDeleted).ToListAsync();
                    foreach (var n in notes) { n.IsActive = false; n.LastModifiedById = uid; n.LastUpdatedDt = now; n.Stamp = n.Stamp + 1; }
                    updatedNotes = notes.Count;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    var updatedProduct = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                    updatedVariants = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                    updatedInventories = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
                    updatedNotes = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 0, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                }
                await tx.CommitAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product deactivated", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
                return Ok(new { success = true, message = "Product has been deactivated successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Deactivate product failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/activate")]
        [Authorize]
        public async Task<IActionResult> Activate(int id, [FromQuery] int? stamp, [FromBody] int? stampBody)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            try
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });
                var uid = CurrentUserId();
                var effectiveStamp = stampBody.HasValue ? stampBody : stamp;
                if (!effectiveStamp.HasValue || effectiveStamp.Value != p.Stamp)
                {
                    await tx.RollbackAsync();
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Product activate conflict - stamp mismatch", $"ProductId={id}", uid);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to product {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                var now = DateTime.Now;
                int updatedVariants = 0, updatedInventories = 0, updatedNotes = 0;
                if (_db.Database.IsSqlite())
                {
                    p.IsActive = true; p.LastModifiedById = uid; p.LastUpdatedDt = now; p.Stamp = p.Stamp + 1;
                    var variants = await _db.ProductVariants.Where(v => v.ProductId == id && !v.IsDeleted).ToListAsync();
                    foreach (var v in variants) { v.IsActive = true; v.LastModifiedById = uid; v.LastUpdatedDt = now; v.Stamp = v.Stamp + 1; }
                    updatedVariants = variants.Count;
                    var vIds = variants.Select(v => v.Id).ToList();
                    var inventories = await _db.ProductInventories.Where(i => vIds.Contains(i.ProductVariantId) && !i.IsDeleted).ToListAsync();
                    foreach (var i in inventories) { i.IsActive = true; i.LastModifiedById = uid; i.LastUpdatedDt = now; i.Stamp = i.Stamp + 1; }
                    updatedInventories = inventories.Count;
                    var notes = await _db.Notes.Where(n => n.ProductId == id && !n.IsDeleted).ToListAsync();
                    foreach (var n in notes) { n.IsActive = true; n.LastModifiedById = uid; n.LastUpdatedDt = now; n.Stamp = n.Stamp + 1; }
                    updatedNotes = notes.Count;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    var updatedProduct = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                    updatedVariants = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                    updatedInventories = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
                    updatedNotes = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                }
                await tx.CommitAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product activated", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
                return Ok(new { success = true, message = "Product has been activated successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Activate product failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost("{id:int}/delete")]
        [Authorize]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!CanManageProducts()) return StatusCode(StatusCodes.Status403Forbidden);
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });
                var uid = CurrentUserId(); var now = DateTime.Now;
                var updatedProduct = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                var updatedVariants = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_variant SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                var updatedInventories = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.product_inventory SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_variant_id IN (SELECT product_variant_id FROM dbo.product_variant WHERE product_id = {id} AND is_deleted = 0) AND is_deleted = 0;");
                var updatedNotes = await _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE dbo.note SET is_active = 0, is_deleted = 1, last_modified_by_id = {uid}, last_updated_dt = {now}, stamp = stamp + 1 WHERE product_id = {id} AND is_deleted = 0;");
                await tx.CommitAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Product deleted", $"ProductId={p.Id}; variantsUpdated={updatedVariants}; inventoriesUpdated={updatedInventories}; notesUpdated={updatedNotes}", uid);
                return Ok(new { success = true, message = "Product has been deleted successfully.", variantsUpdated = updatedVariants, inventoriesUpdated = updatedInventories, notesUpdated = updatedNotes });
            }
            catch (Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Delete product failed", ex.ToString(), CurrentUserId());
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}
