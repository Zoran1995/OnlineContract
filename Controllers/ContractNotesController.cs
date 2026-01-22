using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Infrastructure;
using OnlineContract.Helpers;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/contracts/{id:int}/notes")]
    public class ContractNotesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ContractNotesController(AppDbContext db, IHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetNotes(int id, string? q, int page, int pageSize)
        {
            if (!HttpContext.User?.Identity?.IsAuthenticated ?? true) return StatusCode(StatusCodes.Status401Unauthorized);
            try
            {
                var c = await _db.Contracts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (c == null) return StatusCode(StatusCodes.Status404NotFound);

                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                IQueryable<Models.Note> notes = _db.Notes.AsNoTracking().Where(n => n.ContractId == id && !n.IsDeleted);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var s = q.Trim();
                    notes = notes.Where(n => EF.Functions.Like(n.Comment ?? "", $"%{s}%"));
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
                                    inputUserCode = inputUserCode ?? "",
                                    lastModifiedByCode = lastModifiedByCode ?? "",
                                    n.LastUpdatedDt,
                                    n.Stamp
                                };

                var totalCount = await baseQuery.CountAsync();
                var rows = await baseQuery.OrderByDescending(x => x.InputDt)
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
                    IsDeleted = r.IsDeleted,
                    InputDt = r.InputDt.ToString("yyyy-MM-dd HH:mm:ss"),
                    InputUserId = null,
                    InputUserCode = r.inputUserCode,
                    LastModifiedById = null,
                    LastModifiedByCode = r.lastModifiedByCode,
                    LastUpdatedDt = r.LastUpdatedDt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    Stamp = r.Stamp
                });

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size) });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Contract notes fetch failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>(), totalCount = 0, totalPages = 0 });
            }
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> SaveNotes(int id)
        {
            if (!HttpContext.User?.Identity?.IsAuthenticated ?? true) return StatusCode(StatusCodes.Status401Unauthorized);
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var c = await _db.Contracts.FirstOrDefaultAsync(x => x.Id == id);
                if (c == null) return StatusCode(StatusCodes.Status404NotFound);

                int uid = Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext);
                var dto = await Request.ReadFromJsonAsync<Dtos.NotesBulkSaveDto>();
                if (dto == null) return StatusCode(StatusCodes.Status400BadRequest);

                var isSqlite = _db.Database.ProviderName?.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isSqlite)
                {
                    var autoNotes = await _db.Notes.AsNoTracking()
                        .Where(n => n.ContractId == id && !n.IsDeleted && (n.LastModifiedById == 2) && (n.Subject ?? "").StartsWith("Changed Contract state from "))
                        .Select(n => n.Id)
                        .ToListAsync();
                    if ((dto.Update?.Any(u => autoNotes.Contains(u.Id)) ?? false) || (dto.Delete?.Any(d => autoNotes.Contains(d.Id)) ?? false))
                    {
                        await tx.RollbackAsync();
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Automatic workflow notes cannot be edited or deleted." });
                    }

                    foreach (var add in dto.Add ?? new List<Dtos.NoteCreateDto>())
                    {
                        var comment = (add.Comment ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(comment)) continue;
                        var sqlIns = "INSERT INTO dbo.note (product_id, contract_id, comment, subject, is_main, is_deleted, is_active, input_dt, input_user_id, last_modified_by_id, last_updated_dt, stamp) VALUES (NULL, @pId, @pComment, @pSubject, 0, 0, @pActive, dbo.GetLocalTime(), @pUid, @pUid, dbo.GetLocalTime(), 0);";
                        var pId = new Microsoft.Data.SqlClient.SqlParameter("@pId", System.Data.SqlDbType.Int) { Value = id };
                        var pComment = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)comment };
                        var pSubject = new Microsoft.Data.SqlClient.SqlParameter("@pSubject", System.Data.SqlDbType.NVarChar, 255) { Value = (object)((add.Subject ?? "").Trim()) };
                        var pActive = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (add.IsActive ? 1 : 0) };
                        var pUid = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                        await _db.Database.ExecuteSqlRawAsync(sqlIns, pId, pComment, pSubject, pActive, pUid);
                    }

                    var updatedIds2 = (dto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                    foreach (var upd in dto.Update ?? new List<Dtos.NoteUpdateDto>())
                    {
                        var existing = await _db.Notes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == upd.Id && x.ContractId == id && !x.IsDeleted);
                        if (existing == null) continue;
                        var newComment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                        var newIsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                        var newIsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                        var newIsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? 1 : (existing.IsMain ? 1 : 0);

                        if (!upd.Stamp.HasValue)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - update", $"ContractId={id}; NoteId={upd.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }

                        var sql = "UPDATE dbo.note SET comment = @pComment, is_deleted = @pDeleted, is_active = @pActive, is_main = @pMain, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE note_id = @pNid AND contract_id = @pCid AND stamp = @pStamp;";
                        var pCommentU = new Microsoft.Data.SqlClient.SqlParameter("@pComment", System.Data.SqlDbType.NVarChar, -1) { Value = (object)(newComment ?? string.Empty) };
                        var pDeletedU = new Microsoft.Data.SqlClient.SqlParameter("@pDeleted", System.Data.SqlDbType.Int) { Value = (newIsDeleted ? 1 : 0) };
                        var pActiveU = new Microsoft.Data.SqlClient.SqlParameter("@pActive", System.Data.SqlDbType.Int) { Value = (newIsActive ? 1 : 0) };
                        var pMainU = new Microsoft.Data.SqlClient.SqlParameter("@pMain", System.Data.SqlDbType.Int) { Value = newIsMain };
                        var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                        var pNidU = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = upd.Id };
                        var pCid = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                        var pStampU = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = upd.Stamp.Value };
                        var affectedU = await _db.Database.ExecuteSqlRawAsync(sql, pCommentU, pDeletedU, pActiveU, pMainU, pUidU, pNidU, pCid, pStampU);
                        if (affectedU == 0)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - update", $"ContractId={id}; NoteId={upd.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }
                    }

                    var delItems = (dto.Delete ?? new List<Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
                    if (delItems.Count > 0)
                    {
                        var sqlDel = "UPDATE dbo.note SET is_deleted = 1, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE contract_id = @pCid AND note_id = @pNid AND stamp = @pStamp;";
                        foreach (var did in delItems)
                        {
                            if (!did.Stamp.HasValue)
                            {
                                await tx.RollbackAsync();
                                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - delete", $"ContractId={id}; NoteId={did.Id}", uid);
                                return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                            }
                            var pUidD = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                            var pCidD = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                            var pNidD = new Microsoft.Data.SqlClient.SqlParameter("@pNid", System.Data.SqlDbType.Int) { Value = did.Id };
                            var pStampD = new Microsoft.Data.SqlClient.SqlParameter("@pStamp", System.Data.SqlDbType.Int) { Value = did.Stamp.Value };
                            var affectedD = await _db.Database.ExecuteSqlRawAsync(sqlDel, pUidD, pCidD, pNidD, pStampD);
                            if (affectedD == 0)
                            {
                                await tx.RollbackAsync();
                                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - delete", $"ContractId={id}; NoteId={did.Id}", uid);
                                return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                            }
                        }
                    }

                    if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
                    {
                        var targetId = dto.SetMainId.Value;
                        var updatedIds = (dto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                        var unsetSql = "UPDATE dbo.note SET is_main = 0, last_modified_by_id = @pUid, last_updated_dt = dbo.GetLocalTime() WHERE contract_id = @pCid AND is_deleted = 0 AND is_main = 1 AND note_id != @pTarget";
                        if (updatedIds.Count > 0)
                        {
                            unsetSql += " AND note_id NOT IN (" + string.Join(',', updatedIds) + ")";
                        }
                        var pUidU = new Microsoft.Data.SqlClient.SqlParameter("@pUid", System.Data.SqlDbType.Int) { Value = uid };
                        var pCidU = new Microsoft.Data.SqlClient.SqlParameter("@pCid", System.Data.SqlDbType.Int) { Value = id };
                        var pTargetU = new Microsoft.Data.SqlClient.SqlParameter("@pTarget", System.Data.SqlDbType.Int) { Value = targetId };
                        await _db.Database.ExecuteSqlRawAsync(unsetSql, pUidU, pCidU, pTargetU);

                        if (!updatedIds.Contains(targetId))
                        {
                            await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.note SET is_main = 1, last_modified_by_id = {uid}, last_updated_dt = dbo.GetLocalTime() WHERE note_id = {targetId} AND contract_id = {id} AND is_deleted = 0;");
                        }
                    }
                }
                else
                {
                    foreach (var add in dto.Add ?? new List<Dtos.NoteCreateDto>())
                    {
                        var comment = (add.Comment ?? "").Trim();
                        if (string.IsNullOrWhiteSpace(comment)) continue;
                        var note = new Models.Note
                        {
                            ProductId = null,
                            ContractId = id,
                            Comment = comment,
                            Subject = (add.Subject ?? "").Trim(),
                            IsMain = false,
                            IsDeleted = false,
                            IsActive = add.IsActive,
                            InputDt = DateTime.Now,
                            InputUserId = uid,
                            LastModifiedById = uid,
                            LastUpdatedDt = DateTime.Now,
                            Stamp = 0
                        };
                        _db.Notes.Add(note);
                    }

                    var updatedIds2 = (dto.Update ?? new List<Dtos.NoteUpdateDto>()).Select(u => u.Id).ToList();
                    foreach (var upd in dto.Update ?? new List<Dtos.NoteUpdateDto>())
                    {
                        var existing = await _db.Notes.FirstOrDefaultAsync(x => x.Id == upd.Id && x.ContractId == id && !x.IsDeleted);
                        if (existing == null) continue;
                        if (!upd.Stamp.HasValue || upd.Stamp.Value != existing.Stamp)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - update", $"ContractId={id}; NoteId={upd.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {upd.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }

                        existing.Comment = (upd.Comment != null) ? upd.Comment.Trim() : existing.Comment;
                        existing.IsActive = upd.IsActive.HasValue ? upd.IsActive.Value : existing.IsActive;
                        existing.IsDeleted = upd.IsDeleted.HasValue ? upd.IsDeleted.Value : existing.IsDeleted;
                        existing.IsMain = (dto.SetMainId.HasValue && dto.SetMainId.Value == upd.Id) ? true : existing.IsMain;
                        existing.LastModifiedById = uid;
                        existing.LastUpdatedDt = DateTime.Now;
                        existing.Stamp = existing.Stamp + 1;
                    }

                    var delItems = (dto.Delete ?? new List<Dtos.NoteDeleteDto>()).Where(x => x.Id > 0).ToList();
                    foreach (var did in delItems)
                    {
                        var existing = await _db.Notes.FirstOrDefaultAsync(x => x.Id == did.Id && x.ContractId == id && !x.IsDeleted);
                        if (existing == null) continue;
                        if (!did.Stamp.HasValue || did.Stamp.Value != existing.Stamp)
                        {
                            await tx.RollbackAsync();
                            await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Warning, "Note save conflict - delete", $"ContractId={id}; NoteId={did.Id}", uid);
                            return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to note {did.Id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                        }
                        existing.IsDeleted = true;
                        existing.LastModifiedById = uid;
                        existing.LastUpdatedDt = DateTime.Now;
                        existing.Stamp = existing.Stamp + 1;
                    }

                    if (dto.SetMainId.HasValue && dto.SetMainId.Value > 0)
                    {
                        var targetId = dto.SetMainId.Value;
                        var others = await _db.Notes.Where(n => n.ContractId == id && !n.IsDeleted && n.Id != targetId).ToListAsync();
                        foreach (var n in others)
                        {
                            n.IsMain = false;
                            n.LastModifiedById = uid;
                            n.LastUpdatedDt = DateTime.Now;
                        }
                        if (!(updatedIds2.Contains(targetId)))
                        {
                            var tgt = await _db.Notes.FirstOrDefaultAsync(n => n.Id == targetId && n.ContractId == id && !n.IsDeleted);
                            if (tgt != null)
                            {
                                tgt.IsMain = true;
                                tgt.LastModifiedById = uid;
                                tgt.LastUpdatedDt = DateTime.Now;
                            }
                        }
                    }

                    await _db.SaveChangesAsync();
                }
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Information, "Contract notes saved", $"ContractId={id}", uid);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "All note changes have been saved successfully." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                try { _db.ChangeTracker.Clear(); } catch { }
                await LoggerHelper.LogEventAsync(_db, Helpers.EventType.Error, "Save contract notes failed", ex.ToString(), Infrastructure.UserContextHelper.GetCurrentUserId(HttpContext));
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to save notes. Please try again later." });
            }
        }
    }
}
