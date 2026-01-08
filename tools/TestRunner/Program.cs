using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using OnlineContract.Data;
using OnlineContract.Models;
using OnlineContract.Services;

class Program
{
    static async Task<int> Main()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        int passed = 0, failed = 0;

        async Task RunCase(string name, Func<Task<bool>> fn)
        {
            Console.Write($"{name}... ");
            try
            {
                var ok = await fn();
                if (ok)
                {
                    Console.WriteLine("PASS");
                    passed++;
                }
                else
                {
                    Console.WriteLine("FAIL");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                failed++;
            }
        }

        await RunCase("InvalidEmail_Returns400", async () =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("runner1").Options;
            using var db = new AppDbContext(opts);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var emailSvc = new FakeEmailService();
            var svc = new ForgotPasswordService(config);
            var (status, message) = await svc.HandleAsync(db, "not-an-email", "127.0.0.1", cache, emailSvc);
            return status == 400 && message == "Invalid email.";
        });

        await RunCase("MissingUser_Returns404_DoesNotCallMailer", async () =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("runner2").Options;
            using var db = new AppDbContext(opts);
            var cache = new MemoryCache(new MemoryCacheOptions());
            var emailSvc = new FakeEmailService();
            var svc = new ForgotPasswordService(config);
            var (status, message) = await svc.HandleAsync(db, "missing@example.com", "127.0.0.1", cache, emailSvc);
            return status == 404 && message == "Account with this email doesn't exist. Please create a new account." && emailSvc.SentCount == 0;
        });

        await RunCase("ExistingUser_Returns200_CallsMailer", async () =>
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("runner3").Options;
            using var db = new AppDbContext(opts);
            db.AxUsers.Add(new AxUser { Id = 1, Code = "u1", Email = "user@example.com", IsActive = true, IsDeleted = false, Password = "x" });
            await db.SaveChangesAsync();
            var cache = new MemoryCache(new MemoryCacheOptions());
            var emailSvc = new FakeEmailService();
            var svc = new ForgotPasswordService(config);
            var (status, message) = await svc.HandleAsync(db, " user@example.com ", "127.0.0.1", cache, emailSvc);
            return status == 200 && message == "We sent reset instructions to your email." && emailSvc.SentCount == 1;
        });

        Console.WriteLine();
        Console.WriteLine($"Passed: {passed}, Failed: {failed}");
        return failed == 0 ? 0 : 2;
    }

    class FakeEmailService : OnlineContract.Services.IEmailService
    {
        public int SentCount { get; private set; }
        public Task SendResetEmailAsync(string to, string? code = null, string? token = null, System.Threading.CancellationToken ct = default)
        {
            SentCount++;
            return Task.CompletedTask;
        }

        public Task<bool> HealthAsync(System.Threading.CancellationToken ct = default) => Task.FromResult(true);
    }
}
