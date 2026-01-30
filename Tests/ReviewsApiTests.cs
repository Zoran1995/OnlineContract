using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace OnlineContract.Tests
{
    public class ReviewsApiTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;

        public ReviewsApiTests(WebAppFactory factory)
        {
            _factory = factory;
        }

        private static string ExtractCookie(string setCookieHeader, string cookieName)
        {
            var parts = setCookieHeader.Split(';');
            var nv = parts[0];
            if (nv.StartsWith(cookieName + "=")) return nv;
            return nv;
        }

        [Fact]
        public async Task List_Returns_Valid_Response()
        {
            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/api/reviews?page=1&pageSize=10");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.NotNull(json["totalCount"]);
            Assert.NotNull(json["totalReviews"]);
            Assert.NotNull(json["averageRating"]);
            Assert.NotNull(json["items"]);
            Assert.True(json["items"]!.AsArray() != null);
        }

        [Fact]
        public async Task Create_Anonymous_Review_Returns_Success()
        {
            var client = _factory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/reviews", new { mark = 5, comment = "Great products!" });
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.True(json["success"]!.GetValue<bool>());
            Assert.NotNull(json["review"]);

            var review = json["review"]!.AsObject();
            Assert.Equal(5, (int)review["mark"]!);
            Assert.Equal("Great products!", review["comment"]!.GetValue<string>());
            Assert.Equal("Anonymous", review["userCode"]!.GetValue<string>());
            Assert.Equal(0, (int)review["inputUserId"]!);
        }

        [Fact]
        public async Task Create_Authenticated_Review_Returns_UserCode()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(authCookie))
                client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var resp = await client.PostAsJsonAsync("/api/reviews", new { mark = 4, comment = "Nice quality" });
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.True(json["success"]!.GetValue<bool>());

            var review = json["review"]!.AsObject();
            Assert.Equal(4, (int)review["mark"]!);
            Assert.Equal("testuser", review["userCode"]!.GetValue<string>());
            Assert.True((int)review["inputUserId"]! > 0);
        }

        [Fact]
        public async Task Create_Review_InvalidMark_Returns_BadRequest()
        {
            var client = _factory.CreateClient();

            // Mark = 0
            var resp1 = await client.PostAsJsonAsync("/api/reviews", new { mark = 0, comment = "Test" });
            Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

            // Mark = 6
            var resp2 = await client.PostAsJsonAsync("/api/reviews", new { mark = 6, comment = "Test" });
            Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);

            // Mark = -1
            var resp3 = await client.PostAsJsonAsync("/api/reviews", new { mark = -1, comment = "Test" });
            Assert.Equal(HttpStatusCode.BadRequest, resp3.StatusCode);
        }

        [Fact]
        public async Task Create_Review_Without_Comment_Succeeds()
        {
            var client = _factory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/reviews", new { mark = 3 });
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.True(json["success"]!.GetValue<bool>());

            var review = json["review"]!.AsObject();
            Assert.Equal(3, (int)review["mark"]!);
            Assert.True(review["comment"] == null || string.IsNullOrEmpty(review["comment"]!.GetValue<string>()));
        }

        [Fact]
        public async Task List_Calculates_Average_Rating_Correctly()
        {
            var client = _factory.CreateClient();

            // Create multiple reviews with different ratings
            await client.PostAsJsonAsync("/api/reviews", new { mark = 5, comment = "Excellent" });
            await client.PostAsJsonAsync("/api/reviews", new { mark = 4, comment = "Good" });
            await client.PostAsJsonAsync("/api/reviews", new { mark = 3, comment = "Average" });

            var resp = await client.GetAsync("/api/reviews?page=1&pageSize=10");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.True((int)json["totalCount"]! >= 3);

            // Average should be calculated
            decimal avg = json["averageRating"]!.GetValue<decimal>();
            Assert.True(avg > 0);
        }

        [Fact]
        public async Task Summary_Returns_Stats()
        {
            var client = _factory.CreateClient();

            // Create a review first
            await client.PostAsJsonAsync("/api/reviews", new { mark = 5, comment = "Test for summary" });

            var resp = await client.GetAsync("/api/reviews/summary");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json);
            Assert.NotNull(json["averageRating"]);
            Assert.NotNull(json["totalReviews"]);
            Assert.True((int)json["totalReviews"]! >= 1);
        }

        [Fact]
        public async Task List_Pagination_Works()
        {
            var client = _factory.CreateClient();

            // Create 6 reviews
            for (int i = 1; i <= 6; i++)
            {
                await client.PostAsJsonAsync("/api/reviews", new { mark = i % 5 + 1, comment = $"Review {i}" });
            }

            // Get page 1 with page size 3
            var resp1 = await client.GetAsync("/api/reviews?page=1&pageSize=3");
            resp1.EnsureSuccessStatusCode();
            var json1 = await resp1.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json1);
            Assert.Equal(3, json1["items"]!.AsArray().Count);
            Assert.True((int)json1["totalPages"]! >= 2);

            // Get page 2
            var resp2 = await client.GetAsync("/api/reviews?page=2&pageSize=3");
            resp2.EnsureSuccessStatusCode();
            var json2 = await resp2.Content.ReadFromJsonAsync<JsonObject>();
            Assert.NotNull(json2);
            Assert.True(json2["items"]!.AsArray().Count >= 1);
        }

        [Fact]
        public async Task Delete_Own_Review_Succeeds()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(authCookie))
                client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            // Create a review
            var createResp = await client.PostAsJsonAsync("/api/reviews", new { mark = 5, comment = "To be deleted" });
            createResp.EnsureSuccessStatusCode();
            var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
            int reviewId = (int)createJson!["review"]!.AsObject()["id"]!;
            int stamp = (int)createJson!["review"]!.AsObject()["stamp"]!;

            // Delete the review
            var deleteResp = await client.PostAsync($"/api/reviews/{reviewId}/delete?stamp={stamp}", new StringContent(""));
            deleteResp.EnsureSuccessStatusCode();

            var deleteJson = await deleteResp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.True(deleteJson!["success"]!.GetValue<bool>());
        }

        [Fact]
        public async Task Delete_Others_Review_Forbidden_For_NonAdmin()
        {
            // First, create a review as admin
            var (adminClient, adminCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(adminCookie))
                adminClient.DefaultRequestHeaders.Add("Cookie", ExtractCookie(adminCookie!, ".OnlineContract.Auth"));

            var createResp = await adminClient.PostAsJsonAsync("/api/reviews", new { mark = 5, comment = "Admin review" });
            createResp.EnsureSuccessStatusCode();
            var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
            int reviewId = (int)createJson!["review"]!.AsObject()["id"]!;
            int stamp = (int)createJson!["review"]!.AsObject()["stamp"]!;

            // Try to delete as regular user
            var (userClient, userCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(userCookie))
                userClient.DefaultRequestHeaders.Add("Cookie", ExtractCookie(userCookie!, ".OnlineContract.Auth"));

            var deleteResp = await userClient.PostAsync($"/api/reviews/{reviewId}/delete?stamp={stamp}", new StringContent(""));
            Assert.Equal(HttpStatusCode.Forbidden, deleteResp.StatusCode);
        }

        [Fact]
        public async Task Delete_With_Wrong_Stamp_Returns_Conflict()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(authCookie))
                client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            // Create a review
            var createResp = await client.PostAsJsonAsync("/api/reviews", new { mark = 4, comment = "Concurrency test" });
            createResp.EnsureSuccessStatusCode();
            var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
            int reviewId = (int)createJson!["review"]!.AsObject()["id"]!;

            // Try to delete with wrong stamp
            var deleteResp = await client.PostAsync($"/api/reviews/{reviewId}/delete?stamp=999", new StringContent(""));
            Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
        }

        [Fact]
        public async Task Delete_NonExistent_Review_Returns_NotFound()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie))
                client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var deleteResp = await client.PostAsync("/api/reviews/999999/delete?stamp=0", new StringContent(""));
            Assert.Equal(HttpStatusCode.NotFound, deleteResp.StatusCode);
        }

        [Fact]
        public async Task Delete_RequiresAuth()
        {
            var client = _factory.CreateClient();
            var deleteResp = await client.PostAsync("/api/reviews/1/delete?stamp=0", new StringContent(""));
            // Should redirect or return unauthorized
            Assert.True(deleteResp.StatusCode == HttpStatusCode.Unauthorized || 
                        deleteResp.StatusCode == HttpStatusCode.Redirect ||
                        (int)deleteResp.StatusCode == 302);
        }

        [Fact]
        public async Task Admin_Can_Delete_Any_Review()
        {
            // Create anonymous review
            var anonClient = _factory.CreateClient();
            var createResp = await anonClient.PostAsJsonAsync("/api/reviews", new { mark = 2, comment = "Anonymous to delete" });
            createResp.EnsureSuccessStatusCode();
            var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
            int reviewId = (int)createJson!["review"]!.AsObject()["id"]!;
            int stamp = (int)createJson!["review"]!.AsObject()["stamp"]!;

            // Delete as admin
            var (adminClient, adminCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(adminCookie))
                adminClient.DefaultRequestHeaders.Add("Cookie", ExtractCookie(adminCookie!, ".OnlineContract.Auth"));

            var deleteResp = await adminClient.PostAsync($"/api/reviews/{reviewId}/delete?stamp={stamp}", new StringContent(""));
            deleteResp.EnsureSuccessStatusCode();

            var deleteJson = await deleteResp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.True(deleteJson!["success"]!.GetValue<bool>());
        }

        [Fact]
        public async Task Deleted_Review_Not_In_List()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(authCookie))
                client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            // Create and delete a review
            var createResp = await client.PostAsJsonAsync("/api/reviews", new { mark = 1, comment = "Will be deleted" });
            createResp.EnsureSuccessStatusCode();
            var createJson = await createResp.Content.ReadFromJsonAsync<JsonObject>();
            int reviewId = (int)createJson!["review"]!.AsObject()["id"]!;
            int stamp = (int)createJson!["review"]!.AsObject()["stamp"]!;

            await client.PostAsync($"/api/reviews/{reviewId}/delete?stamp={stamp}", new StringContent(""));

            // Check list doesn't include the deleted review
            var listResp = await client.GetAsync("/api/reviews?page=1&pageSize=100");
            listResp.EnsureSuccessStatusCode();
            var listJson = await listResp.Content.ReadFromJsonAsync<JsonObject>();
            var items = listJson!["items"]!.AsArray();

            bool found = items.Any(i => (int)i!.AsObject()["id"]! == reviewId);
            Assert.False(found);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task Create_Review_All_Valid_Marks(int mark)
        {
            var client = _factory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/reviews", new { mark, comment = $"Rating {mark}" });
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            Assert.True(json!["success"]!.GetValue<bool>());
            Assert.Equal(mark, (int)json["review"]!.AsObject()["mark"]!);
        }

        [Fact]
        public async Task Reviews_Ordered_By_Date_Descending()
        {
            var client = _factory.CreateClient();

            // Create reviews with slight delay
            await client.PostAsJsonAsync("/api/reviews", new { mark = 1, comment = "First" });
            await Task.Delay(50);
            await client.PostAsJsonAsync("/api/reviews", new { mark = 2, comment = "Second" });
            await Task.Delay(50);
            await client.PostAsJsonAsync("/api/reviews", new { mark = 3, comment = "Third (most recent)" });

            var resp = await client.GetAsync("/api/reviews?page=1&pageSize=3");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>();
            var items = json!["items"]!.AsArray();

            if (items.Count >= 2)
            {
                var first = items[0]!.AsObject();
                var second = items[1]!.AsObject();
                var firstDt = DateTime.Parse(first["inputDt"]!.GetValue<string>());
                var secondDt = DateTime.Parse(second["inputDt"]!.GetValue<string>());
                Assert.True(firstDt >= secondDt, "Reviews should be ordered by date descending");
            }
        }
    }
}
