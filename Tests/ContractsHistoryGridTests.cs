using System.Net;
using Xunit;

namespace OnlineContract.Tests;

public class ContractsHistoryGridTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractsHistoryGridTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Items_Page_Uses_Standard_Grid_Markup()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var resp = await client.GetAsync("/contractshistory/500");
        Assert.True(resp.IsSuccessStatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("<table id=\"grid\"", html);
        Assert.Contains("class=\"table-modern", html);
        Assert.Contains("mod-pagination", html);
        Assert.Contains("id=\"prevPage\"", html);
        Assert.Contains("id=\"nextPage\"", html);
        Assert.Contains("id=\"pageSize\"", html);
    }
}
