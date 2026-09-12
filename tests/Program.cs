using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Sunrise.Installer;
using Sunrise.Installer.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Supply one or more Sunrise DLL paths; resources are read without executing the DLLs.");
            return 1;
        }

        string root = Directory.CreateTempSubdirectory("SunriseInstaller-tests-").FullName;
        try
        {
            InstallerLog log = new();
            foreach (string dll in args.Select(Path.GetFullPath))
            {
                CheckSettingsAsync(Path.Combine(root, Guid.NewGuid().ToString("N")), dll, log).GetAwaiter().GetResult();
                CheckUpdateAsync(Path.Combine(root, Guid.NewGuid().ToString("N")), dll, log).GetAwaiter().GetResult();
                Console.WriteLine($"PASS settings and update: {dll}");
            }
            CheckCleanupAsync(Path.Combine(root, "cleanup"), log).GetAwaiter().GetResult();
            CheckFolderSelection(Path.Combine(root, "folders"));
            Console.WriteLine("PASS cleanup retries, cancellation, path safety and folder selection");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(root).StartsWith("SunriseInstaller-tests-", StringComparison.Ordinal))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /** Fresh and reset settings must retain every bundled value except the chosen language. */
    private static async Task CheckSettingsAsync(string root, string dll, InstallerLog log)
    {
        SunriseSettingsService service = new(log);
        JsonObject defaults = SunriseDefaults.Read(dll);
        foreach (LanguageSpec language in AppConstants.Languages)
        {
            JsonObject expected = (JsonObject)defaults.DeepClone();
            expected["steam"]!["language"] = language.SteamLanguage;
            JsonObject actual = await service.PrepareAsync(root, dll, language.SteamLanguage, false, default);
            Require(JsonNode.DeepEquals(expected, actual), "Fresh settings lost bundled values.");
        }

        JsonObject existing = (JsonObject)defaults.DeepClone();
        existing["steam"]!["language"] = "french";
        existing["steam"]!["user"]!["persona_name"] = "Custom Player";
        existing["custom"] = false;
        await SunriseSettingsService.ApplyAsync(root, existing, default);
        JsonObject changed = await service.PrepareAsync(root, dll, "japanese", false, default);
        existing["steam"]!["language"] = "japanese";
        Require(JsonNode.DeepEquals(existing, changed), "Language switching changed compatible settings.");

        JsonObject reset = await service.PrepareAsync(root, dll, "german", true, default);
        JsonObject expectedReset = (JsonObject)defaults.DeepClone();
        expectedReset["steam"]!["language"] = "german";
        Require(JsonNode.DeepEquals(expectedReset, reset), "Repair did not recreate the bundled defaults.");

        existing["version"] = 0;
        existing["steam"]!["language"] = "french";
        await SunriseSettingsService.ApplyAsync(root, existing, default);
        JsonObject upgraded = await service.PrepareAsync(root, dll, null, false, default);
        JsonObject expectedUpgrade = (JsonObject)defaults.DeepClone();
        expectedUpgrade["steam"]!["language"] = "french";
        Require(JsonNode.DeepEquals(expectedUpgrade, upgraded), "A schema reset lost the installed language.");

        JsonObject incomplete = new()
        {
            ["version"] = defaults["version"]!.DeepClone(),
            ["steam"] = new JsonObject { ["language"] = "english" },
        };
        await SunriseSettingsService.ApplyAsync(root, incomplete, default);
        JsonObject repaired = await service.PrepareAsync(root, dll, "english", false, default);
        Require(JsonNode.DeepEquals(defaults, repaired), "Missing settings were not restored from the DLL.");

        byte[] before = await File.ReadAllBytesAsync(SettingsPath(root));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await ExpectAsync<OperationCanceledException>(() => SunriseSettingsService.ApplyAsync(root, reset, cancelled.Token));
        Require(before.SequenceEqual(await File.ReadAllBytesAsync(SettingsPath(root))), "Cancellation replaced settings.");
        Require(!Directory.EnumerateFiles(Path.GetDirectoryName(SettingsPath(root))!, "*.tmp").Any(), "A temporary settings file leaked.");
    }

    /** Update may delete obsolete files only after settings and install state have been saved. */
    private static async Task CheckUpdateAsync(string root, string dll, InstallerLog log)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
        await File.WriteAllTextAsync(Path.Combine(root, "destiny2.exe"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(root, AppConstants.ModRelativePath), "old DLL");
        string obsolete = Path.Combine(root, "old-language.txt");
        await File.WriteAllTextAsync(obsolete, "French");
        InstallerState state = new()
        {
            ReleaseTag = "old-release",
            SteamLanguage = "japanese",
            Manifests = new Dictionary<uint, ulong> { [1085661] = 123, [1085666] = 456 },
            PendingLanguageFiles = ["old-language.txt"],
        };
        await JsonStores.SaveStateAsync(root, state, default);
        string statePath = Path.Combine(root, ".sunrise", "install-state.json");
        using InstallCoordinator coordinator = new(log, new AppOptions(dll));

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath(root))!);
        await File.WriteAllTextAsync(SettingsPath(root), "invalid JSON");
        await ExpectAsync<InstallerException>(() => coordinator.UpdateAsync(root, null, default));
        Require(File.Exists(obsolete), "Settings failure deleted old language files.");
        Require(await File.ReadAllTextAsync(Path.Combine(root, AppConstants.ModRelativePath)) == "old DLL", "Settings failure replaced the DLL.");
        File.Delete(SettingsPath(root));

        using (FileStream locked = new(statePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await ExpectAsync<UnauthorizedAccessException>(() => coordinator.UpdateAsync(root, null, default));
            Require(File.Exists(obsolete), "State-save failure deleted old language files.");
        }

        Require(await coordinator.UpdateAsync(root, null, default), "Update did not install the payload.");
        InstallerState saved = await coordinator.LoadStateAsync(root, default) ?? throw new InvalidOperationException("State is missing.");
        Require(saved.SteamLanguage == "japanese" && saved.Manifests[1085661] == 123 && saved.Manifests[1085666] == 456,
            "Update changed the recorded game depots or language.");
        Require(!File.Exists(obsolete) && saved.PendingLanguageFiles.Length == 0, "Successful update did not finish cleanup.");
        JsonObject settings = JsonNode.Parse(await File.ReadAllTextAsync(SettingsPath(root)))!.AsObject();
        Require(settings["steam"]!["language"]!.GetValue<string>() == "japanese", "Update lost the installed language.");

        await File.WriteAllTextAsync(obsolete, "retry");
        saved.PendingLanguageFiles = ["old-language.txt"];
        await JsonStores.SaveStateAsync(root, saved, default);
        Require(!await coordinator.UpdateAsync(root, null, default), "A current payload was needlessly reinstalled.");
        Require(!File.Exists(obsolete), "Check / Update did not retry cleanup for a current release.");
    }

    /** Interrupted cleanup must remain retryable without deleting files needed by the new language. */
    private static async Task CheckCleanupAsync(string root, InstallerLog log)
    {
        Directory.CreateDirectory(root);
        string[] obsolete = LanguageCleanupService.FindObsoleteFiles(
            ["packages/old.pkg", "packages\\OLD.pkg", "packages/new.pkg", "packages/base.pkg"],
            ["packages\\new.pkg", "packages/base.pkg"]);
        Require(obsolete.SequenceEqual(["packages/old.pkg"]), "Cleanup did not protect shared or reselected files.");

        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        await File.WriteAllTextAsync(first, "old");
        await File.WriteAllTextAsync(second, "old");
        InstallerState state = new() { SteamLanguage = "japanese", PendingLanguageFiles = ["first.txt", "second.txt"] };
        await JsonStores.SaveStateAsync(root, state, default);
        LanguageCleanupService service = new(log);
        using (FileStream locked = new(second, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await ExpectAsync<InstallerException>(() => service.CompleteAsync(root, state, default));
        }
        InstallerState retry = await new JsonStores(log).LoadStateAsync(root, default)
            ?? throw new InvalidOperationException("State is missing.");
        Require(!File.Exists(first) && File.Exists(second) && retry.PendingLanguageFiles.Length == 2 && retry.SteamLanguage == "japanese",
            "Partial cleanup lost its retry state or changed the installed language.");
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await ExpectAsync<OperationCanceledException>(() => service.CompleteAsync(root, retry, cancelled.Token));
        Require(File.Exists(second), "Cancelled cleanup deleted a file.");
        await service.CompleteAsync(root, retry, default);
        Require(!File.Exists(second) && retry.PendingLanguageFiles.Length == 0, "Cleanup retry failed.");
        await ExpectAsync<InstallerException>(() => Task.FromResult(service.RemoveDepotFiles(root, ["../outside.txt"])));
    }

    /** Browsing folders selects their language without overwriting a deliberate choice in the same folder. */
    private static void CheckFolderSelection(string root)
    {
        string english = Path.Combine(root, "english");
        string french = Path.Combine(root, "french");
        JsonStores.SaveStateAsync(english, new InstallerState { SteamLanguage = "english" }, default).GetAwaiter().GetResult();
        JsonStores.SaveStateAsync(french, new InstallerState { SteamLanguage = "french" }, default).GetAwaiter().GetResult();
        using MainForm form = new(new AppOptions(null));
        form.BindingContext = new BindingContext();
        form.CreateControl();
        SetField(form, "preferencesLoaded", true);
        TextBox path = GetField<TextBox>(form, "installPath");
        ComboBox language = GetField<ComboBox>(form, "gameLanguage");
        path.Text = english;
        Refresh(form);
        Require(((LanguageSpec)language.SelectedItem!).SteamLanguage == "english", "Wrong language for the first folder.");
        SetField(form, "preferencesLoaded", false);
        language.SelectedItem = AppConstants.ResolveLanguage("japanese");
        SetField(form, "preferencesLoaded", true);
        Refresh(form);
        Require(((LanguageSpec)language.SelectedItem!).SteamLanguage == "japanese", "Status refresh replaced a deliberate choice.");
        path.Text = french;
        Refresh(form);
        Require(((LanguageSpec)language.SelectedItem!).SteamLanguage == "french", "Folder selection reused a global language preference.");
        path.Text = english;
        path.Text = french;
        path.Text = english;
        Refresh(form);
        Require(((LanguageSpec)language.SelectedItem!).SteamLanguage == "english", "An older folder lookup overwrote the latest selection.");
    }

    private static string SettingsPath(string root) => Path.Combine(root, "bin", "x64", "Sunrise", "settings.json");

    private static T GetField<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void SetField(MainForm form, string name, object value) =>
        typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, value);

    /** WinForms continuations need a message pump on the test's STA thread. */
    private static void Refresh(MainForm form)
    {
        Task pending = (Task)typeof(MainForm).GetMethod("RefreshLocalStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
        Stopwatch timeout = Stopwatch.StartNew();
        while (!pending.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Require(pending.IsCompleted, "Folder lookup timed out.");
        pending.GetAwaiter().GetResult();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
