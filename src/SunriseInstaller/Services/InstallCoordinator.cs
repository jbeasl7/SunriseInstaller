namespace Sunrise.Installer.Services;

public sealed class InstallCoordinator : IDisposable
{
    private readonly InstallerLog log;
    private readonly GitHubClient gitHub;
    private readonly SunriseReleaseService sunrise;
    private readonly DepotDownloaderService depots;
    private readonly PayloadInstaller payloadInstaller;
    private readonly JsonStores stores;
    private readonly SunriseSettingsService sunriseSettings;
    private readonly LanguageCleanupService languageCleanup;

    public InstallCoordinator(InstallerLog log, AppOptions options)
    {
        this.log = log;
        gitHub = new GitHubClient(log);
        sunrise = new SunriseReleaseService(gitHub, log, options.TestPayloadPath);
        depots = new DepotDownloaderService(gitHub, log);
        payloadInstaller = new PayloadInstaller(log);
        sunriseSettings = new SunriseSettingsService(log);
        languageCleanup = new LanguageCleanupService(log);
        stores = new JsonStores(log);
    }

    public async Task InstallAsync(
        string installRoot,
        string steamUsername,
        LanguageSpec language,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateSteamUsername(steamUsername);
        string root = Preflight.PrepareInstallRoot(installRoot, AppConstants.FreshInstallFreeBytes);
        Preflight.EnsureGameIsClosed();
        log.Info("install_start", "Install started.", ("mode", "install"));

        PreparedPayload? payload = null;
        try
        {
            await PrepareGameFilesAsync(
                root,
                steamUsername.Trim(),
                language,
                validate: false,
                progress,
                cancellationToken);
            progress?.Report(new OperationProgress("Preparing Sunrise...", 92));
            payload = await sunrise.PrepareLatestAsync(root, ScaleProgress(progress, 92, 96), cancellationToken);
            progress?.Report(new OperationProgress("Installing Sunrise...", 97));
            await payloadInstaller.ApplyAsync(root, payload, preserveDepotDll: true, cancellationToken);
            await sunriseSettings.SetLanguageAsync(root, language.SteamLanguage, cancellationToken);
            await SaveStateAsync(root, payload, language, cancellationToken);
            progress?.Report(new OperationProgress("Install complete.", 100));
            log.Info("install_done", "Install complete.", ("mode", "install"));
        }
        finally
        {
            if (payload is not null)
            {
                SunriseReleaseService.Cleanup(payload);
            }
        }
    }

    public async Task RepairAsync(
        string installRoot,
        string steamUsername,
        LanguageSpec language,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateSteamUsername(steamUsername);
        string root = Preflight.PrepareInstallRoot(installRoot, AppConstants.RepairFreeBytes);
        Preflight.EnsureGameIsClosed();
        if (!File.Exists(Path.Combine(root, AppConstants.GameExecutableName)))
        {
            throw new InstallerException("No Destiny 2 install was found in this folder. Use Install first.");
        }

        log.Info("repair_start", "Repair started.", ("mode", "repair"));
        PreparedPayload? payload = null;
        try
        {
            await PrepareGameFilesAsync(
                root,
                steamUsername.Trim(),
                language,
                validate: true,
                progress,
                cancellationToken);
            progress?.Report(new OperationProgress("Deleting Sunrise config and cached data...", 95));
            payloadInstaller.DeleteSunriseData(root);
            progress?.Report(new OperationProgress("Preparing Sunrise...", 96));
            payload = await sunrise.PrepareLatestAsync(root, ScaleProgress(progress, 96, 98), cancellationToken);
            progress?.Report(new OperationProgress("Reinstalling Sunrise...", 99));
            await payloadInstaller.ApplyAsync(root, payload, preserveDepotDll: true, cancellationToken);
            await sunriseSettings.SetLanguageAsync(root, language.SteamLanguage, cancellationToken);
            await SaveStateAsync(root, payload, language, cancellationToken);
            progress?.Report(new OperationProgress("Repair complete.", 100));
            log.Info("repair_done", "Repair complete.", ("mode", "repair"));
        }
        finally
        {
            if (payload is not null)
            {
                SunriseReleaseService.Cleanup(payload);
            }
        }
    }

    public async Task<bool> UpdateAsync(
        string installRoot,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string root = Preflight.PrepareInstallRoot(installRoot, AppConstants.UpdateFreeBytes);
        Preflight.EnsureGameIsClosed();
        VerifyGameFiles(root);
        progress?.Report(new OperationProgress("Checking for updates...", 5));
        UpdateCheck check = await CheckForUpdateAsync(root, cancellationToken);
        if (check.Status == UpdateStatus.Current)
        {
            progress?.Report(new OperationProgress(check.Message, 100));
            return false;
        }

        InstallerState? installedState = await stores.LoadStateAsync(root, cancellationToken);
        LanguageSpec installedLanguage = AppConstants.ResolveLanguage(installedState?.SteamLanguage);

        log.Info("update_start", "Update started.", ("tag", check.LatestRelease.Tag));
        PreparedPayload? payload = null;
        try
        {
            payload = await sunrise.PrepareLatestAsync(root, ScaleProgress(progress, 10, 85), cancellationToken);
            progress?.Report(new OperationProgress("Installing the update...", 90));
            await payloadInstaller.ApplyAsync(root, payload, preserveDepotDll: false, cancellationToken);

            await SaveStateAsync(
                root,
                payload,
                installedLanguage,
                cancellationToken);

            progress?.Report(new OperationProgress($"Updated to {payload.Release.Tag}.", 100));
            log.Info("update_done", "Update complete.", ("tag", payload.Release.Tag));
            return true;
        }
        finally
        {
            if (payload is not null)
            {
                SunriseReleaseService.Cleanup(payload);
            }
        }
    }

    public async Task<UpdateCheck> CheckForUpdateAsync(
        string installRoot,
        CancellationToken cancellationToken)
    {
        ReleaseInfo latest = await sunrise.GetLatestAsync(cancellationToken);
        InstallerState? state = await stores.LoadStateAsync(installRoot, cancellationToken);
        if (state is null)
        {
            return new UpdateCheck(UpdateStatus.NotInstalled, "Sunrise is not installed by this installer.", latest);
        }

        if (!state.ReleaseTag.Equals(latest.Tag, StringComparison.OrdinalIgnoreCase) ||
            DigestsDiffer(state.ReleaseAssetDigest, latest.Asset.Digest))
        {
            return new UpdateCheck(
                UpdateStatus.ReleaseAvailable,
                $"Sunrise {latest.Tag} is available. Installed: {state.ReleaseTag}.",
                latest);
        }

        string dllPath = Path.Combine(installRoot, AppConstants.ModRelativePath);
        if (!File.Exists(dllPath))
        {
            return new UpdateCheck(UpdateStatus.LocalFileChanged, "The Sunrise DLL is missing.", latest);
        }

        string localHash = await FileIntegrity.Sha256Async(dllPath, cancellationToken);
        if (!localHash.Equals(state.InstalledDllSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheck(UpdateStatus.LocalFileChanged, "The installed Sunrise DLL has changed.", latest);
        }

        return new UpdateCheck(UpdateStatus.Current, $"Sunrise {state.ReleaseTag} is current.", latest);
    }

    public Task<InstallerState?> LoadStateAsync(string installRoot, CancellationToken cancellationToken) =>
        stores.LoadStateAsync(installRoot, cancellationToken);

    public static Task<UserPreferences> LoadPreferencesAsync(CancellationToken cancellationToken) =>
        JsonStores.LoadPreferencesAsync(cancellationToken);

    public static Task SavePreferencesAsync(UserPreferences preferences, CancellationToken cancellationToken) =>
        JsonStores.SavePreferencesAsync(preferences, cancellationToken);

    public void Dispose() => gitHub.Dispose();

    private async Task PrepareGameFilesAsync(
        string installRoot,
        string steamUsername,
        LanguageSpec language,
        bool validate,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new OperationProgress("Preparing DepotDownloader...", 2));

        string downloader = await depots.EnsureAvailableAsync(
            ScaleProgress(progress, 2, 8),
            cancellationToken);

        using ConsoleWindow console = new("Sunrise Installer - Steam sign-in");

        Console.WriteLine("Sunrise Installer");
        Console.WriteLine("DepotDownloader needs a Steam account that owns the game.");
        Console.WriteLine("Credentials are handled by DepotDownloader in this window.");
        Console.WriteLine();

        InstallerState? existingState = await stores.LoadStateAsync(installRoot, cancellationToken);

        if (existingState is not null)
        {
            LanguageSpec previousLanguage =
                AppConstants.ResolveLanguage(
                    existingState.SteamLanguage);

            bool languageChanged =
                !previousLanguage.SteamLanguage.Equals(
                    language.SteamLanguage,
                    StringComparison.OrdinalIgnoreCase);

            if (languageChanged)
            {
                progress?.Report(
                    new OperationProgress($"Removing {previousLanguage.DisplayName} language files...", 8));

                ulong previousManifestId = existingState.Manifests.TryGetValue(
                    previousLanguage.Depot.DepotId,
                    out ulong installedManifestId)
                    ? installedManifestId
                    : previousLanguage.Depot.ManifestId;

                DepotSpec previousDepot = new(previousLanguage.Depot.DepotId, previousManifestId);

                Console.WriteLine();
                Console.WriteLine(
                    $"Switching language from " +
                    $"{previousLanguage.DisplayName} to " +
                    $"{language.DisplayName}.");

                Console.WriteLine(
                    $"Reading previous language depot " +
                    $"{previousDepot.DepotId} manifest...");

                IReadOnlyList<string> previousFiles = await depots.GetManifestFilesAsync(
                        downloader,
                        steamUsername,
                        previousDepot,
                        cancellationToken);

                int removed = languageCleanup.RemoveDepotFiles(installRoot, previousFiles);

                Console.WriteLine($"Removed {removed} file(s) from the previous language depot.");

                Console.WriteLine();
            }
        }

        await depots.DownloadDepotsAsync(
            downloader,
            installRoot,
            steamUsername,
            language,
            validate,
            MessageProgress(progress),
            cancellationToken);

        VerifyGameFiles(installRoot);

        Console.WriteLine();
        Console.WriteLine(
            "Steam files are ready. Return to the Sunrise Installer.");
    }

    private static async Task SaveStateAsync(
        string root,
        PreparedPayload payload,
        LanguageSpec language,
        CancellationToken cancellationToken)
    {
        DepotSpec[] depots = AppConstants.DepotsFor(language);
        InstallerState state = new()
        {
            ReleaseTag = payload.Release.Tag,
            ReleaseAsset = payload.Release.Asset.Name,
            ReleaseAssetDigest = payload.Release.Asset.Digest,
            InstalledDllSha256 = payload.DllSha256,
            InstalledAtUtc = DateTimeOffset.UtcNow,
            SteamLanguage = language.SteamLanguage,
            Manifests = depots.ToDictionary(depot => depot.DepotId, depot => depot.ManifestId),
        };
        await JsonStores.SaveStateAsync(root, state, cancellationToken);
    }

    private static void VerifyGameFiles(string root)
    {
        if (!File.Exists(Path.Combine(root, AppConstants.GameExecutableName)))
        {
            throw new InstallerException("DepotDownloader finished, but destiny2.exe is missing.");
        }

        if (!Directory.Exists(Path.Combine(root, "bin", "x64")))
        {
            throw new InstallerException("DepotDownloader finished, but bin\\x64 is missing.");
        }
    }

    private static void ValidateSteamUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InstallerException("Enter the Steam account name that owns Destiny 2.");
        }

        if (username.Any(char.IsControl))
        {
            throw new InstallerException("The Steam account name contains an invalid character.");
        }
    }

    private static bool DigestsDiffer(string? installed, string? latest) =>
        !string.IsNullOrWhiteSpace(installed) &&
        !string.IsNullOrWhiteSpace(latest) &&
        !installed.Equals(latest, StringComparison.OrdinalIgnoreCase);

    private static Progress<int>? ScaleProgress(
        IProgress<OperationProgress>? progress,
        int start,
        int end)
    {
        if (progress is null)
        {
            return null;
        }

        return new Progress<int>(value =>
            progress.Report(new OperationProgress("Downloading...", start + ((end - start) * value / 100))));
    }

    private static Progress<string>? MessageProgress(IProgress<OperationProgress>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new Progress<string>(message => progress.Report(new OperationProgress(message)));
    }
}
