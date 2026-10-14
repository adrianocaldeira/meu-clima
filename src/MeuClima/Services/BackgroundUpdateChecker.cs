// =====================================================================================
// Verificação silenciosa em background: ~10 s após o Start, depois a cada 30 min.
// Cada rodada tenta até 3x se a rede falhar (imediato, +30 s, +60 s). Porte da lógica
// que no Meu Markdown vivia na MainWindow, sem dependência de WPF.
//
// Como a UI usa (no App.OnStartup, depois de criar o ícone da bandeja):
//
//   _updates = new BackgroundUpdateChecker();
//   _updates.UpdateAvailable += (_, info) =>
//   {
//       // Já chega no thread de UI (SynchronizationContext capturado no Start).
//       // Ex.: balão/toast na bandeja "Nova versão vX — Atualizar agora / Mais tarde".
//       // "Atualizar agora" -> _updates.Paused = true; AutoUpdateService.UpdateAsync(info, ...)
//       // Texto curto das novidades: BackgroundUpdateChecker.SummarizeNotes(info.ReleaseNotes)
//   };
//   _updates.Start();
//   ...
//   _updates.Dispose();   // no OnExit
//
// Por padrão avisa uma vez por versão por sessão (bandeja não deve insistir a cada 30 min).
// =====================================================================================
namespace MeuClima.Services;

public sealed class BackgroundUpdateChecker : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];

    private readonly Func<UpdateService> _serviceFactory;
    private readonly UpdateLogger _logger;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private SynchronizationContext? _uiContext;
    private string? _lastNotifiedVersion;
    private Task? _loop;

    /// <summary>Disparado quando há versão mais nova; no thread que chamou <see cref="Start"/>.</summary>
    public event EventHandler<UpdateInfo>? UpdateAvailable;

    /// <summary>Última atualização encontrada (para reabrir o aviso pelo menu, por exemplo).</summary>
    public UpdateInfo? LatestAvailable { get; private set; }

    /// <summary>Pula as rodadas enquanto true (aviso aberto ou atualização em curso).</summary>
    public bool Paused { get; set; }

    public bool NotifyOncePerVersion { get; set; } = true;

    /// <summary>Verificação agendada ligada; a manual (<see cref="CheckNowAsync"/>) roda sempre.</summary>
    public Func<bool> Enabled { get; set; } = () => true;

    public BackgroundUpdateChecker(
        Func<UpdateService>? serviceFactory = null,
        UpdateLogger? logger = null,
        TimeSpan? initialDelay = null,
        TimeSpan? interval = null)
    {
        _serviceFactory = serviceFactory ?? (() => new UpdateService());
        _logger = logger ?? new UpdateLogger();
        _initialDelay = initialDelay ?? TimeSpan.FromSeconds(10);
        _interval = interval ?? TimeSpan.FromMinutes(30);
    }

    public void Start()
    {
        if (_loop != null) return;
        _uiContext = SynchronizationContext.Current;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Roda uma verificação agora, fora do agendamento (ex.: item de menu).</summary>
    public async Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct = default)
    {
        var result = await _serviceFactory().CheckForUpdatesAsync(ct);
        if (result is { Status: UpdateCheckStatus.UpdateAvailable, Info: { } info }) LatestAvailable = info;
        return result;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            _logger.Log($"BG_CHECK scheduled (delay {_initialDelay.TotalSeconds}s)");
            await Task.Delay(_initialDelay, ct);

            using var timer = new PeriodicTimer(_interval);
            do
            {
                if (!Paused && Enabled()) await CheckOnceAsync(ct);
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.Log($"BG_CHECK loop exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        try
        {
            UpdateCheckResult? result = null;
            for (int attempt = 0; attempt < RetryDelays.Length; attempt++)
            {
                if (RetryDelays[attempt] > TimeSpan.Zero)
                {
                    _logger.Log($"BG_CHECK retry #{attempt} after {RetryDelays[attempt].TotalSeconds}s");
                    await Task.Delay(RetryDelays[attempt], ct);
                }
                _logger.Log($"BG_CHECK starting (current=v{VersionInfo.Current})");
                result = await _serviceFactory().CheckForUpdatesAsync(ct);
                _logger.Log($"BG_CHECK result status={result.Status} latest={result.Info?.LatestVersion ?? "?"} err={result.ErrorMessage ?? ""}");

                if (result.Status != UpdateCheckStatus.NetworkError) break;
            }

            if (result is not { Status: UpdateCheckStatus.UpdateAvailable, Info: { } info }) return;

            LatestAvailable = info;
            if (NotifyOncePerVersion && info.LatestVersion == _lastNotifiedVersion) return;
            _lastNotifiedVersion = info.LatestVersion;

            Raise(info);
            _logger.Log("BG_CHECK notified");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Log($"BG_CHECK exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void Raise(UpdateInfo info)
    {
        if (_uiContext != null)
            _uiContext.Post(_ => UpdateAvailable?.Invoke(this, info), null);
        else
            UpdateAvailable?.Invoke(this, info);
    }

    /// <summary>Primeiros bullets das release notes (markdown), para caber num balão/toast.</summary>
    public static string SummarizeNotes(string? notes, int maxItems = 5, int maxChars = 350)
    {
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;
        var bullets = notes
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r').TrimStart())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => "• " + l[2..].Trim())
            .Take(maxItems)
            .ToList();
        if (bullets.Count == 0) return string.Empty;

        var text = string.Join("\n", bullets);
        if (text.Length > maxChars)
            text = text[..maxChars].TrimEnd() + "…";
        return text;
    }

    public void Dispose()
    {
        // Sem Dispose do CTS: o loop pode ainda estar saindo e consultando o token.
        _cts.Cancel();
    }
}
