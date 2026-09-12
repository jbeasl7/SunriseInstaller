using System.Text.Json;

namespace Sunrise.Installer.Services;

public sealed class JsonStores(InstallerLog log)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public async Task<InstallerState?> LoadStateAsync(
        string installRoot,
        CancellationToken cancellationToken)
    {
        string path = StatePath(installRoot);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<InstallerState>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            log.Error("state_invalid", "The local install state is invalid.", ("err", exception.GetType().Name));
            return null;
        }
    }

    public static Task SaveStateAsync(
        string installRoot,
        InstallerState state,
        CancellationToken cancellationToken) =>
        SaveAtomicAsync(StatePath(installRoot), state, cancellationToken);

    public static async Task<UserPreferences> LoadPreferencesAsync(CancellationToken cancellationToken)
    {
        string path = PreferencesPath();
        if (!File.Exists(path))
        {
            return new UserPreferences();
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<UserPreferences>(stream, JsonOptions, cancellationToken)
                ?? new UserPreferences();
        }
        catch (JsonException)
        {
            return new UserPreferences();
        }
    }

    public static Task SavePreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken) =>
        SaveAtomicAsync(PreferencesPath(), preferences, cancellationToken);

    private static string StatePath(string installRoot) =>
        Path.Combine(installRoot, ".sunrise", "install-state.json");

    private static string PreferencesPath() => Path.Combine(AppConstants.AppDataRoot, "preferences.json");

    internal static async Task SaveAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, true);
        }
        finally
        {
            FileCleanup.TryDeleteFile(temporaryPath);
        }
    }
}
