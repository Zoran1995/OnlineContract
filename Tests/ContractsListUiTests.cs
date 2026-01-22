using System.Net;
using Xunit;

namespace OnlineContract.Tests;

public class ContractsListUiTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractsListUiTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async System.Threading.Tasks.Task Contracts_List_No_Add_Button()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);
        var resp2 = await client.GetAsync("/contracts.html");
        var html = await resp2.Content.ReadAsStringAsync();
        Assert.DoesNotContain("btnAddContract", html);
    }
}