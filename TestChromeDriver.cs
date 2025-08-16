using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace EinAutomation.Api
{
    public class TestChromeDriver
    {
        private readonly ILogger<TestChromeDriver> _logger;

        public TestChromeDriver(ILogger<TestChromeDriver> logger)
        {
            _logger = logger;
        }

        public bool TestChromeDriverInitialization()
        {
            try
            {
                _logger.LogInformation("Testing ChromeDriver initialization...");
                
                // Check if we're in a container
                var isContainer = Environment.GetEnvironmentVariable("CONTAINER_ENV") == "true" || 
                                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                                File.Exists("/.dockerenv");
                
                _logger.LogInformation("Container environment detected: {IsContainer}", isContainer);
                
                // Check for ChromeDriver in expected locations
                var possiblePaths = new[]
                {
                    "/usr/local/bin/chromedriver",
                    "/usr/bin/chromedriver",
                    "/opt/chromedriver",
                    "./chromedriver"
                };
                
                string? chromedriverPath = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        chromedriverPath = path;
                        _logger.LogInformation("Found ChromeDriver at: {Path}", path);
                        break;
                    }
                }
                
                if (chromedriverPath == null)
                {
                    _logger.LogError("ChromeDriver not found in any expected location");
                    return false;
                }
                
                // Test ChromeDriver service creation
                OpenQA.Selenium.Chrome.ChromeDriverService driverService;
                try
                {
                    var directory = Path.GetDirectoryName(chromedriverPath);
                    var filename = Path.GetFileName(chromedriverPath);
                    driverService = OpenQA.Selenium.Chrome.ChromeDriverService.CreateDefaultService(directory!, filename);
                    _logger.LogInformation("Successfully created ChromeDriver service");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create ChromeDriver service with explicit path");
                    return false;
                }
                
                // Test ChromeDriver initialization
                try
                {
                    var options = new ChromeOptions();
                    options.AddArgument("--headless");
                    options.AddArgument("--no-sandbox");
                    options.AddArgument("--disable-dev-shm-usage");
                    options.AddArgument("--disable-gpu");
                    
                    using var driver = new ChromeDriver(driverService, options);
                    _logger.LogInformation("Successfully created ChromeDriver instance");
                    
                    // Test basic navigation
                    driver.Navigate().GoToUrl("https://www.google.com");
                    _logger.LogInformation("Successfully navigated to Google");
                    
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create ChromeDriver instance or navigate");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during ChromeDriver test");
                return false;
            }
        }
    }
} 