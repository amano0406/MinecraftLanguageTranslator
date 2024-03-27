using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Serilog;
using System.IO.Compression;

public class HelloWorldService : IHostedService
{
    private readonly ILogger<HelloWorldService> _logger;
    private readonly Settings _settings;
    private readonly IHttpClientFactory _httpClientFactory;

    public HelloWorldService(
        ILogger<HelloWorldService> logger,
        IOptions<Settings> settings,
        IHttpClientFactory httpClientFactory)
    {
        this._logger = logger;
        this._settings = settings.Value;
        this._httpClientFactory = httpClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var httpClient = this._httpClientFactory.CreateClient();
        var translationBatchSize = 25; // TODO: It might be better to make this value variable depending on the number of characters to be translated. If the text is too long, it may cause an error on the ChatGPT side because the translation cannot be completed.
        var minecraftDirectoryPath = Directory.GetCurrentDirectory();
        var modDirectoryName = "mods";
        var workDirectoryPath = Path.Combine(minecraftDirectoryPath, "MinecraftLanguageTranslator");
        var tempDirectoryPath = Path.Combine(workDirectoryPath, "temp");
        var logDirectoryPath = Path.Combine(workDirectoryPath, "logs");
        var chatGptApiClient = new ChatGptApiClient(this._settings.ChatGptSecretKey!);

        var prompt = File.ReadAllText($"prompts/{this._settings.PromptFileName}");
        prompt = prompt.Trim(new char[] { '\n', '\r', });

        if (Directory.Exists(tempDirectoryPath))
        {
            Directory.Delete(tempDirectoryPath, true);
        }

        var modFilePaths = Directory.GetFiles(Path.Combine(minecraftDirectoryPath, modDirectoryName), "*.jar*");

        var backupDirectoryPath = Path.Combine(workDirectoryPath, "ModBackup", DateTime.Now.ToString("yyyyMMddHHmmss"), modDirectoryName);
        Directory.CreateDirectory(backupDirectoryPath);
        foreach (var modFilePath in modFilePaths)
        {
            var backupFilePath = Path.Combine(backupDirectoryPath, Path.GetFileName(modFilePath));
            File.Copy(modFilePath, backupFilePath, true);
        }

        foreach (var modFilePath in modFilePaths)
        {
            var modFileName = Path.GetFileNameWithoutExtension(modFilePath);

            Log.Information($"Current processing target: {Array.IndexOf(modFilePaths, modFilePath) + 1} / {modFilePaths.Length} {modFileName}");

            var tempModDirectoryPath = Path.Combine(tempDirectoryPath, modFileName);

            UnzipFile(modFilePath, tempModDirectoryPath);

            var targetLangFilePath = Directory.GetFiles(tempModDirectoryPath, $"{this._settings.TargetLanguage!}.json", SearchOption.AllDirectories).FirstOrDefault();
            if (targetLangFilePath is not null)
            {
                Log.Information("The translation file already exists.");
                continue;
            }

            string? modName = null;
            var metaInfFilePath = Directory.GetFiles(tempModDirectoryPath, "*.MF", SearchOption.AllDirectories).FirstOrDefault();
            if (metaInfFilePath is null)
            {
                Log.Information("The meta information file was not found.");
                continue;
            }
            var specificationTitleLine = File.ReadLines(metaInfFilePath).FirstOrDefault(line => line.StartsWith("Specification-Title:"));
            if (specificationTitleLine is not null)
            {
                modName = specificationTitleLine.Split(':')[1].Trim();
            }

            var sourceLangFilePath = Directory.GetFiles(tempModDirectoryPath, $"{this._settings.SourceLanguage!}.json", SearchOption.AllDirectories).FirstOrDefault();
            if (sourceLangFilePath is null)
            {
                Log.Information("The original language file was not found.");
                continue;
            }

            prompt = prompt.Replace("{MOD_NAME}", modName is null ? modFileName : modName);
            prompt = prompt.Replace("{SOURCE_LANGUAGE}", this._settings.SourceLanguage!);
            prompt = prompt.Replace("{TARGET_LANGUAGE}", this._settings.TargetLanguage!);

            var targetLangJson = new JObject();
            var sourceLangText = File.ReadAllText(sourceLangFilePath);
            var sourceLangJson = JObject.Parse(sourceLangText);
            var keys = sourceLangJson.Properties().Select(p => p.Name).ToList();
            for (var i = 0; i < keys.Count; i += translationBatchSize)
            {
                var currentKeys = keys.Skip(i).Take(translationBatchSize);
                var sourceLangJsonBlock = new JObject();
                foreach (var key in currentKeys)
                {
                    sourceLangJsonBlock.Add(key, sourceLangJson[key]);
                }

                // TODO: Creating a cache locally seems to save money when retrying
                // If you save {MODEL_NAME}.{JSON_KEY}:{VALUE}, you can translate only the missing parts
                var targetLangJsonBlock = TranslateJson(sourceLangJsonBlock, prompt, chatGptApiClient);

                if (targetLangJsonBlock is null)
                {
                    Log.Error("The translation failed.");
                    throw new Exception("The translation failed.");
                }

                targetLangJson.Merge(targetLangJsonBlock);

                foreach (var key in currentKeys)
                {
                    if (isContainSpecialCharacters(sourceLangJson[key]!.ToString()))
                    {
                        targetLangJson[key] = targetLangJsonBlock[key];
                    }
                }
            }

            targetLangFilePath = Path.Combine(Path.GetDirectoryName(sourceLangFilePath)!, $"{this._settings.TargetLanguage!}.json");
            File.WriteAllText(targetLangFilePath, targetLangJson.ToString());

            if (File.Exists(modFilePath))
            {
                File.Delete(modFilePath);
            }

            ZipFile.CreateFromDirectory(tempModDirectoryPath, modFilePath, CompressionLevel.Optimal, false);
            Directory.Delete(tempModDirectoryPath, true);
        }

        Directory.Delete(tempDirectoryPath, true);

        Log.Information("The application has ended.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }


    static void UnzipFile(string sourceZipPath, string destinationPath)
    {
        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, true);
        }
        ZipFile.ExtractToDirectory(sourceZipPath, destinationPath);
    }

    static JObject? TranslateJson(JObject sourceJson, string prompt, ChatGptApiClient chatGptApiClient)
    {
        JObject? targetJson = null;
        for (var i = 0; i < 3; i++)
        {
            try
            {
                targetJson = chatGptApiClient.TranslateAsync(sourceJson.ToString(), prompt).Result;
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred while translating the JSON. Retrying...");
            }
        }
        return targetJson;
    }

    static public bool isContainSpecialCharacters(string input)
    {
        var specialCharacters = "\\§\n";

        return input.Any(specialCharacters.Contains);
    }
}