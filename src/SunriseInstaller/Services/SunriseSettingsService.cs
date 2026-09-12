using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sunrise.Installer.Services;

public sealed class SunriseSettingsService(InstallerLog log)
{
    /** Settings must use the defaults and schema embedded in the DLL being installed. */
    public async Task<JsonObject> PrepareAsync(
        string installRoot,
        string dllPath,
        string? steamLanguage,
        bool reset,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonObject defaults = SunriseDefaults.Read(dllPath);
        JsonObject settings = defaults;
        string path = SettingsPath(installRoot);

        try
        {
            if (!reset && File.Exists(path))
            {
                await using FileStream input = File.OpenRead(path);
                JsonObject existing = await JsonNode.ParseAsync(input, cancellationToken: cancellationToken)
                    as JsonObject ?? throw new InstallerException("Sunrise settings.json is not a JSON object.");
                steamLanguage ??= existing["steam"]?["language"]?.GetValue<string>();

                if ((existing["version"]?.GetValue<uint>() ?? 0) >= defaults["version"]!.GetValue<uint>())
                {
                    AddMissingDefaults(existing, defaults);
                    settings = existing;
                }
                else
                {
                    log.Info("settings_reset", "Sunrise settings were reset for the new settings version.");
                }
            }

            JsonObject steam = settings["steam"] as JsonObject
                ?? throw new InstallerException("Sunrise Steam settings are not a JSON object.");
            steam["language"] = AppConstants.ResolveLanguage(steamLanguage).SteamLanguage;
            return settings;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InstallerException("Sunrise settings.json could not be read.", exception);
        }
    }

    public static Task ApplyAsync(string installRoot, JsonObject settings, CancellationToken cancellationToken) =>
        JsonStores.SaveAtomicAsync(SettingsPath(installRoot), settings, cancellationToken);

    private static string SettingsPath(string installRoot) =>
        Path.Combine(installRoot, "bin", "x64", "Sunrise", "settings.json");

    /** Missing values take bundled defaults; existing values, including null and arrays, stay intact. */
    private static void AddMissingDefaults(JsonObject settings, JsonObject defaults)
    {
        foreach ((string key, JsonNode? value) in defaults)
        {
            if (!settings.TryGetPropertyValue(key, out JsonNode? existing))
            {
                settings[key] = value?.DeepClone();
            }
            else if (existing is JsonObject existingObject && value is JsonObject defaultObject)
            {
                AddMissingDefaults(existingObject, defaultObject);
            }
        }
    }
}
