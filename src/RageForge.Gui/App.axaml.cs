/* RAGEFORGE - App */

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RageForge.Gui.ViewModels;
using RageForge.Gui.Views;

namespace RageForge.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CrashLog.Install();
            LegacyFiles.Clean();
            var vm = new MainViewModel();
            CrashLog.Report = message => Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.Status = message);
            var window = new MainWindow { DataContext = vm };
            var args = desktop.Args ?? [];
            int at = Array.IndexOf(args, "--open");
            string? open = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            window.Opened += async (_, _) =>
            {
                _ = vm.CheckForUpdateAsync();
                await vm.InitializeAsync();
                if (open is not null)
                    vm.OpenByName(open);
                int t = Array.IndexOf(args, "--time");
                if (t >= 0 && t + 1 < args.Length && double.TryParse(args[t + 1], System.Globalization.CultureInfo.InvariantCulture, out var hours))
                    vm.World.TimeOfDay = Math.Clamp(hours, 0, 23.99);
                int w = Array.IndexOf(args, "--world");
                if (w >= 0 && w + 1 < args.Length)
                    vm.OpenWorldByName(args[w + 1]);
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}