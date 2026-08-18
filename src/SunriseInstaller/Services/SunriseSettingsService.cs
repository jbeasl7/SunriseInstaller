using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sunrise.Installer.Services;

public sealed class SunriseSettingsService(InstallerLog log){
    private static readonly JsonSerializerOptions JsonOptions = new(){
        WriteIndented = true,
    };
    public async Task SetLanguageAsync(
        string installRoot,
        string steamLanguage,
        CancellationToken cancellationToken){
        string directory = Path.Combine(
            installRoot,
            "bin",
            "x64",
            "Sunrise");

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "settings.json");

        JsonObject root;

        if (File.Exists(path)){
            await using FileStream input = File.OpenRead(path);

            JsonNode? existing =
                await JsonNode.ParseAsync(
                    input,
                    cancellationToken: cancellationToken);

            root = existing as JsonObject
                ?? throw new InstallerException(
                    "Sunrise settings.json is not a JSON object.");
        }
        else {
            root = new JsonObject();
        }

        JsonObject steam;

        if (root["steam"] is JsonObject existingSteam){
            steam = existingSteam;
        }
        else {
            steam = new JsonObject();
            root["steam"] = steam;
        }

        steam["language"] = steamLanguage;

        string temporaryPath =
            path + $".{Guid.NewGuid():N}.tmp";

        try {
            await using FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            await JsonSerializer.SerializeAsync(
                output,
                root,
                JsonOptions,
                cancellationToken);

            await output.FlushAsync(cancellationToken);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
        catch (JsonException exception){
            throw new InstallerException(
                "Sunrise settings.json could not be updated.",
                exception);
        }
        finally{
            FileCleanup.TryDeleteFile(temporaryPath);
        }

        log.Info(
            "settings_language",
            "Configured Sunrise game language.",
            ("language", steamLanguage));
    }
}