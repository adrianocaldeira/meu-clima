using System.IO;
using Microsoft.Win32;

namespace MeuClima.Services;

public enum InstallType { PerUser, PerMachine, Portable }

/// <summary>Detecta como o app foi instalado; decide se o auto-update é possível e se pede UAC.</summary>
public sealed record InstallContext(InstallType Type, string InstallPath)
{
    // Gravada pelo installer/MeuClima.iss ([Registry]: InstallPath, Version, InstallScope).
    private const string SubKey = @"Software\MeuClima";

    public bool RequiresElevation => Type == InstallType.PerMachine;
    public bool SupportsAutoUpdate => Type != InstallType.Portable;

    public static InstallContext Detect()
        => Detect(new RegistryReader(), AppContext.BaseDirectory);

    public static InstallContext Detect(IRegistryReader registry, string basePath)
    {
        var machineScope = registry.ReadString(RegistryHive.LocalMachine, SubKey, "InstallScope");
        if (string.Equals(machineScope, "machine", StringComparison.OrdinalIgnoreCase))
            return new InstallContext(InstallType.PerMachine, basePath);

        var userScope = registry.ReadString(RegistryHive.CurrentUser, SubKey, "InstallScope");
        if (string.Equals(userScope, "user", StringComparison.OrdinalIgnoreCase))
            return new InstallContext(InstallType.PerUser, basePath);

        var normBase = NormalizePath(basePath);

        var machinePath = registry.ReadString(RegistryHive.LocalMachine, SubKey, "InstallPath");
        if (!string.IsNullOrEmpty(machinePath) && PathMatches(machinePath, normBase))
            return new InstallContext(InstallType.PerMachine, basePath);

        var userPath = registry.ReadString(RegistryHive.CurrentUser, SubKey, "InstallPath");
        if (!string.IsNullOrEmpty(userPath) && PathMatches(userPath, normBase))
            return new InstallContext(InstallType.PerUser, basePath);

        // Sem registro: heurística pelo caminho do executável.
        if (normBase.Contains(@"\program files", StringComparison.OrdinalIgnoreCase))
            return new InstallContext(InstallType.PerMachine, basePath);

        var localAppDataPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        if (normBase.StartsWith(NormalizePath(localAppDataPrograms), StringComparison.OrdinalIgnoreCase))
            return new InstallContext(InstallType.PerUser, basePath);

        return new InstallContext(InstallType.Portable, basePath);
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd('\\', '/');

    private static bool PathMatches(string a, string b)
        => string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);
}
