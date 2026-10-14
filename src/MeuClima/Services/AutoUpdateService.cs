// =====================================================================================
// Atualização automática — baixa o instalador da release, valida tamanho e SHA-256,
// pede para o app encerrar e dispara o setup em modo silencioso. O setup
// (installer/MeuClima.iss, [Run] com Check: WizardSilent) reabre o app na versão nova.
// Independente de UI.
//
// Como a UI usa (só quando InstallContext.Detect().SupportsAutoUpdate; portable abre
// info.ReleaseUrl no navegador):
//
//   var result = await new AutoUpdateService().UpdateAsync(
//       info,                                   // de UpdateService / BackgroundUpdateChecker
//       requestShutdown: () => Task.FromResult(true),   // false = usuário desistiu; pode
//                                                        // perguntar aqui se houver algo em curso
//       progress: new Progress<AutoUpdateProgress>(p => /* barra/texto */),
//       ct);
//
//   if (result.Status == AutoUpdateStatus.Completed)
//       // O setup começa em ~2 s (timeout no cmd). Encerrar JÁ, no thread de UI:
//       // esconder/Dispose do ícone da bandeja (senão fica "fantasma") e
//       // Application.Current.Shutdown().
//   else if (result.Status != AutoUpdateStatus.UserCancelled)
//       // mostrar result.ErrorMessage + link para info.ReleaseUrl (download manual)
//
// Sem ConfigureAwait(false): chamado do thread de UI, requestShutdown roda nele;
// Progress<T> criado no thread de UI já reporta nele.
// =====================================================================================
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace MeuClima.Services;

public enum AutoUpdateStatus
{
    Downloading,
    Validating,
    PreparingShutdown,
    Launching,
    Completed,
    NetworkError,
    IntegrityError,
    LaunchError,
    UserCancelled
}

public sealed record AutoUpdateProgress(
    AutoUpdateStatus Status,
    double PercentComplete,
    string Message);

public sealed record AutoUpdateResult(
    AutoUpdateStatus Status,
    string? DownloadedSetupPath,
    string? ErrorMessage);

public class AutoUpdateService
{
    private readonly HttpClient? _injectedHttp;
    private readonly string _downloadDir;
    private readonly UpdateLogger _logger;
    private readonly Func<string, bool>? _launcher;
    private readonly TimeSpan _downloadTimeout;

    public AutoUpdateService()
        : this(http: null, downloadDir: null, logger: null, launcher: null) { }

    public AutoUpdateService(HttpClient? http, string? downloadDir, UpdateLogger? logger,
        Func<string, bool>? launcher = null, TimeSpan? downloadTimeout = null)
    {
        _injectedHttp = http;
        _downloadDir = downloadDir ?? DefaultDownloadDir();
        _logger = logger ?? new UpdateLogger();
        _launcher = launcher;
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(10);
    }

    private static string DefaultDownloadDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MeuClima", "Updates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<AutoUpdateResult> UpdateAsync(
        UpdateInfo info,
        Func<Task<bool>> requestShutdown,
        IProgress<AutoUpdateProgress> progress,
        CancellationToken ct)
    {
        _logger.Log($"START update {info.CurrentVersion} -> {info.LatestVersion}");

        if (string.IsNullOrEmpty(info.DownloadUrl))
        {
            _logger.Log("FAIL release sem asset de instalador");
            return new AutoUpdateResult(AutoUpdateStatus.NetworkError, null,
                "A release não tem instalador anexado.");
        }

        try
        {
            foreach (var f in Directory.EnumerateFiles(_downloadDir, "*.exe"))
                File.Delete(f);
        }
        catch (Exception ex) { _logger.Log($"WARN cleanup: {ex.Message}"); }

        var fileName = $"MeuClima-Setup-v{info.LatestVersion}.exe";
        var path = Path.Combine(_downloadDir, fileName);

        HttpClient? owned = null;
        HttpClient http;
        if (_injectedHttp != null)
        {
            http = _injectedHttp;
        }
        else
        {
            owned = new HttpClient(new HttpClientHandler { UseProxy = false });
            owned.DefaultRequestHeaders.UserAgent.ParseAdd("MeuClima-Updater");
            owned.Timeout = Timeout.InfiniteTimeSpan;
            http = owned;
        }
        // HttpClient.Timeout não cobre a leitura do corpo com ResponseHeadersRead: o teto vale para o download todo.
        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        downloadCts.CancelAfter(_downloadTimeout);
        var dct = downloadCts.Token;
        try
        {
            progress.Report(new AutoUpdateProgress(AutoUpdateStatus.Downloading, 0, "Baixando atualização..."));
            using (var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, dct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? info.AssetSize;
                using var src = await response.Content.ReadAsStreamAsync(dct);
                using var dst = File.Create(path);
                var buf = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await src.ReadAsync(buf, dct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read), dct);
                    copied += read;
                    if (total > 0)
                    {
                        var pct = copied * 100.0 / total;
                        progress.Report(new AutoUpdateProgress(AutoUpdateStatus.Downloading, pct, $"Baixando... {pct:F0}%"));
                    }
                }
            }

            var fileSize = new FileInfo(path).Length;
            if (info.AssetSize <= 0 || fileSize != info.AssetSize)
            {
                TryDelete(path);
                _logger.Log($"FAIL size mismatch: expected={info.AssetSize}, got={fileSize}");
                return new AutoUpdateResult(AutoUpdateStatus.IntegrityError, null,
                    "Arquivo baixado tem tamanho inesperado.");
            }

            progress.Report(new AutoUpdateProgress(AutoUpdateStatus.Validating, 100, "Verificando integridade..."));
            var digest = await ComputeSha256Async(path, ct);
            // O GitHub publica o digest como "sha256:<hex>".
            var expected = info.AssetDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? info.AssetDigest[7..]
                : info.AssetDigest;
            // Sem digest não há como provar a origem: não executa (o GitHub sempre publica para asset completo).
            if (string.IsNullOrEmpty(expected) || !string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(path);
                _logger.Log($"FAIL hash mismatch: expected={expected}, got={digest}");
                return new AutoUpdateResult(AutoUpdateStatus.IntegrityError, null,
                    "Arquivo baixado falhou na verificação de integridade.");
            }

            progress.Report(new AutoUpdateProgress(AutoUpdateStatus.PreparingShutdown, 100, "Preparando para instalar..."));
            var canShutdown = await requestShutdown();
            if (!canShutdown)
            {
                _logger.Log("ABORT user cancelled shutdown");
                return new AutoUpdateResult(AutoUpdateStatus.UserCancelled, path, "Atualização cancelada pelo usuário.");
            }

            progress.Report(new AutoUpdateProgress(AutoUpdateStatus.Launching, 100, "Iniciando instalador..."));
            var launched = (_launcher ?? DefaultLauncher)(path);
            if (!launched)
            {
                _logger.Log("FAIL launch returned false");
                return new AutoUpdateResult(AutoUpdateStatus.LaunchError, path,
                    "Não foi possível iniciar o instalador.");
            }

            _logger.Log("OK launched setup");
            return new AutoUpdateResult(AutoUpdateStatus.Completed, path, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryDelete(path);
            _logger.Log("FAIL download timeout");
            return new AutoUpdateResult(AutoUpdateStatus.NetworkError, null, "Tempo esgotado ao baixar a atualização.");
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            _logger.Log("ABORT cancelled mid-download");
            return new AutoUpdateResult(AutoUpdateStatus.UserCancelled, null, "Cancelado.");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(path);
            _logger.Log($"FAIL network: {ex.Message}");
            return new AutoUpdateResult(AutoUpdateStatus.NetworkError, null, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.Log($"FAIL unexpected: {ex.Message}");
            return new AutoUpdateResult(AutoUpdateStatus.LaunchError, path, ex.Message);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static bool DefaultLauncher(string setupPath)
    {
        try
        {
            // Setup disparado direto concorre com o app ainda aberto ("incapaz de fechar").
            // O "timeout 2" no cmd dá tempo do app encerrar depois que este método retorna.
            // /VERYSILENT sem UI; /SUPPRESSMSGBOXES aborta em silêncio se houver lock; /NORESTART
            // nunca reinicia o Windows. Quem reabre o app é o [Run] Check: WizardSilent do .iss.
            var workingDir = Path.GetDirectoryName(setupPath) ?? "";
            var args = $"/c timeout /t 2 /nobreak >nul & start \"\" \"{setupPath}\" " +
                       "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
            var psi = new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDir,
            };
            var p = Process.Start(psi);
            return p != null;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = File.OpenRead(path);
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
