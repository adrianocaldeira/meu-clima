// =====================================================================================
// Verificação de atualização — consulta a release "latest" do GitHub e compara com
// VersionInfo.Current. Independente de UI.
//
// Como a UI usa:
//   var result = await new UpdateService().CheckForUpdatesAsync(ct);
//   switch (result.Status)
//     UpToDate / NoReleasePublished -> "Você está na versão mais recente"
//     UpdateAvailable               -> mostrar result.Info (versões + ReleaseNotes) e oferecer
//                                      AutoUpdateService.UpdateAsync(result.Info, ...)
//     NetworkError / ParseError     -> mostrar result.ErrorMessage (check silencioso: só logar).
//                                      Inclui release nova cujo instalador ainda está subindo.
// Para o check periódico em background, use BackgroundUpdateChecker.
// =====================================================================================
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MeuClima.Services;

public record UpdateInfo(
    string LatestVersion,
    string CurrentVersion,
    string DownloadUrl,
    string ReleaseUrl,
    string ReleaseNotes,
    string AssetDigest,
    long AssetSize);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    /// <summary>Repositório ainda sem release publicada (GitHub responde 404).</summary>
    NoReleasePublished,
    NetworkError,
    ParseError
}

public record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info, string? ErrorMessage);

public class UpdateService
{
    public const string RepositoryUrl = "https://github.com/adrianocaldeira/meu-clima";

    private const string LatestReleaseApi =
        "https://api.github.com/repos/adrianocaldeira/meu-clima/releases/latest";

    // Prefixo do asset do instalador gerado pelo installer/MeuClima.iss (OutputBaseFilename).
    private const string SetupAssetPrefix = "MeuClima-Setup-";

    private readonly HttpClient? _injectedHttp;
    private readonly Func<string> _currentVersion;

    public UpdateService() : this(null, null) { }
    public UpdateService(HttpClient http) : this(http, null) { }

    public UpdateService(HttpClient? http, Func<string>? currentVersion)
    {
        _injectedHttp = http;
        _currentVersion = currentVersion ?? (() => VersionInfo.Current);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        string currentVersion = _currentVersion();

        HttpClient? owned = null;
        HttpClient http;
        if (_injectedHttp != null)
        {
            http = _injectedHttp;
        }
        else
        {
            // UseProxy=false evita a auto-detecção WPAD, que pode atrasar 30s+ a 1ª requisição.
            var handler = new HttpClientHandler { UseProxy = false };
            owned = new HttpClient(handler);
            owned.DefaultRequestHeaders.UserAgent.ParseAdd("MeuClima-UpdateChecker");
            owned.Timeout = TimeSpan.FromSeconds(30);
            http = owned;
        }

        try
        {
            var release = await http.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, ct);
            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                return new UpdateCheckResult(UpdateCheckStatus.ParseError, null,
                    "Resposta inválida do GitHub.");
            }

            var latestTag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(latestTag, out var latest))
            {
                return new UpdateCheckResult(UpdateCheckStatus.ParseError, null,
                    $"Não consegui interpretar a versão remota '{release.TagName}'.");
            }

            if (!Version.TryParse(currentVersion, out var current))
            {
                current = new Version(0, 0, 0, 0);
            }

            // A release também traz o MeuClima.exe portable; executá-lo com /VERYSILENT não instala nada.
            var installer = release.Assets?
                .FirstOrDefault(a => a.Name?.StartsWith(SetupAssetPrefix, StringComparison.OrdinalIgnoreCase) == true
                                     && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            var info = new UpdateInfo(
                LatestVersion: latest.ToString(3),
                CurrentVersion: current.ToString(3),
                DownloadUrl: installer?.BrowserDownloadUrl ?? "",
                ReleaseUrl: release.HtmlUrl ?? "",
                ReleaseNotes: release.Body ?? "",
                AssetDigest: installer?.Digest ?? "",
                AssetSize: installer?.Size ?? 0);

            if (latest <= current)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, info, null);

            // A release é publicada antes dos assets terminarem de subir: sem instalador íntegro,
            // trata como falha transitória para a próxima rodada tentar de novo.
            if (installer is not { State: "uploaded", Size: > 0 } || string.IsNullOrEmpty(installer.Digest)
                || installer.BrowserDownloadUrl?.StartsWith(RepositoryUrl + "/releases/download/", StringComparison.Ordinal) != true)
            {
                return new UpdateCheckResult(UpdateCheckStatus.NetworkError, info,
                    $"A versão {info.LatestVersion} ainda não tem instalador disponível.");
            }

            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoReleasePublished, null,
                "Nenhuma release publicada ainda.");
        }
        catch (HttpRequestException ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null, ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, null, "Tempo esgotado.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.ParseError, null, ex.Message);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
