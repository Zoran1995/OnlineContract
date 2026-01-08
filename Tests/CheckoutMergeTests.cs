using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using Xunit;

namespace OnlineContract.Tests;

public class CheckoutMergeTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public CheckoutMergeTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task AnonymousCart_Merges_OnCheckout_AfterLogin()
    {
        // Anonymous add
        var clientAnon = _factory.CreateClientNoRedirect();
        var addReq = JsonContent.Create(new { ProductVariantId = 101, Quantity = 2 });
        var addResp = await clientAnon.PostAsync("/api/anon-cart/items", addReq);
        Assert.True(addResp.IsSuccessStatusCode);
        var setCookie = addResp.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.FirstOrDefault() : null;
        if (!string.IsNullOrEmpty(setCookie)) clientAnon.DefaultRequestHeaders.Add("Cookie", ExtractCookie(setCookie, "anon_cart_id"));

        // Unauthenticated checkout -> redirect
        var ch1 = await clientAnon.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, ch1.StatusCode);
        Assert.Contains("/login?returnUrl=%2Fcheckout", ch1.Headers.Location!.ToString());

        // Authenticate and checkout (reuse anon cookie)
        var (clientAuth, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) clientAuth.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie, ".OnlineContract.Auth"));
        if (!string.IsNullOrEmpty(setCookie)) clientAuth.DefaultRequestHeaders.Add("Cookie", ExtractCookie(setCookie!, "anon_cart_id"));
        var ch2 = await clientAuth.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, ch2.StatusCode);
        Assert.Equal("/contracts", ch2.Headers.Location!.ToString());

        // Validate DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var draft = await db.Contracts.FirstOrDefaultAsync(c => (c.InputUserId ?? 0) == 123 && c.ContractState == OnlineContract.Helpers.ContractState.Draft && c.IsActive && !c.IsDeleted);
        Assert.NotNull(draft);
        Assert.True(draft!.Amount > 0m);
        // created new or updated existing draft
        Assert.True(draft.Stamp >= 0);
        var dets = await db.ContractDets.Where(d => d.ContractId == draft.Id).ToListAsync();
        Assert.NotEmpty(dets);
        Assert.Equal(2, dets.Sum(d => d.Quantity));
    }
    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}
