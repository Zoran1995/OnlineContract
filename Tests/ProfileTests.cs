using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OnlineContract.Tests;

public class ProfileTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ProfileTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Profile_Get_Returns_Current_User()
    {
        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        await EnsureAuthCookieAsync(client);
        var res = await client.GetAsync("/api/profile");
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(dto);
        Assert.Equal("testuser", dto!.Code);
        Assert.True(dto.Stamp >= 0);
    }

    [Fact]
    public async Task Profile_Update_Without_Password_Changes_Saves_And_Does_Not_Update_PasswordDt()
    {
        // Use an isolated user to avoid cross-test interference
        string uname = $"prof_{Guid.NewGuid():N}".Substring(0, 12);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            db.AxUsers.Add(new OnlineContract.Models.AxUser
            {
                Code = uname,
                FirstName = "John",
                LastName = "Doe",
                Email = $"{uname}@example.com",
                Phone = "+381601234567",
                City = "Belgrade",
                StreetAddress = "Main 1",
                PostalCode = "11000",
                Password = OnlineContract.Helpers.PasswordHelper.HashPassword("Password1"),
                RoleId = 6,
                IsActive = true,
                IsDeleted = false,
                IsGroup = false,
                OwnerId = 2,
                CreatedDt = DateTime.UtcNow,
                PasswordDt = DateTime.UtcNow,
                Stamp = 0
            });
            await db.SaveChangesAsync();
        }

        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync(uname, "Password1");
        if (!string.IsNullOrEmpty(cookie))
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
        }

        // Get current profile
        var profRes = await client.GetAsync("/api/profile");
        profRes.EnsureSuccessStatusCode();
        var p = await profRes.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(p);

        // Check current password_dt
        DateTime before;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            var u = await db.AxUsers.AsNoTracking().FirstAsync(x => x.Id == p!.Id);
            before = u.PasswordDt;
        }

        var upd = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p!.Code,
            FirstName = p.FirstName + "x",
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };

        var put = await client.PutAsJsonAsync("/api/profile", upd);
        put.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            var u = await db.AxUsers.AsNoTracking().FirstAsync(x => x.Id == p!.Id);
            Assert.Equal(before, u.PasswordDt);
            Assert.Equal(p.FirstName + "x", u.FirstName);
        }
    }

    [Fact]
    public async Task Profile_Password_Change_Updates_PasswordDt_When_New_Differs()
    {
        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        await EnsureAuthCookieAsync(client);

        var profRes = await client.GetAsync("/api/profile");
        profRes.EnsureSuccessStatusCode();
        var p = await profRes.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(p);

        DateTime before;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            var u = await db.AxUsers.AsNoTracking().FirstAsync(x => x.Id == p!.Id);
            before = u.PasswordDt;
        }

        var upd = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p!.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp,
            CurrentPassword = "Password1",
            NewPassword = "Password2!",
            ConfirmNewPassword = "Password2!"
        };

        var put = await client.PutAsJsonAsync("/api/profile", upd);
        put.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            var u = await db.AxUsers.AsNoTracking().FirstAsync(x => x.Id == p!.Id);
            Assert.NotEqual(before, u.PasswordDt);
        }

        // refresh to get latest stamp, then restore to original to avoid side effects for other tests
        var profRes2 = await client.GetAsync("/api/profile");
        profRes2.EnsureSuccessStatusCode();
        var p2 = await profRes2.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(p2);
        var restore = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p2!.Code,
            FirstName = p2.FirstName,
            LastName = p2.LastName,
            Email = p2.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p2.PhoneNumber) ? "+381601234567" : p2.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p2.City) ? "Belgrade" : p2.City,
            StreetAddress = string.IsNullOrWhiteSpace(p2.StreetAddress) ? "Main 1" : p2.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p2.PostalCode) ? "11000" : p2.PostalCode,
            Stamp = p2.Stamp,
            CurrentPassword = "Password2!",
            NewPassword = "Password1",
            ConfirmNewPassword = "Password1"
        };
        var put2 = await client.PutAsJsonAsync("/api/profile", restore);
        put2.EnsureSuccessStatusCode();
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }

    private static async Task EnsureAuthCookieAsync(HttpClient client)
    {
        var resp = await client.PostAsJsonAsync("/api/login", new OnlineContract.Dtos.LoginDto { Code = "testuser", Password = "Password1" });
        if (!resp.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (var v in values)
        {
            var parts = v.Split(';');
            var nv = parts[0];
            if (nv.StartsWith(".OnlineContract.Auth="))
            {
                client.DefaultRequestHeaders.Remove("Cookie");
                client.DefaultRequestHeaders.Add("Cookie", nv);
                break;
            }
        }
    }
}
