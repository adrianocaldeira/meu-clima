namespace MeuClima.Core.Switches;

public sealed record LuzConfig(bool ApagarAoBloquear, TimeSpan Espera, bool ApagarAoDesligar);

/// <summary><see cref="ReavaliarEm"/>: quanto falta para a espera configurada vencer.</summary>
public sealed record LuzDecisao(bool Apagar, string Motivo, TimeSpan? ReavaliarEm = null);

/// <summary>Estado da luz e da presença, e quando apagar. Sem E/S: o tempo entra por parâmetro.</summary>
/// <remarks>
/// O aparelho anuncia cada mudança em menos de 1 s (medido em 26/10/2026), então um "acendeu" durante o bloqueio
/// que não veio de comando nosso recente foi alguém no interruptor ou no celular.
/// </remarks>
public sealed class LuzPolicy
{
    public static readonly TimeSpan IdadeMaxima = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EcoDoComando = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SemRepetir = TimeSpan.FromSeconds(60);

    private DateTimeOffset? _bloqueadoDesde;
    private bool _acesaPorOutroNoBloqueio;
    private (bool On, DateTimeOffset Em)? _ultimoComando;

    public EwelinkAnnouncement? Ultimo { get; private set; }

    public void Registrar(EwelinkAnnouncement a)
    {
        var antes = Ultimo;
        Ultimo = a;
        if (antes is null || a.Seq == antes.Seq || a.On != true || antes.On == true || _bloqueadoDesde is null) return;
        var nosso = _ultimoComando is { On: true } c && a.VistoEm - c.Em <= EcoDoComando;
        if (!nosso) _acesaPorOutroNoBloqueio = true;
    }

    public void RegistrarComando(bool on, DateTimeOffset em) => _ultimoComando = (on, em);

    public void Bloqueou(DateTimeOffset em) => _bloqueadoDesde ??= em;

    public void Desbloqueou(DateTimeOffset em)
    {
        _bloqueadoDesde = null;
        _acesaPorOutroNoBloqueio = false;
    }

    public LuzDecisao DecidirBloqueio(LuzConfig cfg, DateTimeOffset agora)
    {
        if (!cfg.ApagarAoBloquear) return new(false, "apagar ao bloquear está desligado");
        if (_bloqueadoDesde is not { } desde) return new(false, "PC em uso");
        if (Comum(agora) is { } barrado) return barrado;
        var falta = cfg.Espera - (agora - desde);
        return falta > TimeSpan.Zero ? new(false, $"apago em {falta.TotalSeconds:0} s", falta) : new(true, "acesa com o PC bloqueado");
    }

    /// <summary>Desligamento e suspensão: decide só com o último anúncio, sem tempo para consultar o aparelho.</summary>
    public LuzDecisao DecidirDesligamento(LuzConfig cfg, DateTimeOffset agora) =>
        !cfg.ApagarAoDesligar ? new(false, "apagar ao desligar está desligado") : Comum(agora) ?? new(true, "acesa com o PC saindo");

    private LuzDecisao? Comum(DateTimeOffset agora)
    {
        if (Ultimo is not { } a || agora - a.VistoEm > IdadeMaxima) return new(false, "luz sem anúncio recente");
        if (a.Falha is AnnouncementFailure.SemChave or AnnouncementFailure.ChaveNaoDecifra)
            return new(false, "chave da luz não decifra: sincronize os aparelhos eWeLink");
        if (a.On is null) return new(false, "estado da luz ilegível");
        if (a.On == false) return new(false, "já apagada");
        if (_acesaPorOutroNoBloqueio) return new(false, "acesa no interruptor com o PC bloqueado");
        if (_ultimoComando is { On: false } c && agora - c.Em < SemRepetir) return new(false, "comando de apagar já enviado");
        return null;
    }
}
