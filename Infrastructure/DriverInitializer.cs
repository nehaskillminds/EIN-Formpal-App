
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
            Directory.CreateDirectory(recordDownloadDirectory);
   
            var options = new ChromeOptions();
            // Set Chrome arguments
            options.AddArgument("--disable-gpu");
            options.AddArgument("--enable-unsafe-swiftshader");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--window-size=1920,1080");
            options.AddArgument("--start-maximized");

            // Set Chrome preferences
            var prefs = new Dictionary<string, object>
            {
                ["profile.default_content_setting_values.popups"] = 2,
                ["profile.default_content_setting_values.notifications"] = 2,
                ["profile.default_content_setting_values.geolocation"] = 2,
                ["credentials_enable_service"] = false,
                ["profile.password_manager_enabled"] = false,
                ["autofill.profile_enabled"] = false,
                ["autofill.credit_card_enabled"] = false,
                ["password_manager_enabled"] = false,
                ["profile.password_dismissed_save_prompt"] = true,
                ["plugins.always_open_pdf_externally"] = true,
                ["download.prompt_for_download"] = false,
                ["download.directory_upgrade"] = true,
                ["safebrowsing.enabled"] = true,
                ["download.default_directory"] = recordDownloadDirectory,
                ["savefile.default_directory"] = recordDownloadDirectory,
                ["profile.default_content_setting_values.automatic_downloads"] = 1
            };
            options.AddUserProfilePreference("prefs", prefs);

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

            return driver;
        }

        public static IWebDriver InitializeAKS(string chromeDownloadDirectory, string recordId)
        {
            // Create a unique download directory for the record
            var recordDownloadDirectory = Path.Combine(chromeDownloadDirectory, recordId);
            Directory.CreateDirectory(recordDownloadDirectory);

            // Ensure HOME variable and Chrome data directories
            var chromeHome = "/tmp/chrome-home";
            Environment.SetEnvironmentVariable("HOME", chromeHome);
            Directory.CreateDirectory(chromeHome);

            var chromeUserData = $"/tmp/chrome-{Guid.NewGuid()}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            Directory.CreateDirectory(chromeUserData);

            Directory.CreateDirectory(chromeDownloadDirectory);

            var options = new ChromeOptions
            {
                BinaryLocation = "/usr/bin/chromium",
                AcceptInsecureCertificates = true
            };

            // Chromium runtime arguments
            options.AddArgument($"--user-data-dir={chromeUserData}");
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
            options.AddArgument("--remote-debugging-port=9222");
            options.AddArgument("--remote-debugging-address=0.0.0.0");
            options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            
            // Route downloads into the dedicated HOME/Downloads directory
            options.AddUserProfilePreference("savefile.default_directory", recordDownloadDirectory);
            options.AddUserProfilePreference("download.default_directory", recordDownloadDirectory);
            options.AddUserProfilePreference("download.prompt_for_download", false);
            options.AddUserProfilePreference("download.directory_upgrade", true);
            options.AddUserProfilePreference("safebrowsing.enabled", true);
            options.AddUserProfilePreference("plugins.always_open_pdf_externally", true);
            options.AddUserProfilePreference("profile.default_content_setting_values.automatic_downloads", 1);

            // ChromeDriver service configuration
            var driverService = ChromeDriverService.CreateDefaultService(Path.GetDirectoryName("/usr/bin/"), "chromedriver");
            driverService.LogPath = "/tmp/chromedriver.log"; // Force a known path
            driverService.EnableVerboseLogging = true;
            driverService.SuppressInitialDiagnosticInformation = false;

            var driver = new ChromeDriver(driverService, options);

            driver.ExecuteScript(@"
                    window.alert = function() { return true; };
                    window.confirm = function() { return true; };
                    window.prompt = function() { return null; };
                    window.open = function() { return null; };
                ");

            return driver;
        }
    }
}