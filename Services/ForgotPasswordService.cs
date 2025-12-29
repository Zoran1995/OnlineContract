using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using OnlineContract.Data;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Helpers;
using OnlineContract.Models;

namespace OnlineContract.Services
{
    public class ForgotPasswordService
    {
        private readonly IConfiguration _config;
        public ForgotPasswordService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<(int StatusCode, string Message)> HandleAsync(AppDbContext db, string rawEmail, string ip, IMemoryCache cache, OnlineContract.Helpers.IEmailService emailService)
        {
            // Normalize and validate
            var email = (rawEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^\S+@\S+\.\S+$"))
            {
                return (400, "Invalid email.");
            }

            // Rate limiting (preserve existing behavior)
            try
            {
                var ipKey = $"fp:ip:{ip}";
                var emailKey = $"fp:email:{email.ToLowerInvariant()}";
                int ipCount = 0;
                int emailCount = 0;
                if (cache.TryGetValue(ipKey, out var ipObj) && ipObj is int ipVal) ipCount = ipVal;
                if (cache.TryGetValue(emailKey, out var emObj) && emObj is int emVal) emailCount = emVal;
                if (ipCount >= 20 || emailCount >= 5)
                {
                    return (429, "Too many forgot-password requests. Please try again later.");
                }
                try
                {
                    using (var e = cache.CreateEntry(ipKey))
                    {
                        e.Value = ipCount + 1;
                        e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    }
                    using (var e2 = cache.CreateEntry(emailKey))
                    {
                        e2.Value = emailCount + 1;
                        e2.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                    }
                }
                catch { }
            }
            catch { }

            // Lookup user
            var user = await db.FindByEmailAsync(email);
            if (user == null)
            {
                // Log info (caller should log as needed) and return 404
                return (404, "Account with this email doesn't exist. Please create a new account.");
            }

            // Generate token and persist
            var now = DateTime.Now;
            string? token = null;
            for (int i = 0; i < 5; i++)
            {
                var bytes = new byte[32];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                var t = Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
                var exists = await db.PasswordResetTokens.AnyAsync(p => p.Token == t);
                if (!exists) { token = t; break; }
            }
            if (token == null) token = Guid.NewGuid().ToString("N");

            var expiryMinutes = 15;
            try { expiryMinutes = int.Parse(_config["PasswordReset:ExpiryMinutes"] ?? "15"); } catch { }

            var pr = new PasswordResetToken
            {
                Token = token,
                UserId = user.Id,
                Code = user.Code,
                Email = user.Email,
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(expiryMinutes),
                Used = false
            };

            try
            {
                db.PasswordResetTokens.Add(pr);
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                return (500, "Failed to create password reset token. Please try again later.");
            }

            // Send email
            var appOrigin = _config["AppOrigin"] ?? "";
            var resetLink = appOrigin.TrimEnd('/') + "/reset-password.html?token=" + System.Net.WebUtility.UrlEncode(token);
            var (sent, err) = await emailService.SendResetEmailAsync(_config, user.Email ?? "", resetLink);
            // If sending failed, surface an explicit 502 to the client while logging the failure
            if (!sent)
            {
                return (502, "Failed to send reset email.");
            }

            return (200, "We sent reset instructions to your email.");
        }
    }
}
