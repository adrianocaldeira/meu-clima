using System.Reflection;

namespace MeuClima;

/// <summary>Versão do app lida do assembly em runtime (fonte: <c>Directory.Build.props</c>).</summary>
public static class VersionInfo
{
    /// <summary>Versão semântica "X.Y.Z" (ex.: "0.1.0").</summary>
    public static string Current { get; } = ResolveCurrent();

    private static string ResolveCurrent()
    {
        var asm = Assembly.GetExecutingAssembly();

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
        {
            // O SDK anexa "+<sha do commit>" (SourceLink); a comparação de versão não aceita.
            var v = info.InformationalVersion;
            var plus = v.IndexOf('+');
            if (plus > 0) v = v[..plus];
            return v;
        }

        var version = asm.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }
}
