using EinAutomation.Api.Models;
using EinAutomation.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;

namespace EinAutomation.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EinController : ControllerBase
    {
        private readonly IAutomationOrchestrator _orchestrator;
        private readonly IFormDataMapper _formDataMapper;
        private readonly ILogger<EinController> _logger;
        private readonly ILoggerFactory _loggerFactory;

        public EinController(
            IAutomationOrchestrator orchestrator,
            IFormDataMapper formDataMapper,
            ILogger<EinController> logger,
            ILoggerFactory loggerFactory)
        {
            _orchestrator = orchestrator;
            _formDataMapper = formDataMapper;
            _logger = logger;
            _loggerFactory = loggerFactory;
        }

        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            try
            {
                // Add any additional health checks here if needed
                // For example: database connection check, service dependencies, etc.
                
                var healthResponse = new
                {
                    Status = "Healthy",
                    Timestamp = DateTime.UtcNow,
                    Version = typeof(EinController).Assembly.GetName().Version?.ToString() ?? "1.0.0"
                };

                _logger.LogInformation("Health check executed successfully");
                return Ok(healthResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return StatusCode(500, new
                {
                    Status = "Unhealthy",
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpGet("test-chromedriver")]
        public IActionResult TestChromeDriver()
        {
            try
            {
                var testService = new TestChromeDriver(_loggerFactory.CreateLogger<TestChromeDriver>());
                var success = testService.TestChromeDriverInitialization();
                
                return Ok(new
                {
                    Success = success,
                    Message = success ? "ChromeDriver test passed" : "ChromeDriver test failed",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChromeDriver test failed");
                return StatusCode(500, new
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        [HttpPost("run-irs-ein")]
        // [Authorize]
        public async Task<IActionResult> RunIrsEinAsync([FromBody] JsonElement request, CancellationToken ct)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(request.GetRawText());
                if (data == null || !data.ContainsKey("entityProcessId") || data["formType"]?.ToString() != "EIN")
                {
                    _logger.LogError("Invalid payload or formType");
                    return BadRequest("Invalid payload or formType");
                }

                var caseData = _formDataMapper.MapFormAutomationData(data);
                var (success, einNumber, azureBlobUrl) = await _orchestrator.RunAsync(caseData, ct);

                if (success)
                {
                    return Ok(new
                    {
                        Message = "Form submitted successfully",
                        Status = "Submitted",
                        RecordId = caseData.RecordId,
                        AzureBlobUrl = azureBlobUrl
                    });
                }

                return BadRequest(einNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Endpoint error");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}