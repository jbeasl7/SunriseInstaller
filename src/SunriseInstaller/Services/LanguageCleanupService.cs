namespace Sunrise.Installer.Services;

public sealed class LanguageCleanupService(InstallerLog log)
{
    /** A cleanup failure keeps the saved file list available for the next operation. */
    public async Task CompleteAsync(
        string installRoot,
        InstallerState state,
        CancellationToken cancellationToken)
    {
        if (state.PendingLanguageFiles.Length == 0)
        {
            return;
        }

        try
        {
            RemoveDepotFiles(installRoot, state.PendingLanguageFiles, cancellationToken);
            state.PendingLanguageFiles = [];
            await JsonStores.SaveStateAsync(installRoot, state, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InstallerException(
                "Sunrise is installed, but old language cleanup is incomplete. Use Check / Update to retry.",
                exception);
        }
    }

    /** Shared files and files needed by the selected language must survive cleanup retries. */
    public static string[] FindObsoleteFiles(IEnumerable<string> candidates, IEnumerable<string> keepFiles)
    {
        HashSet<string> keep = new(keepFiles.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        return candidates.Select(NormalizePath)
            .Where(path => !keep.Contains(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    public int RemoveDepotFiles(
        string installRoot,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(installRoot);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        int removed = 0;

        foreach (string relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            if (Path.IsPathRooted(relativePath))
            {
                throw new InstallerException(
                    "A language depot contained an unsafe file path.");
            }

            string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);

            string fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath));

            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallerException("A language depot contained an unsafe file path.");
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            File.Delete(fullPath);
            removed++;
        }

        log.Info(
            "language_files_removed",
            "Removed files from the previous language depot.",
            ("count", removed));

        return removed;
    }
}
