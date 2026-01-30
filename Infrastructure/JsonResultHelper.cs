using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace OnlineContract.Infrastructure
{
    public static class JsonResultHelper
    {
        // Shared options with UnsafeRelaxedJsonEscaping for proper Latin diacritics (ć, č, š, ž, đ)
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static IActionResult StableJson(IHostEnvironment env, object payload)
        {
            if (env.IsEnvironment("Testing"))
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
            }
            return new JsonResult(payload, _jsonOptions);
        }

        public static IActionResult StableJson(IHostEnvironment env, object payload, int status)
        {
            if (env.IsEnvironment("Testing"))
            {
                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                return new ContentResult { Content = json, ContentType = "application/json", StatusCode = status };
            }
            return new JsonResult(payload, _jsonOptions) { StatusCode = status };
        }
    }
}