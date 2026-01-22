using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OnlineContract.Infrastructure;
using OnlineContract.Data;
using OnlineContract.Dtos;
using OnlineContract.Helpers;
using OnlineContract.Services;
using OnlineContract.Models;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;
        private readonly IConfiguration _cfg;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;

        public AuthController(AppDbContext db, IHostEnvironment env, IConfiguration cfg, IEmailService emailService, IMemoryCache cache)
        {
            _db = db; _env = env; _cfg = cfg; _emailService = emailService; _cache = cache;
        }

        [HttpPost("login")]
        [HttpPost("/api/login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                var user = await _db.AxUsers.FirstOrDefaultAsync(u => u.Code == dto.Code && !u.IsDeleted);
                if (user == null)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Login failed - invalid user code", $"Code={dto.Code}", 2);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid user code. Please check your credentials and try again." });
                }

                if (!PasswordHelper.VerifyPassword(dto.Password, user.Password))
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Login failed - invalid password", $"Code={dto.Code}", user.Id);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Invalid password. Please check your credentials and try again." });
                }

                if (user.IsGroup)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Login failed - team account attempted", $"Code={dto.Code}", user.Id);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Team accounts cannot be used to sign in. Please use a personal account or contact our administrator for access." });
                }

                if (user.IsTempPassword)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Login blocked - temp password requires change", $"Code={dto.Code}", user.Id);
                    return JsonResultHelper.StableJson(_env, new { success = false, mustChangePassword = true, message = "Your account requires a password change before you can continue. Please set a new password now.", stamp = user.Stamp });
                }

                if (!user.IsActive)
                {
                    await LoggerHelper.LogEventAsync(_db, EventType.Warning, "Login failed - deactivated account", $"Code={dto.Code}", user.Id);
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Your account has been deactivated. If you need it reactivated, please contact our administrator." });
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Code ?? string.Empty),
                    new Claim(ClaimTypes.Role, user.RoleId.ToString())
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

                user.LastLoginDt = DateTime.Now;
                await _db.SaveChangesAsync();

                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Login successful", $"User {user.Code} logged in.", user.Id);
                return JsonResultHelper.StableJson(_env, new { success = true, userId = user.Id, roleId = user.RoleId });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Login endpoint exception", ex.ToString(), 2);
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Login failed due to a server error. Please try again later." });
            }
        }

        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword()
        {
            try
            {
                var body = await Request.ReadFromJsonAsync<Dictionary<string, string>>();
                var rawEmail = body != null && body.TryGetValue("email", out var e) ? e : "";
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var svc = new OnlineContract.Services.ForgotPasswordService(_cfg);
                var (status, message) = await svc.HandleAsync(_db, rawEmail, ip, _cache, _emailService);
                return new JsonResult(new { message }) { StatusCode = status };
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Forgot-password endpoint exception", ex.ToString(), 2);
                return new JsonResult(new { message = "Failed to process forgot-password request." }) { StatusCode = 500 };
            }
        }

        [HttpGet("reset-token/{token}")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetToken(string token)
        {
            try
            {
                var now = DateTime.Now;
                var pr = await _db.PasswordResetTokens.FirstOrDefaultAsync(p => p.Token == token && !p.IsUsed && p.ExpiryDt > now);
                if (pr == null) return new JsonResult(new { success = false, message = "This reset link is invalid or has expired. Please request a new password reset." });
                return new JsonResult(new { success = true, code = pr.Code, email = pr.Email });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Reset-token check failed", ex.ToString(), 2);
                return new JsonResult(new { success = false, message = "Failed to validate reset token." }) { StatusCode = 500 };
            }
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword()
        {
            try
            {
                var dto = await Request.ReadFromJsonAsync<Dictionary<string, string>>();
                var token = dto != null && dto.TryGetValue("token", out var t) ? t : "";
                var newPw = dto != null && dto.TryGetValue("newPassword", out var n) ? n : "";
                var conf = dto != null && dto.TryGetValue("confirmPassword", out var c) ? c : "";

                if (string.IsNullOrWhiteSpace(token)) return new JsonResult(new { success = false, message = "Token is required." });
                if (string.IsNullOrWhiteSpace(newPw) || newPw != conf) return new JsonResult(new { success = false, message = "Passwords do not match or are empty." });
                if (newPw.Length < 8 || !System.Text.RegularExpressions.Regex.IsMatch(newPw, "[A-Z]") || !System.Text.RegularExpressions.Regex.IsMatch(newPw, "\\d"))
                    return new JsonResult(new { success = false, message = "Password must be at least 8 characters, include one uppercase letter and one number." });

                var now = DateTime.Now;
                var pr = await _db.PasswordResetTokens.FirstOrDefaultAsync(p => p.Token == token && !p.IsUsed && p.ExpiryDt > now);
                if (pr == null) return new JsonResult(new { success = false, message = "This reset link is invalid or has expired." });

                if (!pr.UserId.HasValue) return new JsonResult(new { success = false, message = "No user associated with this token." });
                var user = await _db.AxUsers.FirstOrDefaultAsync(u => u.Id == pr.UserId.Value && !u.IsDeleted);
                if (user == null) return new JsonResult(new { success = false, message = "User account not found." });

                user.Password = PasswordHelper.HashPassword(newPw);
                user.PasswordDt = DateTime.Now;
                user.IsTempPassword = false;
                user.LastLoginDt = DateTime.Now;
                user.Stamp = user.Stamp + 1;

                pr.IsUsed = true;
                pr.UsedDt = DateTime.Now;

                await _db.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Code ?? string.Empty),
                    new Claim(ClaimTypes.Role, user.RoleId.ToString())
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

                await LoggerHelper.LogEventAsync(_db, EventType.Information, "Password reset completed", $"UserId={user.Id}", user.Id);
                return new JsonResult(new { success = true, userId = user.Id, roleId = user.RoleId });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Reset-password failed", ex.ToString(), 2);
                return new JsonResult(new { success = false, message = "Failed to reset password. Please try again later." });
            }
        }

        [HttpPost("logout")]
        [HttpPost("/api/logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok(new { success = true, message = "You have been signed out successfully." });
        }

        [HttpPost("register")]
        [HttpPost("/api/register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            try
            {
                var username = (dto.Username ?? "").Trim();
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(dto.Password))
                    return new JsonResult(new { success = false, message = "Username and password are required. Please provide both to continue." });
                if (string.Equals(dto.Password, "passw0rd", StringComparison.Ordinal))
                    return new JsonResult(new { success = false, message = "Please choose a stronger password; the default password is not allowed." });

                var exists = await _db.AxUsers.AnyAsync(u => u.Code == username);
                if (exists)
                    return new JsonResult(new { success = false, message = "Username already exists. Please choose a different username and try again." });

                var email = (dto.Email ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email))
                    return new JsonResult(new { success = false, message = "Email is required. Please provide an email address." });

                var phoneRaw = (dto.Phone ?? "").Trim();
                if (string.IsNullOrWhiteSpace(phoneRaw))
                    return new JsonResult(new { success = false, message = "Phone is required. Please provide a phone number." });

                if (!IsValidEmail(email))
                    return new JsonResult(new { success = false, message = "Invalid email format. Please enter a valid email address." });

                exists = await _db.AxUsers.AnyAsync(u => u.Email == email);
                if (exists)
                    return new JsonResult(new { success = false, message = "Email already exists. Please use a different email address." });

                var assignedRole = dto.RoleId ?? (int)UserRole.Customer;

                var (okPhone, normalizedPhone, phoneErr) = OnlineContract.Helpers.PhoneHelper.NormalizeSerbianPhone(dto.Phone);
                if (!okPhone) return new JsonResult(new { success = false, message = phoneErr });

                var ownerGroup = await _db.AxUsers.FirstOrDefaultAsync(u => u.RoleId == (int)UserRole.Customer && u.IsGroup && u.IsActive && !u.IsDeleted);

                var newUser = new AxUser
                {
                    FirstName = dto.FirstName ?? "",
                    LastName = dto.LastName ?? "",
                    Email = string.IsNullOrWhiteSpace(dto.Email) ? "" : dto.Email.Trim(),
                    Phone = normalizedPhone ?? "",
                    Code = username,
                    Password = PasswordHelper.HashPassword(dto.Password ?? ""),
                    IsGroup = false,
                    IsTempPassword = dto.IsTempPassword,
                    IsActive = true,
                    IsDeleted = false,
                    OwnerId = ownerGroup != null ? ownerGroup.Id : 0,
                    CreatedDt = DateTime.Now,
                    PasswordDt = DateTime.Now,
                    LastLoginDt = DateTime.Now,
                    City = string.IsNullOrWhiteSpace(dto.City) ? null : dto.City?.Trim(),
                    StreetAddress = string.IsNullOrWhiteSpace(dto.StreetAddress) ? null : dto.StreetAddress?.Trim(),
                    PostalCode = string.IsNullOrWhiteSpace(dto.PostalCode) ? null : dto.PostalCode?.Trim(),
                    RoleId = assignedRole
                };

                newUser.Stamp = dto.IsTempPassword ? 1 : 0;

                _db.AxUsers.Add(newUser);
                await _db.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, newUser.Id.ToString()),
                    new Claim(ClaimTypes.Name, newUser.Code ?? string.Empty),
                    new Claim(ClaimTypes.Role, newUser.RoleId.ToString())
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                    new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.Now.AddHours(8) });

                await LoggerHelper.LogEventAsync(_db, EventType.Information, "New Account successfully created", $"User {username} created and signed in.", newUser.Id);
                return new JsonResult(new { success = true, userId = newUser.Id, roleId = newUser.RoleId });
            }
            catch (Exception ex)
            {
                await LoggerHelper.LogEventAsync(_db, EventType.Error, "Account creation failed", ex.ToString(), 2);
                var msg = _env.IsDevelopment() ? ex.ToString() : "Account creation failed.";
                return new JsonResult(new { success = false, message = msg });
            }
        }

        [HttpGet("whoami")]
        [HttpGet("/whoami")]
        public IActionResult WhoAmI()
        {
            var user = HttpContext.User;
            var isAuth = user?.Identity?.IsAuthenticated ?? false;
            var name = user?.Identity?.Name;
            int roleId = 0;
            try
            {
                var roleClaim = user?.Claims.FirstOrDefault(c =>
                    c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrEmpty(roleClaim)) int.TryParse(roleClaim, out roleId);
            }
            catch (Exception ex)
            {
                // If there is any issue resolving or parsing the role claim, fall back to the default roleId (0).
                // Log the exception so that unexpected issues can be investigated.
                System.Diagnostics.Debug.WriteLine($"Failed to resolve or parse role claim in WhoAmI: {ex}");
                roleId = 0;
            }

            return new JsonResult(new
            {
                isAuthenticated = isAuth,
                name = name,
                roleId = roleId,
                claims = user?.Claims.Select(c => new { c.Type, c.Value }) ?? Enumerable.Empty<object>()
            });
        }

        [HttpGet("/api/login")]
        [AllowAnonymous]
        public IActionResult Health()
        {
            return new JsonResult(new { status = "Login endpoint is alive." });
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