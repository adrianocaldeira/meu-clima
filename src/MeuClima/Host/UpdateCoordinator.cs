using MeuClima.Services;

namespace MeuClima.Host;

/// <summary>Liga a autoatualização (portada do Meu Markdown) à bandeja e ao <c>update</c> do ViewState.</summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly BackgroundUpdateChecker _checker = new();
    private readonly Action<string, string> _notify;
    private readonly Func<Task> _republish;
    private readonly Action _shutdown;
    private bool _downloading;
    private double? _percent;

    public UpdateCoordinator(Action<string, string> notify, Func<Task> republish, Action shutdown, Func<bool> autoCheck)
    {
        _checker.Enabled = autoCheck;
        _notify = notify;
        _republish = republish;
        _shutdown = shutdown;
        _checker.UpdateAvailable += (_, info) =>
        {
            Sync();
            _notify($"Meu Clima {info.LatestVersion} disponível",
                BackgroundUpdateChecker.SummarizeNotes(info.ReleaseNotes) is { Length: > 0 } n ? n : "Abra a central para instalar.");
            _ = _republish();
        };
        Sync();
    }

    public void Start() => _checker.Start();

    /// <summary>Verifica agora; devolve o resultado em uma frase para toast ou aviso da bandeja.</summary>
    public async Task<(bool Ok, string Text)> CheckNowAsync()
    {
        var r = await _checker.CheckNowAsync();
        Sync();
        await _republish();
        return r.Status switch
        {
            UpdateCheckStatus.UpdateAvailable => (true, $"Versão {r.Info?.LatestVersion} disponível. Instale em Ajustes."),
            UpdateCheckStatus.UpToDate or UpdateCheckStatus.NoReleasePublished => (true, $"Você já está na versão mais recente ({VersionInfo.Current})."),
            _ => (false, $"Não deu para verificar atualização: {r.ErrorMessage ?? "sem resposta do GitHub"}. Tente de novo mais tarde."),
        };
    }

    public async Task CheckFromTrayAsync()
    {
        var (_, text) = await CheckNowAsync();
        // Versão nova já gera aviso pelo evento UpdateAvailable; aqui só o "está atualizado" e o erro.
        if (_checker.LatestAvailable is null) _notify("Meu Clima", text);
    }

    public async Task InstallAsync()
    {
        if (_checker.LatestAvailable is not { } info || _downloading) return;
        if (!InstallContext.Detect().SupportsAutoUpdate)
        {
            // Versão portable: não há instalador para rodar; abre a página da release.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true });
            return;
        }
        _checker.Paused = true;
        _downloading = true;
        Sync();
        await _republish();
        var progress = new Progress<AutoUpdateProgress>(p => { _percent = p.PercentComplete; Sync(); });
        var result = await new AutoUpdateService().UpdateAsync(info, () => Task.FromResult(true), progress, CancellationToken.None);
        if (result.Status == AutoUpdateStatus.Completed)
        {
            _shutdown();
            return;
        }
        _downloading = false;
        _percent = null;
        _checker.Paused = false;
        Sync();
        await _republish();
        if (result.Status != AutoUpdateStatus.UserCancelled)
            _notify("Atualização não concluída", $"{result.ErrorMessage} Baixe em {info.ReleaseUrl}");
    }

    private void Sync() => UpdateState.Current = new
    {
        available = _checker.LatestAvailable is not null,
        version = _checker.LatestAvailable?.LatestVersion,
        downloading = _downloading,
        percent = _percent,
    };

    public void Dispose() => _checker.Dispose();
}
