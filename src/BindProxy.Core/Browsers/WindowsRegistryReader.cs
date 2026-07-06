using Microsoft.Win32;

namespace BindProxy.Core.Browsers;

public sealed class WindowsRegistryReader : IRegistryReader
{
    public IReadOnlyList<string> GetSubKeyNames(string keyPath)
    {
        using var key = Open(keyPath);
        return key?.GetSubKeyNames() ?? [];
    }

    public string? GetDefaultValue(string keyPath)
    {
        using var key = Open(keyPath);
        return key?.GetValue(null) as string;
    }

    private static RegistryKey? Open(string keyPath)
    {
        int separator = keyPath.IndexOf('\\');
        var hive = keyPath[..separator] switch
        {
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Unknown hive in '{keyPath}'", nameof(keyPath)),
        };
        return hive.OpenSubKey(keyPath[(separator + 1)..]);
    }
}
