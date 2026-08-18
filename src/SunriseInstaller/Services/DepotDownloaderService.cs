using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Sunrise.Installer.Services;

public sealed class DepotDownloaderService(GitHubClient gitHub, InstallerLog log)
{
    private readonly string toolRoot = Path.Combine(AppConstants.AppDataRoot, "tools", "DepotDownloader");

    public async Task<string> EnsureAvailableAsync(
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ReleaseInfo release = await gitHub.GetLatestReleaseAsync(
            AppConstants.DepotDownloaderOwner,
            AppConstants.DepotDownloaderRepository,
            GitHubClient.SelectDepotDownloaderAsset,
            cancellationToken);
        string versionName = SanitizeFileName(release.Tag);
        string versionDirectory = Path.Combine(toolRoot, "versions", versionName);
        string executable = Path.Combine(versionDirectory, "DepotDownloader.exe");
        if (File.Exists(executable))
        {
            return executable;
        }

        Directory.CreateDirectory(Path.Combine(toolRoot, "versions"));
        string temporaryDirectory = Path.Combine(toolRoot, "versions", $".{versionName}-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(temporaryDirectory, "DepotDownloader.zip");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            log.Info("tool_download", "Downloading DepotDownloader.", ("tag", release.Tag));
            await gitHub.DownloadAsync(release.Asset, archivePath, progress, cancellationToken);
            string extracted = Path.Combine(temporaryDirectory, "extracted");
            await SafeZip.ExtractAllAsync(archivePath, extracted, cancellationToken);
            string extractedExecutable = Directory
                .EnumerateFiles(extracted, "DepotDownloader.exe", SearchOption.AllDirectories)
                .SingleOrDefault()
                ?? throw new InstallerException("The DepotDownloader ZIP does not contain DepotDownloader.exe.");

            string extractedRoot = Path.GetDirectoryName(extractedExecutable)!;
            try
            {
                Directory.Move(extractedRoot, versionDirectory);
            }
            catch (IOException) when (File.Exists(executable))
            {
            }

            if (!File.Exists(executable))
            {
                throw new InstallerException("DepotDownloader could not be installed.");
            }

            log.Info("tool_ready", "DepotDownloader is ready.", ("tag", release.Tag));
            return executable;
        }
        finally
        {
            FileCleanup.TryDeleteDirectory(temporaryDirectory);
        }
    }

    public async Task DownloadDepotsAsync(
        string executable,
        string installRoot,
        string steamUsername,
        LanguageSpec language,
        bool validate,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(toolRoot);


        DepotSpec[] depots = AppConstants.DepotsFor(language);
        for (int index = 0; index < depots.Length; index++)
        {
            DepotSpec depot = depots[index];
            status?.Report($"{(validate ? "Validating" : "Downloading")} depot {depot.DepotId}...");
            int exitCode = await RunAsync(
                executable,
                installRoot,
                steamUsername,
                depot,
                validate,
                cancellationToken);
            if (exitCode != 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"DepotDownloader stopped with exit code {exitCode}.");
                throw new InstallerException(DepotFailureMessage(exitCode));
            }
        }

    }

    public async Task<IReadOnlyList<string>> GetManifestFilesAsync(
    string executable,
    string steamUsername,
    DepotSpec depot,
    CancellationToken cancellationToken)
{
    string temporaryDirectory = Path.Combine(
        AppConstants.AppDataRoot,
        "temp",
        $"manifest-{depot.DepotId}-{Guid.NewGuid():N}");

    Directory.CreateDirectory(temporaryDirectory);

    try {
        int exitCode = await RunAsync(
            executable,
            temporaryDirectory,
            steamUsername,
            depot,
            validate: false,
            cancellationToken,
            manifestOnly: true);

        if (exitCode != 0){
            throw new InstallerException(
                $"DepotDownloader could not read manifest {depot.ManifestId} " +
                $"for depot {depot.DepotId}.");
        }

        string manifestPath = Path.Combine(
            temporaryDirectory,
            $"manifest_{depot.DepotId}_{depot.ManifestId}.txt");

        if (!File.Exists(manifestPath)){
            throw new InstallerException("DepotDownloader did not produce the expected manifest file.");
        }

        string[] lines = await File.ReadAllLinesAsync(
            manifestPath,
            cancellationToken);

        List<string> files = [];

        foreach (string line in lines){
            string[] parts = line.Split(
                ' ',
                5,
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 5){
                continue;
            }

            if (!ulong.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _)){
                continue;
            }

            if (!int.TryParse(
                    parts[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _)){
                continue;
            }

            if (parts[2].Length != 40){
                continue;
            }

            if (!int.TryParse(
                    parts[3],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out _)){
                continue;
            }

            files.Add(parts[4]);
        }

        if (files.Count == 0){
            throw new InstallerException("The previous language depot manifest contained no readable files.");
        }

        log.Info(
            "manifest_files_loaded",
            "Loaded language depot manifest file list.",
            ("depot", depot.DepotId),
            ("manifest", depot.ManifestId),
            ("count", files.Count));

        return files;
    }
    finally {
        FileCleanup.TryDeleteDirectory(temporaryDirectory);
    }
}

    private async Task<int> RunAsync(
        string executable,
        string installRoot,
        string steamUsername,
        DepotSpec depot,
        bool validate,
        CancellationToken cancellationToken,
        bool manifestOnly = false)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            WorkingDirectory = toolRoot,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        AddArgument(startInfo, "-app", AppConstants.SteamAppId.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-depot", depot.DepotId.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-manifest", depot.ManifestId.ToString(CultureInfo.InvariantCulture));
        AddArgument(startInfo, "-dir", installRoot);
        AddArgument(startInfo, "-username", steamUsername);
        startInfo.ArgumentList.Add("-remember-password");
        AddArgument(startInfo, "-os", "windows");
        AddArgument(startInfo, "-osarch", "64");
        if (manifestOnly)
        {
            startInfo.ArgumentList.Add("-manifest-only");
        }
        if (validate)
        {
            startInfo.ArgumentList.Add("-validate");
        }

        log.Info(
            "depot_start",
            $"Starting depot {depot.DepotId}.",
            ("depot", depot.DepotId),
            ("manifest", depot.ManifestId),
            ("validate", validate));
        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InstallerException("DepotDownloader could not be started.");
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            });
            await process.WaitForExitAsync(cancellationToken);
            log.Info(
                "depot_done",
                $"Depot {depot.DepotId} finished.",
                ("depot", depot.DepotId),
                ("code", process.ExitCode));
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InstallerException("DepotDownloader could not start. Security software may have blocked it.", exception);
        }
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static string DepotFailureMessage(int exitCode) =>
        $"DepotDownloader failed with exit code {exitCode}. " +
        "Read the Steam window for the exact error. Check ownership, Steam Guard, free space, and the install folder.";

    private static string SanitizeFileName(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (char character in value)
        {
            result.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }

        return result.ToString();
    }

}
