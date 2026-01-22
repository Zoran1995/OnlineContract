using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/products/{id:int}/notes")]
    public class ProductNotesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ProductNotesController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetNotes(int id, string? q, int page, int pageSize)
        {
            if (!Infrastructure.UserContextHelper.CanManageProducts(HttpContext)) return StatusCode(StatusCodes.Status403Forbidden);

            try
            {
                var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });

                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                IQueryable<Models.Note> notes = _db.Notes
                    .AsNoTracking()
                    .Where(n => n.ProductId == id && !n.IsDeleted);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    notes = notes.Where(n => EF.Functions.Like(n.Comment ?? "", $"%{s}%"));
                }

                var baseQuery =
                    from n in notes
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
                        n.InputDt,
                        inputUserCode = inputUserCode ?? "",
                        lastModifiedByCode = lastModifiedByCode ?? "",
                        n.LastUpdatedDt,
                        n.Stamp
                    };

                var totalCount = await baseQuery.CountAsync();
                var rows = await baseQuery
                    .OrderByDescending(x => x.InputDt)
                    .Skip(Math.Max(0, (pageIndex - 1) * size))
                    .Take(size)
                    .ToListAsync();

                var items = rows.Select(r => new Dtos.NoteDto
                {
                    Id = r.Id,
                    Subject = r.Subject ?? "",
                    Comment = r.Comment ?? "",
                    IsActive = r.IsActive,
                    IsMain = r.IsMain,
                    InputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    InputUserId = null,
                    InputUserCode = r.inputUserCode,
                    LastModifiedById = null,
                    LastModifiedByCode = r.lastModifiedByCode,
                    LastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    Stamp = r.Stamp
                });

                return JsonResultHelper.StableJson(_env, new
                {
                    items,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)size)
                });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Product notes fetch failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> SaveNotes(int id, Dtos.NotesBulkSaveDto dto)
        {
            if (!Infrastructure.UserContextHelper.CanManageProducts(HttpContext)) return StatusCode(StatusCodes.Status403Forbidden);

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
                if (p == null) return NotFound(new { message = "Product not found. The product may have been removed." });

                int uid = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
                {
                    var targetCheckId = dto.SetMainId.Value;
                    var targetNote = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == targetCheckId && n.ProductId == id && !n.IsDeleted);
                    if (targetNote == null)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "The selected note was not found. Please refresh and try again." });
                    }
                    if (!targetNote.IsActive)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Cannot set an inactive note as main. Please activate the note first and try again." });
                    }
                    if (targetNote.IsMain)
                    {
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "This note is already set as main. No changes were made." });
                    }
                    if (!dto.SetMainStamp.HasValue || dto.SetMainStamp.Value != targetNote.Stamp)
                    {
                        await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - setmain stamp mismatch", $"ProductId={id}; NoteId={targetCheckId}", Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {targetCheckId} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                }

                foreach (var add in dto.Add ?? new List<Dtos.NoteCreateDto>())
                {
                    var text = (add.Comment ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp)
                        VALUES ({id}, NULL, {text}, '', 0, 0, {(add.IsActive ? 1 : 0)}, dbo.GetLocalTime(), {uid}, {uid}, dbo.GetLocalTime(), 0);");
                }

                var updatedIds = (dto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                foreach (var upd in dto.Update ?? new List<Dtos.NoteUpdateDto>())
                {
                    var existing = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ProductId == id && !x.IsDeleted);
                    if (existing == null) continue;
                    var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                    var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                    var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                    var newIsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);
                    if (!upd.Stamp.HasValue)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - update", $"ProductId={id}; NoteId={upd.Id}", uid);
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }

                    var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND product_id = @pPid AND stamp = @pStamp;";
                    var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                    var pDeleted = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
                    var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
                    var pMain = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
                    var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                    var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
                    var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                    var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
                    var affected = await _db.Database.ExecuteSqlRawAsync(sql, pComment, pDeleted, pActive, pMain, pUid, pNid, pPid, pStamp);
                    if (affected == 0)
                    {
                        await tx.RollbackAsync();
                        await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - update", $"ProductId={id}; NoteId={upd.Id}", uid);
                        return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                    }
                }

                var delItems = (dto.Delete ?? new List<Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
                if (delItems.Count > 0)
                {
                    var sql = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND note_id = @pNid AND stamp = @pStamp;";
                    foreach (var did in delItems)
                    {
                        if (!did.Stamp.HasValue)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - delete", $"ProductId={id}; NoteId={did.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }
                        var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                        var pPid = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                        var pNid = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                        var pStamp = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                        var affected = await _db.Database.ExecuteSqlRawAsync(sql, pUid, pPid, pNid, pStamp);
                        if (affected == 0)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - delete", $"ProductId={id}; NoteId={did.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }
                    }
                }

                if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
                {
                    var targetId = dto.SetMainId.Value;
                    var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE product_id = @pPid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
                    if (updatedIds != null && updatedIds.Count > 0)
                    {
                        unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds) + ")";
                    }
                    var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                    var pPidU = new Microsoft.Data.SqlClient.SqlParameter("@pPid", System.Data.SqlDbType.Int) { Value = id };
                    var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
                    await _db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pPidU, pTargetU);

                    if (!(updatedIds?.Contains(targetId) ?? false))
                    {
                        await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND product_id = {id} AND is_deleted = 0;");
                    }
                }
                await tx.CommitAsync();

                var totalNotes = await _db.Notes.CountAsync(n => n.ProductId == id && !n.IsDeleted);
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Information, "Product notes saved", $"ProductId={id}; totalNotes={totalNotes}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "All note changes have been saved successfully.", totalNotes });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                try { _db.ChangeTracker.Clear(); } catch { }
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Save product notes failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to save notes. Please try again later." });
            }
        }
    }
}
