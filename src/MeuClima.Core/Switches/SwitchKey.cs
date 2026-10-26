namespace MeuClima.Core.Switches;

/// <summary>Chave de um interruptor eWeLink, tirada da nuvem uma vez; muda só se o aparelho for pareado de novo.</summary>
public sealed record SwitchKey(string Name, string DeviceKey, int? Uiid);
