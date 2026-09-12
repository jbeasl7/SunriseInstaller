using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sunrise.Installer.Services;

public static class SunriseDefaults
{
    // Sunrise embeds default settings as RCDATA resource 101.
    private const int DefaultSettingsId = 101;
    private const int RcData = 10;
    // Resource-only loading must never execute the DLL's entry point.
    private const uint LoadLibraryAsDataFileExclusive = 0x40;
    // Sunrise accepts at most 1 MiB of settings.
    private const uint MaxSettingsBytes = 1024 * 1024;

    /** Reads the bundled settings without loading the DLL for execution. */
    public static JsonObject Read(string dllPath)
    {
        nint module = LoadLibraryEx(Path.GetFullPath(dllPath), 0, LoadLibraryAsDataFileExclusive);
        if (module == 0)
        {
            throw new InstallerException("The Sunrise DLL could not be opened for its default settings.");
        }

        try
        {
            nint resource = FindResource(module, DefaultSettingsId, RcData);
            uint size = resource == 0 ? 0 : SizeofResource(module, resource);
            nint loaded = resource == 0 ? 0 : LoadResource(module, resource);
            nint bytes = loaded == 0 ? 0 : LockResource(loaded);
            if (bytes == 0 || size == 0 || size > MaxSettingsBytes)
            {
                throw new InstallerException("The Sunrise DLL does not contain valid default settings.");
            }

            byte[] document = new byte[(int)size];
            Marshal.Copy(bytes, document, 0, document.Length);
            JsonObject settings = JsonNode.Parse(document) as JsonObject
                ?? throw new InstallerException("Sunrise default settings are not a JSON object.");
            if (settings["version"] is not JsonValue version || !version.TryGetValue(out uint number) ||
                number == 0 || settings["steam"] is not JsonObject)
            {
                throw new InstallerException("The Sunrise DLL has unsupported default settings.");
            }

            return settings;
        }
        catch (JsonException exception)
        {
            throw new InstallerException("The Sunrise DLL contains invalid default settings.", exception);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(string fileName, nint file, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindResource(nint module, nint name, nint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(nint module, nint resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LoadResource(nint module, nint resource);

    [DllImport("kernel32.dll")]
    private static extern nint LockResource(nint resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
