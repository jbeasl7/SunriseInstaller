namespace Sunrise.Installer;

public sealed record DepotSpec(uint DepotId, ulong ManifestId);

public sealed record LanguageSpec(
    string DisplayName,
    string SteamLanguage,
    DepotSpec Depot);

public sealed record ReleaseAsset(
    string Name,
    Uri DownloadUrl,
    long Size,
    string? Digest);

public sealed record ReleaseInfo(
    string Tag,
    ReleaseAsset Asset);

public sealed record PreparedPayload(
    ReleaseInfo Release,
    string StagingDirectory,
    string DllPath,
    string DllSha256);

public sealed class InstallerState
{
    public int SchemaVersion { get; set; } = 2;
    public uint AppId { get; set; } = AppConstants.SteamAppId;
    public string ReleaseTag { get; set; } = string.Empty;
    public string ReleaseAsset { get; set; } = string.Empty;
    public string? ReleaseAssetDigest { get; set; }
    public string InstalledDllSha256 { get; set; } = string.Empty;
    public DateTimeOffset InstalledAtUtc { get; set; }
    public string SteamLanguage { get; set; } = "english";
    public Dictionary<uint, ulong> Manifests { get; set; } = [];
    public string[] PendingLanguageFiles { get; set; } = [];
}

public sealed class UserPreferences
{
    public string InstallDirectory { get; set; } = string.Empty;
    public string SteamUsername { get; set; } = string.Empty;
    public string SteamLanguage { get; set; } = "english";
}

public enum UpdateStatus
{
    Current,
    ReleaseAvailable,
    LocalFileChanged,
    NotInstalled,
}

public sealed record UpdateCheck(UpdateStatus Status, string Message, ReleaseInfo LatestRelease);

public sealed class InstallerException(string message, Exception? innerException = null)
    : Exception(message, innerException);
