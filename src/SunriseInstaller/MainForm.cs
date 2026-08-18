using Sunrise.Installer.Services;

namespace Sunrise.Installer;

public sealed partial class MainForm : Form
{
    private readonly AppOptions options;
    private readonly InstallerLog log = new();
    private readonly InstallCoordinator coordinator;
    private readonly TextBox installPath = new();
    private readonly TextBox steamUsername = new();
    private readonly ComboBox gameLanguage = new();
    private readonly Label languageDownloadNotice = new();
    private readonly Label languageSupportWarning = new();
    private readonly Label status = new();
    private readonly ProgressBar progressBar = new();
    private readonly RichTextBox activity = new();
    private readonly Button browseButton = new();
    private readonly Button installButton = new();
    private readonly Button repairButton = new();
    private readonly Button updateButton = new();
    private readonly Button cancelButton = new();


    private CancellationTokenSource? operationCancellation;
    private bool busy;
    private bool preferencesLoaded;

    private LanguageSpec SelectedLanguage => gameLanguage.SelectedItem as LanguageSpec ?? AppConstants.Languages[0];

    public MainForm(AppOptions options)
    {
        this.options = options;
        coordinator = new InstallCoordinator(log, options);
        Text = options.IsTestMode ? "Sunrise Installer - Test Mode" : "Sunrise Installer";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 590);
        Size = new Size(820, 650);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);
        BuildLayout();
        gameLanguage.SelectedIndexChanged += GameLanguage_SelectedIndexChanged;
        log.MessageWritten += OnLogMessage;
        Shown += async (_, _) => await LoadPreferencesAsync();
        FormClosing += OnFormClosing;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            coordinator.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void GameLanguage_SelectedIndexChanged(object? sender, EventArgs eventArgs)
    {
        UpdateLanguageWarning();

        if (!preferencesLoaded || busy)
        {
            return;
        }

        try
        {
            await SavePreferencesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            ShowFailure(exception);
        }
    }

    private async Task LoadPreferencesAsync()
    {
        try
        {
            UserPreferences preferences = await InstallCoordinator.LoadPreferencesAsync(CancellationToken.None);
            installPath.Text = preferences.InstallDirectory;
            steamUsername.Text = preferences.SteamUsername;
            LanguageSpec savedLanguage = AppConstants.ResolveLanguage(preferences.SteamLanguage);
            gameLanguage.SelectedItem = savedLanguage;
            preferencesLoaded = true;
            await RefreshLocalStatusAsync();
        }
        catch (Exception exception)
        {
            ShowFailure(exception);
        }
    }

    private void UpdateLanguageWarning()
    {
        languageSupportWarning.Visible =
            !SelectedLanguage.SteamLanguage.Equals(
                "english",
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshLocalStatusAsync()
    {
        if (busy || string.IsNullOrWhiteSpace(installPath.Text))
        {
            return;
        }

        try
        {
            string installDirectory = Path.GetFullPath(installPath.Text.Trim());
            InstallerState? state = await coordinator.LoadStateAsync(installDirectory, CancellationToken.None);
            status.Text = state is null
                ? "No Sunrise install was found in this folder."
                : $"Installed Sunrise release: {state.ReleaseTag}";
        }
        catch
        {
            status.Text = "The install folder path is not valid.";
        }
    }

    private Task SavePreferencesAsync(
        CancellationToken cancellationToken)
    {
        UserPreferences preferences = new()
        {
            InstallDirectory = installPath.Text.Trim(),
            SteamUsername = steamUsername.Text.Trim(),
            SteamLanguage = SelectedLanguage.SteamLanguage,
        };

        return InstallCoordinator.SavePreferencesAsync(
            preferences,
            cancellationToken);
    }
}
