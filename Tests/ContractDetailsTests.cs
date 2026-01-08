using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests;

public class ContractDetailsTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractDetailsTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Contract_Details_Includes_Codes_Dates_And_Lookup_State_Text()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var resp = await client.GetAsync("/api/contracts/500");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ContractDetailsDto>();
        Assert.NotNull(dto);
        Assert.Equal(500, dto!.id);
        Assert.False(string.IsNullOrEmpty(dto.contractStateText));
        Assert.False(string.IsNullOrEmpty(dto.entryDate));
        Assert.True(dto.stamp >= 0);
        Assert.NotNull(dto.inputUserCode);
        Assert.NotNull(dto.lastModifiedByCode);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class ContractDetailsDto
{
    public int id { get; set; }
    public string customerFullName { get; set; } = string.Empty;
    public int? inputUserId { get; set; }
    public string contractState { get; set; } = string.Empty;
    public string contractStateText { get; set; } = string.Empty;
    public string entryDate { get; set; } = string.Empty;
    public int? lastModifiedById { get; set; }
    public string inputUserCode { get; set; } = string.Empty;
    public string lastModifiedByCode { get; set; } = string.Empty;
    public string? lastUpdatedDt { get; set; }
    public int stamp { get; set; }
}
