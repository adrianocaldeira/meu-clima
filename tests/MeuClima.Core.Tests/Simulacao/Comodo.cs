using MeuClima.Core.Devices;
using static MeuClima.Core.Tests.TestKit;

namespace MeuClima.Core.Tests.Simulacao;

/// <summary>Cômodo de 1ª ordem com o aparelho: troca calor com fora, o frio puxa para o alvo, o sensor lê graus inteiros com ruído.</summary>
internal sealed class Comodo(Random rng, double inicialC = 24)
{
    public double RealC { get; private set; } = inicialC;
    public bool Conectado { get; set; } = true;
    public DeviceSnapshot Estado { get; set; } = Snap(on: false, mode: AcMode.Cool, fan: FanSpeed.Auto, target: 24, ambient: null, eco: false);

    public void Passo(double foraC, TimeSpan dt)
    {
        var min = dt.TotalMinutes;
        RealC += (foraC - RealC) * 0.01 * min;
        if (Estado.On && Estado.Mode == AcMode.Cool && RealC > (Estado.TargetC ?? 24) - 1)
            RealC -= (Estado.Fan == FanSpeed.Turbo ? 0.12 : 0.06) * min;
        if (Estado.On && Estado.Mode == AcMode.Heat && RealC < (Estado.TargetC ?? 24))
            RealC += 0.06 * min;
    }

    public DeviceSnapshot Ler(DateTimeOffset now) => Conectado
        ? Estado with { AmbientC = (int)Math.Round(RealC + (rng.NextDouble() - 0.5) * 0.8), ReadAt = now, Connected = true }
        : Estado with { Connected = false, ReadAt = now };

    public void Aplicar(IEnumerable<DeviceCommand> comandos)
    {
        if (!Conectado) return;
        foreach (var c in comandos) Estado = c.ApplyTo(Estado);
    }
}
