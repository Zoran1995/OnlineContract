using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/notes")]
    public class NotesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public NotesController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> List([FromQuery] int? contractId, [FromQuery] int? productId, [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null)
        {
            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                IQueryable<Models.Note> notes = _db.Notes.AsNoTracking().Where(n => !n.IsDeleted);
                if (contractId.HasValue && contractId.Value > 0)
                    notes = notes.Where(n => n.ContractId == contractId.Value);
                if (productId.HasValue && productId.Value > 0)
                    notes = notes.Where(n => n.ProductId == productId.Value);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    notes = notes.Where(n => EF.Functions.Like(n.Comment ?? "", $"%{s}%") || EF.Functions.Like(n.Subject ?? "", $"%{s}%"));
                }

                var baseQuery = from n in notes
                                let inputUserCode = (from u in _db.AxUsers.AsNoTracking()
                                                     where u.Id == n.InputUserId
                                                     select u.Code).FirstOrDefault()
                                let lastModifiedByCode = (from u in _db.AxUsers.AsNoTracking()
                                                          where u.Id == n.LastModifiedById
                                                          select u.Code).FirstOrDefault()
                                select new
                                {
                                    n.Id,
                                    n.Subject,
                                    n.Comment,
                                    n.IsActive,
                                    n.IsMain,
                                    n.IsDeleted,
                                    n.InputDt,
                                    n.InputUserId,
                                    inputUserCode = inputUserCode ?? "",
                                    n.LastModifiedById,
                                    lastModifiedByCode = lastModifiedByCode ?? "",
                                    n.LastUpdatedDt,
                                    n.Stamp,
                                    n.ProductId,
                                    n.ContractId
                                };

                // Sorting
                IOrderedQueryable<dynamic> ordered;
                bool desc = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
                switch ((sortBy ?? "").ToLowerInvariant())
                {
                    case "id":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.Id) : baseQuery.OrderBy(x => x.Id);
                        break;
                    case "contractid":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.ContractId) : baseQuery.OrderBy(x => x.ContractId);
                        break;
                    case "productid":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.ProductId) : baseQuery.OrderBy(x => x.ProductId);
                        break;
                    case "subject":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.Subject) : baseQuery.OrderBy(x => x.Subject);
                        break;
                    case "inputdt":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.InputDt) : baseQuery.OrderBy(x => x.InputDt);
                        break;
                    case "inputuserid":
                        ordered = desc ? baseQuery.OrderByDescending(x => x.InputUserId) : baseQuery.OrderBy(x => x.InputUserId);
                        break;
                    case "status":
                        // Approximate: active first, then main
                        ordered = desc ? baseQuery.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.IsMain) : baseQuery.OrderBy(x => x.IsActive).ThenBy(x => x.IsMain);
                        break;
                    default:
                        ordered = baseQuery.OrderByDescending(x => x.InputDt);
                        break;
                }

                var totalCount = await baseQuery.CountAsync();
                var rows = await ordered
                    .Skip(Math.Max(0, (pageIndex - 1) * size))
                    .Take(size)
                    .ToListAsync();

                var items = rows.Select(r => new
                {
                    id = r.Id,
                    subject = r.Subject ?? "",
                    comment = r.Comment ?? "",
                    isActive = r.IsActive,
                    isMain = r.IsMain,
                    isDeleted = r.IsDeleted,
                    inputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    inputUserId = r.InputUserId,
                    inputUserCode = r.inputUserCode,
                    lastModifiedById = r.LastModifiedById,
                    lastModifiedByCode = r.lastModifiedByCode,
                    lastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    stamp = r.Stamp,
                    productId = r.ProductId,
                    contractId = r.ContractId,
                    status = (r.IsMain ? "Main" : (r.IsActive ? "Active" : "Inactive"))
                });

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Notes fetch failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetOne(int id)
        {
            if (id <= 0) return JsonResultHelper.StableJson(_env, new { message = "Note not found." }, StatusCodes.Status404NotFound);
            try
            {
                var n = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (n == null) return JsonResultHelper.StableJson(_env, new { message = "Note not found." }, StatusCodes.Status404NotFound);
                var inputUserCode = await _db.AxUsers.AsNoTracking().Where(u => u.Id == n.InputUserId).Select(u => u.Code).FirstOrDefaultAsync() ?? "";
                var lastModifiedByCode = await _db.AxUsers.AsNoTracking().Where(u => u.Id == n.LastModifiedById).Select(u => u.Code).FirstOrDefaultAsync() ?? "";

                return JsonResultHelper.StableJson(_env, new
                {
                    id = n.Id,
                    subject = n.Subject ?? "",
                    comment = n.Comment ?? "",
                    isActive = n.IsActive,
                    isMain = n.IsMain,
                    isDeleted = n.IsDeleted,
                    inputDt = n.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    inputUserId = n.InputUserId,
                    inputUserCode,
                    lastModifiedById = n.LastModifiedById,
                    lastModifiedByCode,
                    lastUpdatedDt = n.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    stamp = n.Stamp,
                    productId = n.ProductId,
                    contractId = n.ContractId
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Notes fetch failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { message = "Failed to load note." }, StatusCodes.Status500InternalServerError);
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create(Dtos.NoteCreateSimpleDto dto)
        {
            try
            {
                var subject = (dto.Subject ?? "").Trim();
                var comment = (dto.Comment ?? "").Trim();
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(comment))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Both Subject and Comment are required." }, StatusCodes.Status400BadRequest);

                bool hasContract = dto.ContractId.HasValue && dto.ContractId.Value > 0;
                bool hasProduct = dto.ProductId.HasValue && dto.ProductId.Value > 0;
                if (!hasContract && !hasProduct)
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Please provide either a valid Contract Id or Product Id." }, StatusCodes.Status400BadRequest);
                if (hasContract && hasProduct)
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Provide only one target: Contract Id or Product Id, not both." }, StatusCodes.Status400BadRequest);

                if (hasContract)
                {
                    var existsC = await _db.Contracts.AsNoTracking().AnyAsync(c => c.Id == dto.ContractId!.Value);
                    if (!existsC) return JsonResultHelper.StableJson(_env, new { success = false, message = $"Contract with id {dto.ContractId!.Value} was not found." }, StatusCodes.Status400BadRequest);
                }
                if (hasProduct)
                {
                    var existsP = await _db.Products.AsNoTracking().AnyAsync(p => p.Id == dto.ProductId!.Value && p.IsActive && !p.IsDeleted);
                    if (!existsP) return JsonResultHelper.StableJson(_env, new { success = false, message = $"Product with id {dto.ProductId!.Value} was not found or is not active." }, StatusCodes.Status400BadRequest);
                }

                int uid = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                var isSqlite = _db.Database.ProviderName?.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isSqlite)
                {
                    var note = new Models.Note
                    {
                        ProductId = hasProduct ? dto.ProductId : null,
                        ContractId = hasContract ? dto.ContractId : null,
                        Subject = subject,
                        Comment = comment,
                        IsMain = false,
                        IsDeleted = false,
                        IsActive = dto.IsActive ?? true,
                        InputDt = DateTime.Now,
                        InputUserId = uid,
                        LastModifiedById = uid,
                        LastUpdatedDt = DateTime.Now,
                        Stamp = 0
                    };
                    _db.Notes.Add(note);
                    await _db.SaveChangesAsync();
                    return JsonResultHelper.StableJson(_env, new { success = true, id = note.Id });
                }
                else
                {
                    var sql = "INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES (@pPid, @pCid, @pComment, @pSubject, 0, 0, @pActive, dbo.GetLocalTime(), @pUid, @pUid, dbo.GetLocalTime(), 0);";
                    var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = (object?)dto.ProductId ?? DBNull.Value };
                    var pCid = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = (object?)dto.ContractId ?? DBNull.Value };
                    var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)comment };
                    var pSubject = new Microsoft.Data.SqlClient.SqlParameter("@pSubject", System.Data.SqlDbType.NVarChar, 255) { Value = (object)subject };
                    var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = ((dto.IsActive ?? true) ? 1 : 0) };
                    var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                    await _db.Database.ExecuteSqlRawAsync(sql, pPid, pCid, pComment, pSubject, pActive, pUid);
                    // fetch newly inserted id (best-effort by timestamp + user)
                    var lastId = await _db.Notes.AsNoTracking()
                        .Where(n => n.InputUserId == uid)
                        .OrderByDescending(n => n.InputDt)
                        .Select(n => n.Id)
                        .FirstOrDefaultAsync();
                    return JsonResultHelper.StableJson(_env, new { success = true, id = lastId });
                }
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Note create failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to save the note. Please try again later." });
            }
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateSimple(int id, Dtos.NoteCreateSimpleDto dto)
        {
            if (id <= 0) return JsonResultHelper.StableJson(_env, new { message = "Note not found. Please verify the note ID and try again." }, StatusCodes.Status404NotFound);
            try
            {
                var n = await _db.Notes.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (n == null) return JsonResultHelper.StableJson(_env, new { message = "Note not found. The note may have been removed." }, StatusCodes.Status404NotFound);

                if (n.ProductId.HasValue)
                {
                    var parent = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == n.ProductId.Value);
                    if (parent == null || !parent.IsActive || parent.IsDeleted)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Note for this product cannot be changed because this product is deactivated or deleted." }, StatusCodes.Status400BadRequest);
                    }
                }

                var uid = UserContextHelper.GetCurrentUserId(HttpContext);

                var newSubject = dto.Subject != null ? dto.Subject.Trim() : n.Subject ?? string.Empty;
                var newComment = dto.Comment != null ? dto.Comment.Trim() : n.Comment ?? string.Empty;
                int? newContractId = dto.ContractId.HasValue ? ((dto.ContractId.Value > 0) ? dto.ContractId : null) : n.ContractId;
                int? newProductId = dto.ProductId.HasValue ? ((dto.ProductId.Value > 0) ? dto.ProductId : null) : n.ProductId;

                var now = DateTime.Now;

                if (newContractId.HasValue)
                {
                    var existsC = await _db.Contracts.AsNoTracking().AnyAsync(c => c.Id == newContractId.Value && c.IsActive && !c.IsDeleted);
                    if (!existsC) return JsonResultHelper.StableJson(_env, new { success = false, message = $"Contract with id {newContractId.Value} was not found or is not active. Please provide a valid, active Contract Id from the Contracts list." }, StatusCodes.Status400BadRequest);
                }
                if (newProductId.HasValue)
                {
                    var existsP = await _db.Products.AsNoTracking().AnyAsync(p => p.Id == newProductId.Value && p.IsActive && !p.IsDeleted);
                    if (!existsP) return JsonResultHelper.StableJson(_env, new { success = false, message = $"Product with id {newProductId.Value} was not found or is not active. Please provide a valid, active Product Id from the Products list." }, StatusCodes.Status400BadRequest);
                }

                bool newIsActive = dto.IsActive.HasValue ? dto.IsActive.Value : n.IsActive;

                int mainVal;
                if (dto.IsMain.HasValue)
                {
                    mainVal = dto.IsMain.Value ? 1 : 0;
                    var targetHasProduct = newProductId.HasValue || (n.ProductId.HasValue && n.ProductId.Value > 0);
                    if (dto.IsMain.Value && !targetHasProduct)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "A note can be marked as 'Main' only when it is linked to a product. Please set Product Id first." }, StatusCodes.Status400BadRequest);
                    }
                }
                else
                {
                    mainVal = n.IsMain ? 1 : 0;
                }

                if (_db.Database.IsSqlite())
                {
                    n.Comment = newComment;
                    n.Subject = newSubject;
                    n.ContractId = newContractId;
                    n.ProductId = newProductId;
                    n.IsMain = (mainVal == 1);
                    n.IsActive = newIsActive;
                    n.LastModifiedById = uid;
                    n.LastUpdatedDt = now;
                    n.Stamp = n.Stamp + 1;
                    await _db.SaveChangesAsync();
                }
                else
                {
                    var updSql = $@"UPDATE dbo.note SET comment = @pComment, subject = @pSubject, contract_id = @pContract, product_id = @pProduct, is_main = @pMain, is_active = @pIsActive, last_modified_by_id = @pUid, last_updated_dt = @pNow WHERE note_id = @pNid;";
                    var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                    var pSubject = new Microsoft.Data.SqlClient.SqlParameter("@pSubject", System.Data.SqlDbType.NVarChar, 250) { Value = (object)(newSubject ?? string.Empty) };
                    var pContract = new Microsoft.Data.SqlClient.SqlParameter("@pContract", System.Data.SqlDbType.Int) { Value = (object?)newContractId ?? DBNull.Value };
                    var pProduct = new Microsoft.Data.SqlClient.SqlParameter("@pProduct", System.Data.SqlDbType.Int) { Value = (object?)newProductId ?? DBNull.Value };
                    var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                    var pNow = new Microsoft.Data.SqlClient.SqlParameter("@pNow", System.Data.SqlDbType.DateTime2) { Value = now };
                    var pIsActive = new Microsoft.Data.SqlClient.SqlParameter("@pIsActive", System.Data.SqlDbType.Bit) { Value = newIsActive };
                    var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = mainVal };
                    var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = id };
                    var affected = await _db.Database.ExecuteSqlRawAsync(updSql, pComment, pSubject, pContract, pProduct, pMain, pIsActive, pUid, pNow, pNid);
                    if (affected == 0)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "The note could not be updated (it may have been changed by another user)." });
                    }
                }

                var refreshed = await _db.Notes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Stamp, x.LastUpdatedDt }).FirstOrDefaultAsync();
                var lastModifiedByCode = await _db.AxUsers.Where(u => u.Id == uid).Select(u => u.Code).FirstOrDefaultAsync();

                return JsonResultHelper.StableJson(_env, new
                {
                    success = true,
                    id = id,
                    stamp = refreshed?.Stamp ?? 0,
                    lastUpdatedDt = (refreshed?.LastUpdatedDt.HasValue == true)
                        ? refreshed.LastUpdatedDt!.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        : now.ToString("yyyy-MM-dd HH:mm:ss"),
                    lastModifiedByCode = lastModifiedByCode ?? ""
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Note update failed", ex.ToString(), UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to save the note. Please try again later." });
            }
        }
    }
}
