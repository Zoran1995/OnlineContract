using System.Linq;
using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests
{
    public class ProductsActivationTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public ProductsActivationTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task Toggle_Product_Active_State_Via_Query_Stamp()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var list1 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/products?page=1&pageSize=50");
            var p = list1!["items"]!.AsArray().First()!.AsObject();
            int id = (int)p["id"]!;
            int stamp = (int)p["stamp"]!;
            bool isActive = (bool)p["isActive"]!;

            var kind = isActive ? "deactivate" : "activate";
            var resp = await client.PostAsync($"/api/products/{id}/{kind}?stamp={stamp}", new System.Net.Http.StringContent(""));
            resp.EnsureSuccessStatusCode();

            var list2 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/products?page=1&pageSize=50");
            var updated = list2!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.NotEqual(isActive, (bool)updated["isActive"]!);

            // Now toggle back using the new stamp, to verify both directions
            int newStamp = (int)updated["stamp"]!;
            var resp2 = await client.PostAsync($"/api/products/{id}/{(isActive ? "activate" : "deactivate")}?stamp={newStamp}", new System.Net.Http.StringContent(""));
            resp2.EnsureSuccessStatusCode();
            var list3 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/products?page=1&pageSize=50");
            var roundTrip = list3!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.Equal(isActive, (bool)roundTrip["isActive"]!);
        }

        [Fact]
        public async System.Threading.Tasks.Task Wrong_Stamp_Returns_Conflict_Message()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var list1 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/products?page=1&pageSize=50");
            var p = list1!["items"]!.AsArray().First()!.AsObject();
            int id = (int)p["id"]!;

            var resp = await client.PostAsync($"/api/products/{id}/activate?stamp=999999", new System.Net.Http.StringContent(""));
            var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Assert.True(body!["success"]!.GetValue<bool>() == false);
        }

        private static string ExtractCookie(string setCookieHeader, string cookieName)
        {
            var parts = setCookieHeader.Split(';');
            var nv = parts[0];
            if (nv.StartsWith(cookieName + "=")) return nv;
            return nv;
        }
    }
}