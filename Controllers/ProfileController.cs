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
using OnlineContract.Dtos;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHostEnvironment _env;

        public ProfileController(AppDbContext db, IHostEnvironment env)
        {
            _db = db; _env = env;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Get()
        {
            if (!(User?.Identity?.IsAuthenticated ?? false)) return StatusCode(StatusCodes.Status401Unauthorized);
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            var u = await _db.AxUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid && !x.IsDeleted);
            if (u == null) return StatusCode(StatusCodes.Status404NotFound);
            var dto = new ProfileDto
            {
                Id = u.Id,
                Code = u.Code ?? string.Empty,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                Email = u.Email ?? string.Empty,
                PhoneNumber = u.Phone ?? string.Empty,
                City = u.City ?? string.Empty,
                StreetAddress = u.StreetAddress ?? string.Empty,
                PostalCode = u.PostalCode ?? string.Empty,
                Stamp = u.Stamp
            };
            return JsonResultHelper.StableJson(_env, dto);
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> Update([FromBody] ProfileUpdateDto dto)
        {
            if (!(User?.Identity?.IsAuthenticated ?? false)) return StatusCode(StatusCodes.Status401Unauthorized);
            var uid = UserContextHelper.GetCurrentUserId(HttpContext);
            if (dto == null) return StatusCode(StatusCodes.Status400BadRequest);

            string err(string m) => m;
            if (string.IsNullOrWhiteSpace(dto.Code)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Username is required.") });
            if (string.IsNullOrWhiteSpace(dto.FirstName)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("First name is required.") });
            if (string.IsNullOrWhiteSpace(dto.LastName)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Last name is required.") });
            if (string.IsNullOrWhiteSpace(dto.Email)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Email is required.") });
            if (string.IsNullOrWhiteSpace(dto.PhoneNumber)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Phone number is required.") });
            if (string.IsNullOrWhiteSpace(dto.City)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("City is required.") });
            if (string.IsNullOrWhiteSpace(dto.StreetAddress)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Street address is required.") });
            if (string.IsNullOrWhiteSpace(dto.PostalCode)) return JsonResultHelper.StableJson(_env, new { success = false, message = err("Postal code is required.") });
            var postal = dto.PostalCode.Trim();
            if (postal.Length != 5)
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Postal code must be exactly 5 characters." });

            var code = dto.Code.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z][A-Za-z0-9._-]{2,29}$"))
                return JsonResultHelper.StableJson(_env, new { success = false, message = "Username must start with a letter and be 3-30 chars (letters, digits, ., _, -)." });

            var email = dto.Email.Trim();
            var emailAttr = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();
            if (!emailAttr.IsValid(email)) return JsonResultHelper.StableJson(_env, new { success = false, message = "Email format is invalid." });

            var (okPhone, normalizedPhone, phoneErr) = PhoneHelper.NormalizeSerbianPhone(dto.PhoneNumber);
            if (!okPhone) return JsonResultHelper.StableJson(_env, new { success = false, message = phoneErr });

            var existsCode = await _db.AxUsers.AnyAsync(x => x.Code == code && !x.IsDeleted && x.Id != uid);
            if (existsCode) return JsonResultHelper.StableJson(_env, new { success = false, message = "Username is already taken." });
            var existsEmail = await _db.AxUsers.AnyAsync(x => (x.Email ?? "") == email && !x.IsDeleted && x.Id != uid);
            if (existsEmail) return JsonResultHelper.StableJson(_env, new { success = false, message = "Email is already in use." });

            var user = await _db.AxUsers.FirstOrDefaultAsync(x => x.Id == uid && !x.IsDeleted);
            if (user == null) return StatusCode(StatusCodes.Status404NotFound);
            if (user.Stamp != dto.Stamp)
                return StatusCode(StatusCodes.Status409Conflict);

            var changingPassword = !string.IsNullOrWhiteSpace(dto.CurrentPassword) || !string.IsNullOrWhiteSpace(dto.NewPassword) || !string.IsNullOrWhiteSpace(dto.ConfirmNewPassword);
            bool passwordActuallyChanged = false;
            if (changingPassword)
            {
                var cur = (dto.CurrentPassword ?? "").Trim();
                var np = (dto.NewPassword ?? "").Trim();
                var cp = (dto.ConfirmNewPassword ?? "").Trim();
                if (string.IsNullOrEmpty(cur) || string.IsNullOrEmpty(np) || string.IsNullOrEmpty(cp))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "To change your password, fill all three fields: current, new, and confirm." });
                if (!PasswordHelper.VerifyPassword(cur, user.Password))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "Current password is incorrect." });
                if (np != cp) return JsonResultHelper.StableJson(_env, new { success = false, message = "New password and confirmation do not match." });
                if (np.Length < 8 || !np.Any(char.IsUpper) || !np.Any(char.IsDigit))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "New password must be at least 8 chars and include an uppercase letter and a digit." });
                if (PasswordHelper.VerifyPassword(np, user.Password))
                    return JsonResultHelper.StableJson(_env, new { success = false, message = "New password must be different from the current password." });
                user.Password = PasswordHelper.HashPassword(np);
                user.PasswordDt = DateTime.Now;
                passwordActuallyChanged = true;
            }

            user.Code = code;
            user.FirstName = dto.FirstName.Trim();
            user.LastName = dto.LastName.Trim();
            user.Email = email;
            user.Phone = normalizedPhone ?? dto.PhoneNumber.Trim();
            user.City = dto.City.Trim();
            user.StreetAddress = dto.StreetAddress.Trim();
            user.PostalCode = dto.PostalCode.Trim();
            user.Stamp = user.Stamp + 1;

            await _db.SaveChangesAsync();
            return JsonResultHelper.StableJson(_env, new { success = true, passwordChanged = passwordActuallyChanged, stamp = user.Stamp });
        }
    }
}
