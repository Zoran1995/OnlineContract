using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace OnlineContract.Infrastructure
{
    public static class JsonResultHelper
    {
        public static IActionResult StableJson(IHostEnvironment env, object payload)
        {
            if (env.IsEnvironment("Testing"))
            {
                var json = JsonSerializer.Serialize(payload);
                return new ContentResult { Content = json, ContentType = "application/json", StatusCode = 200 };
            }
            return new JsonResult(payload);
        }

        public static IActionResult StableJson(IHostEnvironment env, object payload, int status)
        {
            if (env.IsEnvironment("Testing"))
            {
                var json = JsonSerializer.Serialize(payload);
                return new ContentResult { Content = json, ContentType = "application/json", StatusCode = status };
            }
            return new JsonResult(payload) { StatusCode = status };
        }
    }
}