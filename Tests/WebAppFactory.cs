using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using OnlineContract.Data;

namespace OnlineContract.Tests;

public class WebAppFactory : WebApplicationFactory<OnlineContract.Controllers.Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, "Testing");
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registrations
            var existingDb = services.Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>) || d.ServiceType == typeof(AppDbContext)).ToList();
            foreach (var d in existingDb) services.Remove(d);

            // Initialize persistent SQLite in-memory connection
            _connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _connection.Open();

            services.AddDbContext<AppDbContext>(opts => opts.UseSqlite(_connection));

            // Build provider and seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            TestDataSeeder.Seed(db);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
            _connection = null;
        }
    }

    public HttpClient CreateClientNoRedirect()
    {
        return this.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public async Task<(HttpClient client, string? authCookie)> CreateAuthenticatedClientAsync(string username, string password)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var loginResp = await client.PostAsJsonAsync("/api/login", new { Code = username, Password = password });
        if (!loginResp.IsSuccessStatusCode)
        {
            throw new Exception("Login failed in test setup");
        }

        string? authPair = null;
        if (loginResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var sc in setCookies)
            {
                // Take only the name=value part before the first ';'
                var nv = (sc ?? string.Empty).Split(';')[0].Trim();
                if (nv.StartsWith(".OnlineContract.Auth="))
                {
                    authPair = nv;
                    break;
                }
            }
        }

        return (client, authPair);
    }
}
