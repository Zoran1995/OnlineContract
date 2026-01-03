using Microsoft.AspNetCore.Mvc;
using OnlineContract.Services;

namespace OnlineContract.Controllers
{
    [ApiController]
    [Route("health")]
    public class EmailHealthController : ControllerBase
    {
        private readonly IEmailService _svc;

        public EmailHealthController(IEmailService svc)
        {
            _svc = svc;
        }

        [HttpGet("/health/email")]
        public async Task<IActionResult> Get()
        {
            var ok = await _svc.HealthAsync();
            if (ok) return Ok(new { status = "UP" });
            return StatusCode(503, new { status = "DOWN" });
        }
    }
}
