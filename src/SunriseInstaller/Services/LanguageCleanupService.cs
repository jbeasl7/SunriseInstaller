namespace Sunrise.Installer.Services;

public sealed class LanguageCleanupService(InstallerLog log){
    public int RemoveDepotFiles(
        string installRoot,
        IEnumerable<string> relativePaths){
        string root = Path.GetFullPath(installRoot);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        int removed = 0;

        foreach (string relativePath in relativePaths){
            if (string.IsNullOrWhiteSpace(relativePath)){
                continue;
            }

            if (Path.IsPathRooted(relativePath)){
                throw new InstallerException(
                    "A language depot contained an unsafe file path.");
            }

            string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);

            string fullPath = Path.GetFullPath(Path.Combine(root, normalizedPath));

            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)){
                throw new InstallerException("A language depot contained an unsafe file path.");
            }

            if (!File.Exists(fullPath)){
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