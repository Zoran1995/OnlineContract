using System.Net;
using System.Net.Http.Json;
using OnlineContract.Helpers;
using Xunit;

namespace OnlineContract.Tests;

public class ChangeStateModalTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ChangeStateModalTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task ModalData_For_DraftItem_Returns_CurrentName_And_NotEndState()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var resp = await client.GetAsync("/api/contracts/items/501/state/modal-data");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ModalDto>();
        Assert.NotNull(dto);
        Assert.Equal((int)ProductStateInOrder.Draft, dto!.CurrentStateId);
        Assert.Equal("Draft", dto.CurrentStateName);
        Assert.False(dto.IsEndState);
        Assert.Empty(dto.NextStates);
    }

    [Fact]
    public async Task ModalData_For_RejectedItem_Returns_EndState()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var resp = await client.GetAsync("/api/contracts/items/502/state/modal-data");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<ModalDto>();
        Assert.NotNull(dto);
        Assert.Equal((int)ProductStateInOrder.Rejected, dto!.CurrentStateId);
        Assert.Equal("Rejected", dto.CurrentStateName);
        Assert.True(dto.IsEndState);
        Assert.Empty(dto.NextStates);
    }

    [Fact]
    public async Task SetState_InvalidTransition_Returns400()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
        var resp = await client.PostAsJsonAsync("/api/contracts/items/501/state/set", new { nextStateId = (int)ProductStateInOrder.Rejected });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class ModalDto
{
    public int CurrentStateId { get; set; }
    public string CurrentStateName { get; set; } = string.Empty;
    public bool IsEndState { get; set; }
    public List<NextStateDto> NextStates { get; set; } = new();
}

file sealed class NextStateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
