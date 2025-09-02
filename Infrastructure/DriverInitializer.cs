
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace EinAutomation.Api.Infrastructure
{
    public static class DriverInitializer
    {
        public static IWebDriver InitializeLocal(string chromeDownloadDirectory, string recordId)
        {         
            // Create a unique download directory for the record
            var recordDownloadDirectory = Path.Combine(chromeDownloadDirectory, recordId);
            Console.WriteLine($"DriverInitializer: Creating download directory: {recordDownloadDirectory}");
            Directory.CreateDirectory(recordDownloadDirectory);
            Console.WriteLine($"DriverInitializer: Download directory created successfully: {Directory.Exists(recordDownloadDirectory)}");
            
            // Create a unique user data directory for this session
            var userDataDir = Path.Combine(Path.GetTempPath(), $"chrome-userdata-{Guid.NewGuid()}");
            Directory.CreateDirectory(userDataDir);
   
            var options = new ChromeOptions();
            // Set Chrome arguments
            options.AddArgument($"--user-data-dir={userDataDir}");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--enable-unsafe-swiftshader");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--start-maximized");
            options.AddArgument($"--download-directory={recordDownloadDirectory}");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");

            // Set Chrome preferences - use individual calls instead of dictionary
            options.AddUserProfilePreference("profile.default_content_setting_values.popups", 2);
            options.AddUserProfilePreference("profile.default_content_setting_values.notifications", 2);
            options.AddUserProfilePreference("profile.default_content_setting_values.geolocation", 2);
            options.AddUserProfilePreference("credentials_enable_service", false);
            options.AddUserProfilePreference("profile.password_manager_enabled", false);
            options.AddUserProfilePreference("autofill.profile_enabled", false);
            options.AddUserProfilePreference("autofill.credit_card_enabled", false);
            options.AddUserProfilePreference("password_manager_enabled", false);
            options.AddUserProfilePreference("profile.password_dismissed_save_prompt", true);
            options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);
            options.AddUserProfilePreference("download.prompt_for_download", false);
            options.AddUserProfilePreference("download.directory_upgrade", true);
            options.AddUserProfilePreference("safebrowsing.enabled", true);
            options.AddUserProfilePreference("download.default_directory", recordDownloadDirectory);
            options.AddUserProfilePreference("savefile.default_directory", recordDownloadDirectory);
            options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);
            options.AddUserProfilePreference("download.open_pdf_in_system_reader", false);
            options.AddUserProfilePreference("profile.default_content_setting_values.mixed_script", 1);

            // Use regular ChromeDriver with anti-detection options
            var service = ChromeDriverService.CreateDefaultService();
            var driver = new ChromeDriver(service, options);

            // Option 2: Or use default if ChromeDriver is in PATH
            // Driver = new ChromeDriver(options);

            // Override JS functions
            driver.ExecuteScript(@"
                    window.alert = function() { return true; };
                    window.confirm = function() { return true; };
                    window.prompt = function() { return null; };
                    window.open = function() { return null; };
                ");

            // Configure download behavior via CDP immediately after driver creation
            if (driver is ChromeDriver chromeDriver)
            {
                try
                {
                    // Set download behavior via CDP
                    var downloadBehavior = new Dictionary<string, object>
                    {
                        ["downloadPath"] = recordDownloadDirectory,
                        ["promptForDownload"] = false
                    };

                    chromeDriver.ExecuteCdpCommand("Page.setDownloadBehavior", downloadBehavior);
                    Console.WriteLine($"Successfully configured Chrome download behavior via CDP for directory: {recordDownloadDirectory}");
                }
                catch (Exception cdpEx)
                {
                    Console.WriteLine($"Failed to configure download directory via CDP: {cdpEx.Message}");
                }
            }

            return driver;
        }

        public static IWebDriver InitializeAKS(string chromeDownloadDirectory, string recordId)
        {
            try
            {
                // Each pod/job gets its own unique temp folder
                string uniqueProfileDir = Path.Combine(Path.GetTempPath(), "chrome-profile-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(uniqueProfileDir);
                Console.WriteLine($"DriverInitializer: Created unique profile directory: {uniqueProfileDir}");

                // Create download directory within the unique profile
                string downloadDir = Path.Combine(uniqueProfileDir, "downloads");
                Directory.CreateDirectory(downloadDir);
                Console.WriteLine($"DriverInitializer: Created download directory: {downloadDir}");

                // Generate unique remote debugging port
                var random = new Random();
                var remoteDebuggingPort = random.Next(9223, 9999);
                Console.WriteLine($"DriverInitializer: Using remote debugging port: {remoteDebuggingPort}");

                var options = new ChromeOptions
                {
                    BinaryLocation = "/usr/bin/chromium",
                    AcceptInsecureCertificates = true
                };

                // Use the unique profile directory
                options.AddArgument($"--user-data-dir={uniqueProfileDir}");
                options.AddArgument("--headless=new");
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-setuid-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--disable-gpu");
                options.AddArgument("--disable-software-rasterizer");
                options.AddArgument("--disable-infobars");
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddArgument("--disable-extensions");
                options.AddArgument("--no-first-run");
                options.AddArgument("--no-default-browser-check");
                options.AddArgument("--disable-background-networking");
                options.AddArgument("--disable-sync");
                options.AddArgument("--disable-default-apps");
                options.AddArgument("--disable-translate");
                options.AddArgument("--window-size=1920,1080");
                options.AddArgument($"--remote-debugging-port={remoteDebuggingPort}");
                options.AddArgument("--remote-debugging-address=0.0.0.0");
                options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                options.AddArgument($"--download-directory={downloadDir}");
                options.AddArgument("--disable-web-security");
                options.AddArgument("--allow-running-insecure-content");
                options.AddArgument("--disable-background-timer-throttling");
                options.AddArgument("--disable-backgrounding-occluded-windows");
                options.AddArgument("--disable-renderer-backgrounding");
                options.AddArgument("--disable-features=TranslateUI");
                options.AddArgument("--disable-ipc-flooding-protection");

                // Force PDF download and set download preferences
                options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);
                options.AddUserProfilePreference("download.default_directory", downloadDir);
                options.AddUserProfilePreference("download.prompt_for_download", false);
                options.AddUserProfilePreference("download.directory_upgrade", true);
                options.AddUserProfilePreference("safebrowsing.enabled", true);
                options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);
                options.AddUserProfilePreference("download.open_pdf_in_system_reader", false);
                options.AddUserProfilePreference("profile.default_content_setting_values.mixed_script", 1);

                // ChromeDriver service configuration with unique log path
                var uniqueLogPath = Path.Combine(Path.GetTempPath(), $"chromedriver-{Guid.NewGuid()}-{Environment.ProcessId}.log");
                var driverService = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName("/usr/bin/"), "chromedriver");
                driverService.LogPath = uniqueLogPath;
                driverService.EnableVerboseLogging = true;
                driverService.SuppressInitialDiagnosticInformation = false;
                Console.WriteLine($"DriverInitializer: ChromeDriver log path: {uniqueLogPath}");

                // Clean up old Chrome directories
                try
                {
                    CleanupOldChromeDirectories();
                }
                catch (Exception cleanupEx)
                {
                    Console.WriteLine($"DriverInitializer: Warning - Failed to cleanup old directories: {cleanupEx.Message}");
                }

                ChromeDriver driver;
                try
                {
                    driver = new ChromeDriver(driverService, options);
                    Console.WriteLine($"DriverInitializer: ChromeDriver created successfully with unique profile: {uniqueProfileDir}");
                    Console.WriteLine($"DriverInitializer: Downloads will go to: {downloadDir}");
                    
                    // Update the ChromeDownloadDirectory to point to our unique download directory
                    // This ensures the file monitoring works correctly
                    return driver;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DriverInitializer: Failed to create ChromeDriver: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DriverInitializer: Error in InitializeAKS: {ex.Message}");
                throw;
            }
        }

        private static void CleanupOldChromeDirectories()
        {
            // Clean up old Chrome profile directories in temp
            var tempPath = Path.GetTempPath();
            var chromeProfilePattern = "chrome-profile-";
            
            try
            {
                var tempDirs = Directory.GetDirectories(tempPath, chromeProfilePattern + "*");
                foreach (var dir in tempDirs)
                {
                    try
                    {
                        var creationTime = Directory.GetCreationTime(dir);
                        if (DateTime.Now - creationTime > TimeSpan.FromHours(1))
                        {
                            Console.WriteLine($"DriverInitializer: Cleaning up old Chrome profile directory: {dir}");
                            Directory.Delete(dir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DriverInitializer: Error cleaning up old Chrome profile directory {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DriverInitializer: Error accessing temp directory for cleanup: {ex.Message}");
            }

            // Also clean up any old chromedriver log files
            try
            {
                var logFiles = Directory.GetFiles(tempPath, "chromedriver-*.log");
                foreach (var logFile in logFiles)
                {
                    try
                    {
                        var creationTime = File.GetCreationTime(logFile);
                        if (DateTime.Now - creationTime > TimeSpan.FromHours(1))
                        {
                            Console.WriteLine($"DriverInitializer: Cleaning up old ChromeDriver log file: {logFile}");
                            File.Delete(logFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"DriverInitializer: Error cleaning up old ChromeDriver log file {logFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DriverInitializer: Error accessing temp directory for log cleanup: {ex.Message}");
            }
        }
    }
}