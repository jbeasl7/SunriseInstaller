# Sunrise Installer

Installer for [Sunrise](https://github.com/stanuwu/Sunrise).

## License

Sunrise Installer is Copyright (C) 2026 stanuwu. It is licensed under version 2 of the
[GNU General Public License](LICENSE). The full terms are stored with the project.

## DepotDownloader Credit and License

Sunrise Installer uses [DepotDownloader](https://github.com/SteamRE/DepotDownloader) to download Steam depots. DepotDownloader is developed by the SteamRE Team and uses SteamKit2.
Copyright for DepotDownloader belongs to its authors and contributors.

DepotDownloader is also licensed under GNU GPL version 2. Its full license is stored in
[DEPOTDOWNLOADER_LICENSE.txt](DEPOTDOWNLOADER_LICENSE.txt). The published Sunrise Installer contains
no DepotDownloader binary. DepotDownloader is not linked and is not a package or project dependency.

On first use, Sunrise Installer downloads the official `DepotDownloader-windows-x64.zip` release
from SteamRE into `%LOCALAPPDATA%`. It checks the GitHub digest when supplied, then extracts and
starts DepotDownloader as a separate program.

## Logo credit

Logo credit: [Solus](https://www.youtube.com/@Solus-yt).

## Operations

| action         | result |
|----------------|--------|
| Install        | Downloads the correct game build, selected language, and Sunrise. |
| Repair         | Validates the game and selected language, deletes the Sunrise config, and reinstalls the mod. |
| Check / Update | Checks for a new Sunrise release and installs it. |

Uses Steam app `1085660` with the shared Destiny 2 2.9.2 Windows depot
and one language-specific depot selected during installation.

| Language | Steam language | Depot | Manifest |
|---|---|---:|---:|
| English | `english` | `1085662` | `2210332166360342287` |
| French | `french` | `1085663` | `2934940253687559290` |
| German | `german` | `1085664` | `2207989571290186153` |
| Italian | `italian` | `1085665` | `6668232053215128229` |
| Japanese | `japanese` | `1085666` | `7430022397683116838` |
| Portuguese (Brazil) | `brazilian` | `1085667` | `9037238175838085860` |
| Spanish (Spain) | `spanish` | `1085668` | `3424833900894552134` |
| Russian | `russian` | `1085669` | `4539277942371480381` |
| Polish | `polish` | `1085670` | `6407581507105256731` |
| Chinese (Simplified) | `schinese` | `1085671` | `4397663774546719308` |
| Chinese (Traditional) | `tchinese` | `1085672` | `3906738704604711877` |
| Spanish (Latin America) | `latam` | `1085673` | `4773170998099699561` |
| Korean | `koreana` | `1085674` | `7148196199569436690` |

The shared Windows depot is:

| Depot | Manifest |
|---|---:|
| `1085661` | `7180122903232116872` |

When changing languages, the installer removes files belonging to the
previous language depot before installing the new one. The selected
language is also written to Sunrise's Steam language setting.

Install requires ~110 GiB of free space.

## Sunrise releases

Downloads latest from `https://github.com/stanuwu/Sunrise/releases`.
## Test mode

Run the installer with a local Sunrise DLL:

```powershell
.\SunriseInstaller.exe -test "C:\path\to\steam_api64.dll"
```

## Local data
`%LOCALAPPDATA%\SunriseInstaller\tools`. 

`%LOCALAPPDATA%\SunriseInstaller\logs`.
