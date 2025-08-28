using Microsoft.AspNetCore.Mvc;
using EinAutomation.Api.TestClasses;

namespace EinAutomation.Api.Controllers;

[ApiController]
[Route("test/sample-pdf")]
public class SamplePdfDownloadController : ControllerBase
{
    private readonly ILoggerFactory _loggerFactory;

    public SamplePdfDownloadController(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    [HttpGet("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var logger = _loggerFactory.CreateLogger<SamplePdfDownloadTest>();
        var test = new SamplePdfDownloadTest(logger);
        await test.RunAllAsync(ct);
        
        return Ok(new { message = "Test completed. Check logs for results." });
    }
}