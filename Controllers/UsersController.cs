using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using OnlineContract.Data;
using OnlineContract.Dtos;
using OnlineContract.Helpers;
using OnlineContract.Models;
using Microsoft.Extensions.Hosting;
using OnlineContract.Infrastructure;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;
        public UsersController(AppDbContext db, IHostEnvironment env) { _db = db; _env = env; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? name, [FromQuery] string? team, [FromQuery] int page, [FromQuery] int pageSize, [FromQuery] int? userId, [FromQuery] string? sortBy, [FromQuery] string? sortDir)
        {
            try
            {
                var pageIndex = page < 1 ? 1 : page;
                var size = pageSize <= 0 ? 10 : (pageSize > 200 ? 200 : pageSize);

                var q = _db.AxUsers.AsNoTracking().AsQueryable();
                q = q.Where(u => !u.IsDeleted && u.Id > 0 && u.Id != 2);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var n = name.Trim().ToLower();
                    q = q.Where(u =>
                        (u.FirstName ?? "").ToLower().Contains(n)
                        || (u.LastName ?? "").ToLower().Contains(n)
                        || (u.Code ?? "").ToLower().Contains(n));
                }

                if (!string.IsNullOrWhiteSpace(team))
                {
                    var t = team.Trim().ToLower();
                    q = from u in q
                        join g in _db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups
                        from g in groups.DefaultIfEmpty()
                        where g != null && ((g.Code ?? "").ToLower().Contains(t)
                                             || ((g.FirstName ?? "") + " " + (g.LastName ?? "")).Trim().ToLower().Contains(t))
                        select u;
                }

                var baseQuery = from u in q
                                join g in _db.AxUsers.AsNoTracking().Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups
                                from g in groups.DefaultIfEmpty()
                                select new
                                {
                                    id = u.Id,
                                    code = u.Code,
                                    firstName = u.FirstName,
                                    lastName = u.LastName,
                                    email = u.Email,
                                    phone = u.Phone,
                                    roleId = u.RoleId,
                                    isTempPassword = u.IsTempPassword,
                                    isActive = u.IsActive,
                                    isDeleted = u.IsDeleted,
                                    isGroup = u.IsGroup,
                                    ownerId = u.OwnerId,
                                    groupName = g != null ? g.Code : null,
                                    stamp = u.Stamp
                                };

                var sortSpec = string.IsNullOrWhiteSpace(sortBy) ? null : new OnlineContract.Helpers.SortSpec(sortBy!.Trim(), string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
                var sortMap = new Dictionary<string, System.Linq.Expressions.Expression<Func<OnlineContract.Models.AxUser, object?>>> {
                    { "id", u => u.Id },
                    { "code", u => u.Code },
                    { "firstName", u => u.FirstName },
                    { "lastName", u => u.LastName },
                    { "email", u => u.Email },
                    { "roleId", u => u.RoleId },
                    { "ownerId", u => u.OwnerId },
                    { "isActive", u => u.IsActive },
                    { "stamp", u => u.Stamp }
                };

                var totalCount = await baseQuery.CountAsync();

                var usersQuery = _db.AxUsers.AsNoTracking().Where(u => !u.IsDeleted && u.Id > 0 && u.Id != 2);
                if (!string.IsNullOrWhiteSpace(name)) {
                    var n = name.Trim().ToLower();
                    usersQuery = usersQuery.Where(u => (u.FirstName ?? "").ToLower().Contains(n)
                        || (u.LastName ?? "").ToLower().Contains(n)
                        || (u.Code ?? "").ToLower().Contains(n));
                }
                if (!string.IsNullOrWhiteSpace(team)) {
                    var t = team.Trim().ToLower();
                    usersQuery = from u in usersQuery
                                 join g in _db.AxUsers.Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups2
                                 from g in groups2.DefaultIfEmpty()
                                 where g != null && ((g.Code ?? "").ToLower().Contains(t) || (((g.FirstName ?? "") + " " + (g.LastName ?? "")).Trim().ToLower().Contains(t)))
                                 select u;
                }

                var orderedUsers = usersQuery.ApplySort(sortSpec, sortMap, u => u.Id);

                var items = await (
                    from u in orderedUsers
                    join g in _db.AxUsers.AsNoTracking().Where(x => x.IsGroup && !x.IsDeleted) on u.OwnerId equals g.Id into groups2
                    from g in groups2.DefaultIfEmpty()
                    select new
                    {
                        id = u.Id,
                        code = u.Code,
                        firstName = u.FirstName,
                        lastName = u.LastName,
                        email = u.Email,
                        phone = u.Phone,
                        roleId = u.RoleId,
                        isTempPassword = u.IsTempPassword,
                        isActive = u.IsActive,
                        isDeleted = u.IsDeleted,
                        isGroup = u.IsGroup,
                        ownerId = u.OwnerId,
                        groupName = g != null ? g.Code : null,
                        stamp = u.Stamp
                    })
                    .Skip(Math.Max(0, (pageIndex - 1) * size))
                    .Take(size)
                    .ToListAsync();

                return JsonResultHelper.StableJson(_env, new { items, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size), sortBy = sortBy ?? "", sortDir = sortDir ?? "" });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Users fetch failed", ex.ToString(), userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { items = Array.Empty<object>() });
            }
        }

        [HttpPost("change-temp-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangeTempPassword([FromBody] ChangeTempPasswordDto dto)
        {
            try
            {
                var code = (dto.Code ?? "").Trim();
                if (string.IsNullOrEmpty(code)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Username is required. Please enter your username." });

                var u = await _db.AxUsers.FirstOrDefaultAsync(x => x.Code == code && !x.IsDeleted);
                if (u == null) return JsonResultHelper.StableJson(_env, new { success = false, message = "User not found. Please check your username and try again." });

                if (!u.IsTempPassword)
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "This account does not require a password change." });

                if (!dto.Stamp.HasValue || dto.Stamp.Value != u.Stamp)
                {
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Your session is out of date. Please retry the password change and try again." });
                }

                var pw = dto.NewPassword ?? "";
                var pw2 = dto.ConfirmPassword ?? "";
                if (string.IsNullOrWhiteSpace(pw)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Password is required. Please enter a new password." });
                if (!string.Equals(pw, pw2, System.StringComparison.Ordinal)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Passwords do not match. Please ensure both entries are identical." });
                if (pw.Length < 8) return JsonResultHelper.StableJson(_env, new { success = false, message = "Password must be at least 8 characters long." });
                if (!System.Text.RegularExpressions.Regex.IsMatch(pw, @"[A-Z]")) return JsonResultHelper.StableJson(_env, new { success = false, message = "Password must contain at least one uppercase letter." });
                if (!System.Text.RegularExpressions.Regex.IsMatch(pw, @"\d")) return JsonResultHelper.StableJson(_env, new { success = false, message = "Password must contain at least one number." });

                if (PasswordHelper.VerifyPassword(pw, u.Password))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "The new password cannot be the same as your current password. Please choose a different password." });

                u.Password = PasswordHelper.HashPassword(pw);
                u.PasswordDt = DateTime.Now;
                u.LastLoginDt = DateTime.Now;
                u.IsTempPassword = false;
                u.Stamp = u.Stamp + 1;
                await _db.SaveChangesAsync();

                var claims = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, u.Id.ToString()),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, u.Code ?? string.Empty),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, u.RoleId.ToString())
                };
                var identity = new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new System.Security.Claims.ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Temporary password changed and user signed in", $"User {u.Code}", u.Id);
                return JsonResultHelper.StableJson(_env, new { success = true, userId = u.Id, roleId = u.RoleId });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Temp password change failed", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to change password. Please try again later." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateDto dto, [FromQuery] int? userId)
        {
            try
            {
                var code = (dto.Code ?? "").Trim();
                if (string.IsNullOrEmpty(code))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "User code is required. Please enter a valid code." });

                var exists = await _db.AxUsers.AnyAsync(u => u.Code == code);
                if (exists)
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "The provided code already exists. Please choose a different code." });

                if (string.IsNullOrWhiteSpace(dto.Email))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Email is required. Please provide an email address." });
                if (string.IsNullOrWhiteSpace(dto.Phone))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Phone is required. Please provide a phone number." });

                var emailCandidate = dto.Email.Trim();
                if (!IsValidEmail(emailCandidate))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid email format. Please enter a valid email address." });

                var existsEmail = await _db.AxUsers.AnyAsync(u => u.Email == emailCandidate);
                if (existsEmail)
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Email already exists. Please use a different email address." });

                var (okCreatePhone, normalizedCreatePhone, createPhoneErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
                if (!okCreatePhone) return JsonResultHelper.StableJson(_env, new { success = false, message = createPhoneErr });

                if (!dto.IsGroup)
                {
                    if (string.IsNullOrWhiteSpace(dto.Password))
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Password is required. Please provide a password." });
                    if (string.Equals(dto.Password, "passw0rd", StringComparison.Ordinal))
                        return JsonResultHelper.StableJson(_env, new { success = false, message = "Please choose a stronger password; the default password is not allowed." });
                }

                var u = new AxUser
                {
                    Code = code,
                    FirstName = dto.FirstName ?? "",
                    LastName = dto.IsGroup ? "" : (dto.LastName ?? ""),
                    Email = emailCandidate,
                    Phone = normalizedCreatePhone ?? "",
                    RoleId = dto.RoleId,
                    IsGroup = dto.IsGroup,
                    IsTempPassword = dto.IsTempPassword,
                    OwnerId = dto.OwnerId,
                    City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
                    StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
                    PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
                    IsActive = true,
                    IsDeleted = false,
                    CreatedDt = DateTime.Now,
                    PasswordDt = DateTime.Now,
                    LastLoginDt = null,
                    Stamp = dto.IsTempPassword ? 1 : 0,
                    Password = dto.IsGroup ? "" : PasswordHelper.HashPassword(dto.Password ?? ""),
                    InputUserId = userId ?? 2
                };

                _db.AxUsers.Add(u);
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, dto.IsGroup ? "Team created" : "User created", $"Code={u.Code}", userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { success = true, id = u.Id });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Create user failed", ex.ToString(), userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Failed to create the user. Please try again later." });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UserUpdateDto dto, [FromQuery] int? userId)
        {
            try
            {
                var u = await _db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
                if (u == null) return NotFound(new { message = "User not found. Please refresh the list and try again." });

                if (!dto.Stamp.HasValue || dto.Stamp.Value != u.Stamp)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "User update conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }

                if (string.IsNullOrWhiteSpace(dto.Email))
                    return new JsonResult(new { success = false, message = "Email is required. Please provide an email address." });
                if (string.IsNullOrWhiteSpace(dto.Phone))
                    return new JsonResult(new { success = false, message = "Phone is required. Please provide a phone number." });

                var emailUpdate = dto.Email.Trim();
                if (!IsValidEmail(emailUpdate))
                    return new JsonResult(new { success = false, message = "Invalid email format. Please enter a valid email address." });

                var existsEmail = await _db.AxUsers.AnyAsync(x => x.Email == emailUpdate && x.Id != id);
                if (existsEmail) return new JsonResult(new { success = false, message = "Email already exists." });

                if (!u.IsGroup && dto.Password != null)
                {
                    var pwd = dto.Password ?? "";
                    if (pwd == "••••••••" || pwd == "********")
                    {
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(pwd))
                            return JsonResultHelper.StableJson(_env, new { success = false, message = "Password is required when updating a user. Please provide a password." });
                        if (string.Equals(pwd, "passw0rd", StringComparison.Ordinal))
                            return JsonResultHelper.StableJson(_env, new { success = false, message = "Please choose a stronger password; the default password is not allowed." });
                        u.Password = PasswordHelper.HashPassword(pwd);
                        u.PasswordDt = DateTime.Now;
                    }
                }

                var changed = false;

                if (!string.IsNullOrWhiteSpace(dto.Code))
                {
                    var newCode = dto.Code.Trim();
                    if (!string.Equals(newCode, u.Code, StringComparison.Ordinal))
                    {
                        var existsCode = await _db.AxUsers.AnyAsync(x => x.Code == newCode && x.Id != id);
                        if (existsCode) return JsonResultHelper.StableJson(_env, new { success = false, message = "Code already exists." });
                        u.Code = newCode;
                        changed = true;
                    }
                }

                if (dto.FirstName != null)
                {
                    u.FirstName = dto.FirstName.Trim();
                    changed = true;
                }
                if (dto.LastName != null)
                {
                    u.LastName = dto.LastName.Trim();
                    changed = true;
                }
                if (dto.Email != null)
                {
                    u.Email = dto.Email.Trim();
                    changed = true;
                }
                if (dto.Phone != null)
                {
                    var (okUpdPhone, normalizedUpdPhone, updErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
                    if (!okUpdPhone) return JsonResultHelper.StableJson(_env, new { success = false, message = updErr });
                    u.Phone = normalizedUpdPhone ?? "";
                    changed = true;
                }
                if (dto.RoleId.HasValue)
                {
                    u.RoleId = dto.RoleId.Value;
                    changed = true;
                }
                if (dto.IsActive.HasValue)
                {
                    u.IsActive = dto.IsActive.Value;
                    changed = true;
                }
                if (dto.OwnerId.HasValue)
                {
                    u.OwnerId = dto.OwnerId.Value;
                    changed = true;
                }
                if (dto.City != null)
                {
                    u.City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City.Trim();
                    changed = true;
                }
                if (dto.StreetAddress != null)
                {
                    u.StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress.Trim();
                    changed = true;
                }
                if (dto.PostalCode != null)
                {
                    u.PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode.Trim();
                    changed = true;
                }

                if (dto.IsTempPassword.HasValue)
                {
                    if (dto.IsTempPassword.Value != u.IsTempPassword)
                    {
                        u.IsTempPassword = dto.IsTempPassword.Value;
                        changed = true;
                    }
                }

                if (!u.IsGroup && dto.Password != null)
                {
                    var pwd = dto.Password ?? "";
                    if (!(pwd == "••••••••" || pwd == "********"))
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    u.Stamp = u.Stamp + 1;
                }
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "User updated", $"Code={u.Code}", userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { success = true, message = "User has been updated successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Update user failed", ex.ToString(), userId ?? 2);
                return JsonResultHelper.StableJson(_env, new { success = false });
            }
        }

        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> Deactivate(int id, [FromQuery] int? userId, [FromQuery] int? stamp)
        {
            try
            {
                var u = await _db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
                if (u == null) return NotFound(new { message = "User not found. The user may have been removed." });
                if (!stamp.HasValue || stamp.Value != u.Stamp)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "User deactivate conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                u.IsActive = false;
                u.Stamp = u.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "User deactivated", $"Code={u.Code}", userId ?? 2);
                return Ok(new { success = true, message = "User has been deactivated successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Deactivate user failed", ex.ToString(), userId ?? 2);
                return StatusCode(500);
            }
        }

        [HttpPost("{id}/activate")]
        public async Task<IActionResult> Activate(int id, [FromQuery] int? userId, [FromQuery] int? stamp)
        {
            try
            {
                var u = await _db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
                if (u == null) return NotFound(new { message = "User not found. The user may have been removed." });
                if (!stamp.HasValue || stamp.Value != u.Stamp)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "User activate conflict - stamp mismatch", $"UserId={id}", userId ?? 2);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = $"Your changes to user {id} cannot be saved because another user has changed it. You need to discard your changes and reapply them." });
                }
                u.IsActive = true;
                u.Stamp = u.Stamp + 1;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "User activated", $"Code={u.Code}", userId ?? 2);
                return Ok(new { success = true, message = "User has been activated successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Activate user failed", ex.ToString(), userId ?? 2);
                return StatusCode(500);
            }
        }

        [HttpPost("{id}/delete")]
        public async Task<IActionResult> Delete(int id, [FromQuery] int? userId)
        {
            try
            {
                var u = await _db.AxUsers.FirstOrDefaultAsync(x => x.Id == id);
                if (u == null) return NotFound(new { message = "User not found. The user may have been removed." });
                u.IsActive = false;
                u.IsDeleted = true;
                await _db.SaveChangesAsync();
                await LoggerHelper.LogEventAsync(_db, EventType.Information, "User deleted", $"Code={u.Code}", userId ?? 2);
                return Ok(new { success = true, message = "User has been deleted successfully." });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Delete user failed", ex.ToString(), userId ?? 2);
                return StatusCode(500);
            }
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch { return false; }
        }
    }
}