using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

internal partial class Program
{
    private static async Task Main(string[] args)
    {
        var settingFileName = "settings.ini";
        Log.Logger = new LoggerConfiguration()

#if !DEBUG
            .MinimumLevel.Information()
# else
            .MinimumLevel.Verbose()
#endif
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();
        Log.Information("Starting the application");

#if !DEBUG
        try
        {
#endif
        var builder = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddIniFile(settingFileName, optional: true);
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHttpClient();
                services.Configure<Settings>(hostContext.Configuration.GetSection("Settings"));
                services.AddHostedService<HelloWorldService>();
            });

        var host = builder.Build();
        Log.Information($"Loading {settingFileName}");

        var config = host.Services.GetRequiredService<IConfiguration>();
        var settings = config.GetSection("Settings").Get<Settings>();

        if (settings == null)
        {
            Log.Error($"Unable to retrieve settings from {settingFileName}");
            return;
        }

        if (string.IsNullOrEmpty(settings.ChatGptSecretKey))
        {
            Log.Error($"ChatGptSecretKey is not set in {settingFileName}");
        }
        if (string.IsNullOrEmpty(settings.SourceLanguage))
        {
            Log.Error($"SourceLanguage is not set in {settingFileName}");
        }
        if (string.IsNullOrEmpty(settings.TargetLanguage))
        {
            Log.Error($"TargetLanguage is not set in {settingFileName}");
        }
        if (string.IsNullOrEmpty(settings.PromptFileName))
        {
            Log.Error($"PromptFileName is not set in {settingFileName}");

            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), settings.PromptFileName)))
            {
                Log.Error($"{settings.PromptFileName} not found in the application directory");
            }
        }

        await host.RunAsync();
#if !DEBUG
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The application terminated abnormally");
        }
        finally
        {
            Log.Information("Terminating the application");
            Log.CloseAndFlush(); 
        }
#endif
    }
}