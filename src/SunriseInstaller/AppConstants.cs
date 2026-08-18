namespace Sunrise.Installer;

public static class AppConstants
{
    public const uint SteamAppId = 1085660;
    public const string GameExecutableName = "destiny2.exe";
    public const string ModRelativePath = @"bin\x64\steam_api64.dll";
    public const string SunriseOwner = "stanuwu";
    public const string SunriseRepository = "Sunrise";
    public const string DepotDownloaderOwner = "SteamRE";
    public const string DepotDownloaderRepository = "DepotDownloader";
    public const long FreshInstallFreeBytes = 110L * 1024 * 1024 * 1024;
    public const long RepairFreeBytes = 5L * 1024 * 1024 * 1024;
    public const long UpdateFreeBytes = 256L * 1024 * 1024;

    public static readonly DepotSpec BaseDepot = new(1085661, 7180122903232116872);

    public static readonly LanguageSpec[] Languages = [
        new("English", "english",
            new DepotSpec(1085662, 2210332166360342287)),
        new("French", "french",
            new DepotSpec(1085663, 2934940253687559290)),
        new("German", "german",
            new DepotSpec(1085664, 2207989571290186153)),
        new("Italian", "italian",
            new DepotSpec(1085665, 6668232053215128229)),
        new("Japanese", "japanese",
            new DepotSpec(1085666, 7430022397683116838)),
        new("Portuguese (Brazil)", "brazilian",
            new DepotSpec(1085667, 9037238175838085860)),
        new("Spanish (Spain)", "spanish",
            new DepotSpec(1085668, 3424833900894552134)),
        new("Russian", "russian",
            new DepotSpec(1085669, 4539277942371480381)),
        new("Polish", "polish",
            new DepotSpec(1085670, 6407581507105256731)),
        new("Chinese (Simplified)", "schinese",
            new DepotSpec(1085671, 4397663774546719308)),
        new("Chinese (Traditional)", "tchinese",
            new DepotSpec(1085672, 3906738704604711877)),
        new("Spanish (Latin America)", "latam",
            new DepotSpec(1085673, 4773170998099699561)),
        new("Korean", "koreana",
            new DepotSpec(1085674, 7148196199569436690)),
    ];

    public static LanguageSpec ResolveLanguage(string? steamLanguage)
    {
        foreach (LanguageSpec language in Languages)
        {
            if (language.SteamLanguage.Equals(steamLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }
        return Languages[0];
    }

    public static DepotSpec[] DepotsFor(LanguageSpec language) => [BaseDepot, language.Depot,];

    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SunriseInstaller");
}
