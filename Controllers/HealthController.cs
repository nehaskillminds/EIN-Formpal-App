using Microsoft.AspNetCore.Mvc;

namespace EinAutomation.Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly ILogger<HealthController> _logger;

        public HealthController(ILogger<HealthController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Get()
        {
            _logger.LogInformation("Health check requested");
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }

        [HttpGet("ready")]
        public IActionResult Ready()
        {
            _logger.LogInformation("Readiness check requested");
            return Ok(new { status = "ready", timestamp = DateTime.UtcNow });
        }

        [HttpGet("live")]
        public IActionResult Live()
        {
            _logger.LogInformation("Liveness check requested");
            return Ok(new { status = "alive", timestamp = DateTime.UtcNow });
        }
    }
}
