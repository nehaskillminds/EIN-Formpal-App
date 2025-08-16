using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EinAutomation.Api.Infrastructure;
using EinAutomation.Api.Services;
using EinAutomation.Api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using EinAutomation.Api.Services.Interfaces; 

namespace EinAutomation.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Configure Selenium environment variables for containerized environments
            Environment.SetEnvironmentVariable("SELENIUM_MANAGER", "false");
            Environment.SetEnvironmentVariable("WDM_LOG_LEVEL", "0");
            Environment.SetEnvironmentVariable("SELENIUM_MANAGER_DRIVER_PATH", "/usr/bin/chromedriver");
            Environment.SetEnvironmentVariable("SELENIUM_MANAGER_BROWSER_PATH", "/usr/bin/chromium");
            
            // AKS-specific configurations
            var isAKS = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production" || 
                       Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Staging" ||
                       !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
            
            // Enable Swagger based on environment
            if (isAKS)
            {
                // In AKS, only enable Swagger if explicitly requested
                var enableSwagger = Environment.GetEnvironmentVariable("ENABLE_SWAGGER");
                if (string.IsNullOrEmpty(enableSwagger))
                {
                    Environment.SetEnvironmentVariable("ENABLE_SWAGGER", "false");
                }
            }
            else
            {
                // Enable Swagger for local development
                Environment.SetEnvironmentVariable("ENABLE_SWAGGER", "true");
            }
            
            var builder = WebApplication.CreateBuilder(args);

            // Configure URLs for AKS deployment
            if (isAKS)
            {
                // In AKS, use the port provided by Kubernetes (usually 80 or 8080)
                var port = Environment.GetEnvironmentVariable("PORT") ?? "80";
                builder.WebHost.UseUrls($"http://+:{port}");
                
                // Configure Kestrel for AKS
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(int.Parse(port));
                });
            }
            else
            {
                // In development, use the port from launchSettings.json or default to 5190
                var port = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5190;https://localhost:7126";
                builder.WebHost.UseUrls(port);
            }

            // Add services to the container
            builder.Services.AddControllers();
            
            // Add health checks for AKS
            if (isAKS)
            {
                builder.Services.AddHealthChecks();
            }

            // Configure CORS for AKS
            builder.Services.AddCors(options =>
            {
                if (isAKS)
                {
                    // More restrictive CORS for production AKS
                    options.AddPolicy("AKSPolicy", corsBuilder =>
                    {
                        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?.Split(',') 
                                           ?? new[] { "*" }; // Fallback to allow all if not configured
                        
                        if (allowedOrigins.Contains("*"))
                        {
                            corsBuilder.AllowAnyOrigin()
                                      .AllowAnyMethod()
                                      .AllowAnyHeader();
                        }
                        else
                        {
                            corsBuilder.WithOrigins(allowedOrigins)
                                      .AllowAnyMethod()
                                      .AllowAnyHeader()
                                      .AllowCredentials();
                        }
                    });
                }
                else
                {
                    // Permissive CORS for local development
                    options.AddPolicy("AllowAll", corsBuilder =>
                    {
                        corsBuilder.AllowAnyOrigin()
                                  .AllowAnyMethod()
                                  .AllowAnyHeader();
                    });
                }
            });

            // Configure authentication (Azure AD)
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
                options.Audience = $"api://{builder.Configuration["AzureAd:ClientId"]}";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"https://sts.windows.net/{builder.Configuration["AzureAd:TenantId"]}/",
                    ValidateAudience = true,
                    ValidAudience = $"api://{builder.Configuration["AzureAd:ClientId"]}"
                };
            });

            // This binds the "Azure:Blob" section from appsettings.json to AzureBlobStorageOptions
            builder.Services.Configure<AzureBlobStorageOptions>(builder.Configuration.GetSection("Azure:Blob"));

            // Register services
            builder.Services.AddSingleton<IEinFormFiller, IRSEinFormFiller>();
            builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
            builder.Services.AddSingleton<ISalesforceClient, SalesforceClient>();
            builder.Services.AddSingleton<IAutomationOrchestrator, AutomationOrchestrator>();
            builder.Services.AddSingleton<IFormDataMapper, FormDataMapper>();
            builder.Services.AddHttpClient<ISalesforceClient, SalesforceClient>();
            builder.Services.AddSingleton<IErrorMessageExtractionService, ErrorMessageExtractionService>();


            // Add configuration for Key Vault - Enhanced for AKS
            var environment = builder.Environment.EnvironmentName;
            var enableKeyVault = builder.Configuration.GetValue<bool>("EnableKeyVault", false);
            
            // In AKS, always try to use Key Vault for secrets management
            if (isAKS || environment == "Production" || enableKeyVault)
            {
                try
                {
                    builder.Configuration.AddKeyVaultSecrets();
                    Console.WriteLine("Key Vault configuration loaded successfully for AKS deployment.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load Key Vault configuration in AKS: {ex.Message}");
                    Console.WriteLine("Application will continue with environment variables and configuration.");
                    
                    // In AKS, log configuration source for debugging
                    if (isAKS)
                    {
                        Console.WriteLine("AKS Environment Variables:");
                        Console.WriteLine($"  KUBERNETES_SERVICE_HOST: {Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST")}");
                        Console.WriteLine($"  ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}");
                        Console.WriteLine($"  PORT: {Environment.GetEnvironmentVariable("PORT")}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Key Vault configuration skipped for local development.");
                
                // Log local configuration values for debugging
                var localClientId = builder.Configuration["Salesforce:ClientId"];
                var localUsername = builder.Configuration["Salesforce:Username"];
                Console.WriteLine($"Using local Salesforce configuration:");
                Console.WriteLine($"  Client ID: {localClientId ?? "NULL"}");
                Console.WriteLine($"  Username: {localUsername ?? "NULL"}");
            }

            // Add logging - Enhanced for AKS
            builder.Services.AddLogging(logging =>
            {
                logging.AddConsole();
                
                if (isAKS)
                {
                    // In AKS, use structured logging and configure log levels from environment
                    var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
                    var minLogLevel = logLevel switch
                    {
                        "Debug" => LogLevel.Debug,
                        "Information" => LogLevel.Information,
                        "Warning" => LogLevel.Warning,
                        "Error" => LogLevel.Error,
                        "Critical" => LogLevel.Critical,
                        _ => LogLevel.Information
                    };
                    
                    logging.SetMinimumLevel(minLogLevel);
                    
                    // Add Application Insights if configured
                    var appInsightsKey = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
                    if (!string.IsNullOrEmpty(appInsightsKey))
                    {
                        builder.Services.AddApplicationInsightsTelemetry();
                    }
                }
                else
                {
                    // Local development logging
                    logging.SetMinimumLevel(LogLevel.Information);
                }
            });

            // Add Swagger for API documentation
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "IRS EIN API", Version = "v1", Description = "Automated IRS EIN form processing with Azure AD auth" });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline
            // Health checks for AKS
            if (isAKS)
            {
                app.MapHealthChecks("/health");
                app.MapHealthChecks("/ready");
            }
            
            // Enable Swagger based on environment
            if (app.Environment.IsDevelopment() || Environment.GetEnvironmentVariable("ENABLE_SWAGGER") == "true")
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IRS EIN API v1");
                    c.RoutePrefix = "swagger";
                });
            }

            // HTTPS redirection - disable in AKS if using ingress controller
            if (!isAKS || Environment.GetEnvironmentVariable("ENABLE_HTTPS_REDIRECT") == "true")
            {
                app.UseHttpsRedirection();
            }
            
            // Use appropriate CORS policy
            if (isAKS)
            {
                app.UseCors("AKSPolicy");
            }
            else
            {
                app.UseCors("AllowAll");
            }
            
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();

            app.Run();
        }
    }
}