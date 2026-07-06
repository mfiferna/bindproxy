namespace BindProxy.Core.Browsers;

/// <summary>Registry access behind an interface so browser detection is testable.
/// Key paths are full, e.g. @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet".</summary>
public interface IRegistryReader
{
    IReadOnlyList<string> GetSubKeyNames(string keyPath);
    string? GetDefaultValue(string keyPath);
}
