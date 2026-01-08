using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests;

public class ContractsListTests : IClassFixture<WebAppFactory>
{
	private readonly WebAppFactory _factory;
	public ContractsListTests(WebAppFactory factory) { _factory = factory; }

	[Fact]
	public async Task Contracts_List_Shows_Lookup_Text_For_State()
	{
		var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
		if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
		var resp = await client.GetAsync("/api/contracts?page=1&pageSize=10");
		Assert.True(resp.IsSuccessStatusCode);
		var payload = await resp.Content.ReadFromJsonAsync<ContractListDto>();
		Assert.NotNull(payload);
		Assert.True(payload!.items.Count >= 1);
		var row = payload.items.FirstOrDefault(x => x.id == 500);
		Assert.NotNull(row);
		Assert.False(string.IsNullOrEmpty(row!.contractStateText));
	}

	private static string ExtractCookie(string setCookieHeader, string cookieName)
	{
		var parts = setCookieHeader.Split(';');
		var nv = parts[0];
		if (nv.StartsWith(cookieName + "=")) return nv;
		return nv;
	}
}

file sealed class ContractListDto
{
	public List<ContractListItem> items { get; set; } = new();
	public int totalCount { get; set; }
	public int totalPages { get; set; }
}

file sealed class ContractListItem
{
	public int id { get; set; }
	public string customerFullName { get; set; } = string.Empty;
	public decimal amount { get; set; }
	public string contractState { get; set; } = string.Empty;
	public string contractStateText { get; set; } = string.Empty;
	public string entryDate { get; set; } = string.Empty;
	public string deliveredDate { get; set; } = string.Empty;
	public string writtenOffDate { get; set; } = string.Empty;
	public string rejectedDate { get; set; } = string.Empty;
	public string cancelledDate { get; set; } = string.Empty;
}
