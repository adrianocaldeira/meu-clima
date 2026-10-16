using MeuClima.Core.Devices;

namespace MeuClima.Core.Rules;

/// <summary>Como terminou o envio de um plano à nuvem.</summary>
public enum ResultadoEnvio { Ok, Rejected, Failed, Deferred }

/// <summary>Avisos de ação ("Conforto ligou o ar") que esperam a nuvem aceitar um comando do próprio plano.</summary>
public sealed class AvisosPendentes
{
    public static readonly TimeSpan Prazo = TimeSpan.FromMinutes(15);

    private readonly Dictionary<(string Device, string Rule), List<(Notification Aviso, DateTimeOffset Em, IReadOnlyList<DeviceCommand> Cmds)>> _porPlano = [];

    public void Guardar(PlannedCommands plano, DateTimeOffset now)
    {
        if (plano.Notifications.Count == 0) return;
        var chave = (plano.DeviceId, plano.RuleName);
        if (!_porPlano.TryGetValue(chave, out var lista)) _porPlano[chave] = lista = [];
        foreach (var a in plano.Notifications)
            if (!lista.Any(x => x.Aviso.Title == a.Title && x.Aviso.Body == a.Body)) lista.Add((a, now, plano.Commands));
    }

    public void Expirar(DateTimeOffset now)
    {
        foreach (var lista in _porPlano.Values) lista.RemoveAll(a => now - a.Em > Prazo);
    }

    /// <summary>Devolve os avisos a mostrar depois do envio de <paramref name="enviados"/>.</summary>
    /// <remarks>
    /// Só sai o aviso cujo plano tem algum comando aceito: o envio de outra fase não confirma o turbo recusado.
    /// Recusa descarta; falha de rede ou timeout guardam, porque o comando pode ter sido aplicado e vai de novo.
    /// </remarks>
    public IReadOnlyList<Notification> Concluir(string deviceId, string regra, IReadOnlyList<DeviceCommand> enviados, ResultadoEnvio resultado)
    {
        var chave = (deviceId, regra);
        if (!_porPlano.TryGetValue(chave, out var lista)) return [];
        if (resultado == ResultadoEnvio.Rejected)
        {
            _porPlano.Remove(chave);
            return [];
        }
        if (resultado == ResultadoEnvio.Failed) return [];
        var sair = lista.Where(x => x.Cmds.Intersect(enviados).Any()).ToList();
        lista.RemoveAll(sair.Contains);
        return [.. sair.Select(x => x.Aviso)];
    }
}
