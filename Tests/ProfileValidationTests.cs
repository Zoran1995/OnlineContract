using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class ProfileValidationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ProfileValidationTests(WebAppFactory factory) { _factory = factory; }

    private static async Task<(OnlineContract.Dtos.ProfileDto p, HttpClient client)> GetProfileAsync(WebAppFactory factory)
    {
        // Use an isolated user per test to avoid cross-test interference
        string uname = $"val_{Guid.NewGuid().ToString("N").Substring(0,8)}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            db.AxUsers.Add(new AxUser
            {
                Code = uname,
                Email = $"{uname}@example.com",
                FirstName = "Val",
                LastName = "User",
                RoleId = 5,
                IsActive = true,
                IsDeleted = false,
                Password = OnlineContract.Helpers.PasswordHelper.HashPassword("Password1"),
                CreatedDt = DateTime.Now,
                PasswordDt = DateTime.Now,
                Phone = "+381601234567",
                City = "Belgrade",
                StreetAddress = "Main 1",
                PostalCode = "11000",
                Stamp = 0
            });
            db.SaveChanges();
        }

        var (client, setCookie) = await factory.CreateAuthenticatedClientAsync(uname, "Password1");
        if (!string.IsNullOrEmpty(setCookie))
        {
            var cookieParts = setCookie.Split(',');
            string? authPair = null;
            foreach (var part in cookieParts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith(".OnlineContract.Auth=")) { authPair = trimmed.Split(';')[0]; break; }
            }
            if (authPair == null) authPair = setCookie.Split(';')[0];
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", authPair);
        }
        var res = await client.GetAsync("/api/profile");
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(dto);
        return (dto!, client);
    }

    

    private static async Task<(JsonDocument doc, HttpResponseMessage resp)> PutAsync(HttpClient client, object body)
    {
        var put = await client.PutAsJsonAsync("/api/profile", body);
        var text = await put.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        return (doc, put);
    }

    [Fact]
    public async Task Username_Format_Invalid()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = "1bad",
            FirstName = string.IsNullOrEmpty(p.FirstName) ? "Test" : p.FirstName,
            LastName = string.IsNullOrEmpty(p.LastName) ? "User" : p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("Username must start with a letter", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Username_Not_Unique()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            db.AxUsers.Add(new AxUser
            {
                Code = "takenuser",
                Email = "someone@example.com",
                RoleId = 5,
                IsActive = true,
                IsDeleted = false,
                Password = OnlineContract.Helpers.PasswordHelper.HashPassword("Password9!"),
                FirstName = "Other",
                LastName = "User",
                CreatedDt = DateTime.Now,
                PasswordDt = DateTime.Now,
                Stamp = 0
            });
            db.SaveChanges();
        }

        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = "takenuser",
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Username is already taken.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Email_Format_Invalid()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = "not-an-email",
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Email format is invalid.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Email_Not_Unique()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OnlineContract.Data.AppDbContext>();
            db.AxUsers.Add(new AxUser
            {
                Code = "otheruser",
                Email = "used@example.com",
                RoleId = 5,
                IsActive = true,
                IsDeleted = false,
                Password = OnlineContract.Helpers.PasswordHelper.HashPassword("Password9!"),
                FirstName = "Other",
                LastName = "User",
                CreatedDt = DateTime.Now,
                PasswordDt = DateTime.Now,
                Stamp = 0
            });
            db.SaveChanges();
        }

        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = "used@example.com",
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Email is already in use.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Phone_Invalid_Extension_Text()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = "060 123 456 ext 3",
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("omit extension", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_Missing_Fields()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp,
            NewPassword = "Password2!"
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("fill all three fields", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_Current_Incorrect()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp,
            CurrentPassword = "Wrong",
            NewPassword = "Password2!",
            ConfirmNewPassword = "Password2!"
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Current password is incorrect.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Password_Mismatch()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
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
            ConfirmNewPassword = "Password3!"
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("New password and confirmation do not match.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Password_Complexity_Fails()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp,
            CurrentPassword = "Password1",
            NewPassword = "password", // lacks uppercase and digit
            ConfirmNewPassword = "password"
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("at least 8 chars", doc.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_Same_As_Current_Fails()
    {
        // First change to a complex password so it meets complexity
        var (p, client) = await GetProfileAsync(_factory);
        var firstChange = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
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
        var (firstDoc, firstResp) = await PutAsync(client, firstChange);
        Assert.True(firstResp.IsSuccessStatusCode);

        // Reload profile to get updated stamp using the same authenticated client
        var profRes = await client.GetAsync("/api/profile");
        profRes.EnsureSuccessStatusCode();
        var p2 = await profRes.Content.ReadFromJsonAsync<OnlineContract.Dtos.ProfileDto>();
        Assert.NotNull(p2);

        // Now attempt to change to the same password again
        var repeat = new OnlineContract.Dtos.ProfileUpdateDto
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
            NewPassword = "Password2!",
            ConfirmNewPassword = "Password2!"
        };
        var (doc, resp) = await PutAsync(client, repeat);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("New password must be different from the current password.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostalCode_Required()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = "",
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Postal code is required.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostalCode_Length_Must_Be_5()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = "1234", // invalid length
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Postal code must be exactly 5 characters.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task City_Required()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = "",
            StreetAddress = string.IsNullOrWhiteSpace(p.StreetAddress) ? "Main 1" : p.StreetAddress,
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("City is required.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StreetAddress_Required()
    {
        var (p, client) = await GetProfileAsync(_factory);
        var body = new OnlineContract.Dtos.ProfileUpdateDto
        {
            Code = p.Code,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Email = p.Email,
            PhoneNumber = string.IsNullOrWhiteSpace(p.PhoneNumber) ? "+381601234567" : p.PhoneNumber,
            City = string.IsNullOrWhiteSpace(p.City) ? "Belgrade" : p.City,
            StreetAddress = "",
            PostalCode = string.IsNullOrWhiteSpace(p.PostalCode) ? "11000" : p.PostalCode,
            Stamp = p.Stamp
        };
        var (doc, resp) = await PutAsync(client, body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Street address is required.", doc.RootElement.GetProperty("message").GetString());
    }
}
