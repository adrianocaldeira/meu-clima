using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeuClima.Host;
using WinForms = System.Windows.Forms;

namespace MeuClima.Ui;

public static class AppIcon
{
    private static Stream Open() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream("MeuClima.app-icon.ico")
        ?? throw new InvalidOperationException("Ícone embutido não encontrado.");

    public static ImageSource ImageSource { get; } = BitmapFrame.Create(Open());

    public static System.Drawing.Icon TrayIcon()
    {
        using var s = Open();
        return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
    }
}

/// <summary>Ícone na bandeja: clique abre o painel; menu com central, início com o Windows e sair.</summary>
public sealed class Tray : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _autostart;

    public Tray(Action toggleFlyout, Action<string?> openCentral, Action quit)
    {
        _autostart = new WinForms.ToolStripMenuItem("Iniciar com o Windows") { CheckOnClick = true };
        _autostart.Click += (_, _) => AutostartChanged?.Invoke(_autostart.Checked);
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Abrir o painel", null, (_, _) => toggleFlyout());
        menu.Items.Add("Abrir a central", null, (_, _) => openCentral(null));
        menu.Items.Add("Regras", null, (_, _) => openCentral("rules"));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_autostart);
        menu.Items.Add("Verificar atualizações", null, (_, _) => CheckUpdateClicked?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Sair do Meu Clima", null, (_, _) => quit());
        menu.Opening += (_, _) => _autostart.Checked = Autostart.IsEnabled();

        _icon = new WinForms.NotifyIcon
        {
            Icon = AppIcon.TrayIcon(),
            Text = "Meu Clima",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) toggleFlyout(); };
        _icon.BalloonTipClicked += (_, _) => toggleFlyout();
    }

    public event Action<bool>? AutostartChanged;
    public event Action? CheckUpdateClicked;

    /// <summary>Texto curto ao passar o mouse (limite de 127 caracteres do Windows).</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    public void Notify(string title, string body) =>
        _icon.ShowBalloonTip(6000, title, body, WinForms.ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
