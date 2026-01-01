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
        private IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection().Build();

        private AppDbContext CreateDb()
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("testdb").Options;
            var db = new AppDbContext(opts);
            return db;
        }

        [Fact]
        public async Task InvalidEmail_Returns400()
        {
            var db = CreateDb();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var mockEmail = new Mock<OnlineContract.Services.IEmailService>();
            var svc = new ForgotPasswordService(Configuration);

            var (status, message) = await svc.HandleAsync(db, "not-an-email", "127.0.0.1", cache, mockEmail.Object);
            Assert.Equal(400, status);
            Assert.Equal("Please enter a valid email address.", message);
        }

        [Fact]
        public async Task MissingUser_Returns404_DoesNotCallMailer()
        {
            var db = CreateDb();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var mockEmail = new Mock<OnlineContract.Services.IEmailService>();
            var svc = new ForgotPasswordService(Configuration);

            var (status, message) = await svc.HandleAsync(db, "missing@example.com", "127.0.0.1", cache, mockEmail.Object);
            Assert.Equal(404, status);
            Assert.Equal("Account with this email doesn't exist. Please create a new account.", message);
            mockEmail.Verify(m => m.SendResetEmailAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ExistingUser_Returns200_CallsMailer()
        {
            var db = CreateDb();
            db.AxUsers.Add(new AxUser { Id = 1, Code = "u1", Email = "user@example.com", IsActive = true, IsDeleted = false, Password = "x" });
            await db.SaveChangesAsync();

            var cache = new MemoryCache(new MemoryCacheOptions());
            var mockEmail = new Mock<OnlineContract.Services.IEmailService>();
            mockEmail.Setup(m => m.SendResetEmailAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>())).Returns(Task.CompletedTask);

            var svc = new ForgotPasswordService(Configuration);
            var (status, message) = await svc.HandleAsync(db, " user@example.com ", "127.0.0.1", cache, mockEmail.Object);
            Assert.Equal(200, status);
            Assert.Equal("We sent reset instructions to your email.", message);
            mockEmail.Verify(m => m.SendResetEmailAsync("user@example.com", null, It.IsAny<string?>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }
    }
}
