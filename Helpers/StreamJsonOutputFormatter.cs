using Microsoft.AspNetCore.Mvc.Formatters;
using System.Text;
using System.Text.Json;

namespace OnlineContract.Helpers
{
    // Custom JSON output formatter used only in Testing to avoid PipeWriter
    // which is not supported by TestServer's ResponseBodyPipeWriter in this setup.
    public class StreamJsonOutputFormatter : TextOutputFormatter
    {
        private readonly JsonSerializerOptions _options;

        public StreamJsonOutputFormatter(JsonSerializerOptions options)
        {
            _options = options;
            SupportedMediaTypes.Add("application/json");
            SupportedMediaTypes.Add("application/problem+json");
            SupportedMediaTypes.Add("text/json");
            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
        }

        protected override bool CanWriteType(Type? type)
        {
            return true; // Use for all types in Testing
        }

        public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            var response = context.HttpContext.Response;
            if (context.Object is null)
            {
                await response.Body.WriteAsync(Array.Empty<byte>());
                return;
            }

            await JsonSerializer.SerializeAsync(response.Body, context.Object, context.Object.GetType(), _options);
        }
    }
}
