using System;
using Velopack;

namespace FreakyNikki;

/// <summary>
/// Explicit entry point. Velopack must run first so install/update/uninstall
/// hooks are handled before any WPF initialisation.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
