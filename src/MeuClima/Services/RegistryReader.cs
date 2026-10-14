using Microsoft.Win32;

namespace MeuClima.Services;

/// <summary>Leitura de registro abstraída para testar o <see cref="InstallContext"/>.</summary>
public interface IRegistryReader
{
    string? ReadString(RegistryHive hive, string subKey, string valueName);
}

public sealed class RegistryReader : IRegistryReader
{
    public string? ReadString(RegistryHive hive, string subKey, string valueName)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
