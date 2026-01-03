using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using OnlineContract.Data;
using OnlineContract.Models;
using OnlineContract.Services;
using Xunit;

namespace OnlineContract.Tests
{
    public class ForgotPasswordTests
    {
        private IConfiguration BuildConfig(Action<IConfigurationBuilder>? customize = null)
        {
            var defaults = new Dictionary<string, string?>
            {
                ["ForgotPassword:EnableThrottling"] = "true",
                ["ForgotPassword:ThrottleWindowSeconds"] = "60"
            };

            var builder = new ConfigurationBuilder().AddInMemoryCollection(defaults);
            customize?.Invoke(builder);
            return builder.Build();
        }

        private AppDbContext CreateDb()
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(opts);
        }

        private ForgotPasswordService CreateService(IConfiguration? cfg = null)
            => new ForgotPasswordService(cfg ?? BuildConfig());

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-email")]
        [InlineData("user@exa mple.com")]
        [InlineData("user@example")]
        public async Task InvalidEmail_Returns400(string? rawEmail)
        {
            var db = CreateDb();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, rawEmail!, "127.0.0.1", cache, mailer.Object);

            Assert.Equal(400, status);
            Assert.Contains("email", message, StringComparison.OrdinalIgnoreCase);
            mailer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task InvalidEmail_DoubleAt_Returns404()
        {
            var db = CreateDb();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, "user@@example.com", "127.0.0.1", cache, mailer.Object);

            Assert.Equal(404, status);
            Assert.Contains("doesn't exist", message, StringComparison.OrdinalIgnoreCase);
            mailer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task MissingUser_Returns404_DoesNotCallMailer()
        {
            var db = CreateDb();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, "missing@example.com", "127.0.0.1", cache, mailer.Object);

            Assert.Equal(404, status);
            Assert.Equal("Account with this email doesn't exist. Please create a new account.", message);
            mailer.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData(" user@example.com ")]
        [InlineData("USER@EXAMPLE.COM")]
        public async Task ExistingUser_Returns200_CallsMailer_WithNormalizedEmail(string input)
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 1, Code = "u1", Email = "user@example.com",
                IsActive = true, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>();
            mailer.Setup(m => m.SendResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var svc = CreateService();
            var (status, message) = await svc.HandleAsync(db, input, "127.0.0.1", cache, mailer.Object);

            Assert.Equal(200, status);
            Assert.Equal("We sent reset instructions to your email.", message);
            mailer.Verify(m => m.SendResetEmailAsync(
                "user@example.com", null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExistingUser_WithPlusAlias_NotSupported_Returns404()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 1, Code = "u1", Email = "user@example.com",
                IsActive = true, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, "user+alias@example.com", "127.0.0.1", cache, mailer.Object);

            Assert.Equal(404, status);
            Assert.Contains("doesn't exist", message, StringComparison.OrdinalIgnoreCase);
            mailer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task InactiveUser_Returns404_UniformMessage_NoMailer()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 2, Code = "u2", Email = "inactive@example.com",
                IsActive = false, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, "inactive@example.com", "127.0.0.2", cache, mailer.Object);

            Assert.Equal(404, status);
            Assert.Contains("doesn't exist", message, StringComparison.OrdinalIgnoreCase);
            mailer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task DeletedUser_Returns404_UniformMessage_NoMailer()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 3, Code = "u3", Email = "deleted@example.com",
                IsActive = true, IsDeleted = true, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>(MockBehavior.Strict);
            var svc = CreateService();

            var (status, message) = await svc.HandleAsync(db, "deleted@example.com", "127.0.0.3", cache, mailer.Object);

            Assert.Equal(404, status);
            Assert.Contains("doesn't exist", message, StringComparison.OrdinalIgnoreCase);
            mailer.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Throttling_RepeatedRequestsWithinWindow_SendsDistinctTokens_TwoEmails()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 4, Code = "u4", Email = "rate@example.com",
                IsActive = true, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());

            var captured = new List<(string email, string? codeToken)>();
            var mailer = new Mock<IEmailService>();
            mailer.Setup(m => m.SendResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string?, string?, CancellationToken>((email, _, token, _) =>
                  {
                      captured.Add((email, token));
                  })
                  .Returns(Task.CompletedTask);

            var svc = CreateService(BuildConfig());

            var (status1, _) = await svc.HandleAsync(db, "rate@example.com", "127.0.0.4", cache, mailer.Object);
            var (status2, _) = await svc.HandleAsync(db, "rate@example.com", "127.0.0.4", cache, mailer.Object);

            Assert.Equal(200, status1);
            Assert.Equal(200, status2);

            Assert.Equal(2, captured.Count);
            Assert.All(captured, c => Assert.Equal("rate@example.com", c.email));
            Assert.True(captured.Select(c => c.codeToken).Distinct().Count() == 2);
        }

        [Fact]
        public async Task MultipleRequests_DifferentIp_AreTreatedIndependently()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 6, Code = "u6", Email = "multiip@example.com",
                IsActive = true, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>();
            mailer.Setup(m => m.SendResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var svc = CreateService();

            var (s1, _) = await svc.HandleAsync(db, "multiip@example.com", "10.0.0.1", cache, mailer.Object);
            var (s2, _) = await svc.HandleAsync(db, "multiip@example.com", "10.0.0.2", cache, mailer.Object);

            Assert.Equal(200, s1);
            Assert.Equal(200, s2);
            mailer.Verify(m => m.SendResetEmailAsync(
                "multiip@example.com", null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task MailerThrows_Returns5xx_DoesNotCacheSuccess()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser
            {
                Id = 5, Code = "u5", Email = "boom@example.com",
                IsActive = true, IsDeleted = false, Password = "x"
            });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mailer = new Mock<IEmailService>();
            mailer.Setup(m => m.SendResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .ThrowsAsync(new InvalidOperationException("SMTP down"));

            var svc = CreateService();

            var (status, _) = await svc.HandleAsync(db, "boom@example.com", "127.0.0.5", cache, mailer.Object);
            Assert.InRange(status, 500, 503);

            mailer.Reset();
            mailer.Setup(m => m.SendResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

            var (status2, _) = await svc.HandleAsync(db, "boom@example.com", "127.0.0.5", cache, mailer.Object);
            Assert.Equal(200, status2);
        }
    }
}