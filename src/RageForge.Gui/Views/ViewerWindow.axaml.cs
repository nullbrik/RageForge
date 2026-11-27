/* RAGEFORGE - Viewer Window */

using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RageForge.Gui.ViewModels;

namespace RageForge.Gui.Views;

public partial class ViewerWindow : Window
{
    private Point? _lastDrag;

    public ViewerWindow()
    {
        InitializeComponent();
        ResetViewButton.Click += (_, _) =>
        {
            Gpu.ResetView();
            Vm?.ResetView();
        };
        Gpu.SceneLost += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => Vm?.ReloadScene());
        Gpu.InitFailed += () => { if (Vm is { } vm) vm.UseSoftware = true; };
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName != nameof(PreviewViewModel.IsModel) || !vm.IsModel) return;
                await Task.Delay(2000);
                if (!Gpu.IsReady)
                    vm.UseSoftware = true;
            };
        };
        TrackList.DoubleTapped += (_, _) => Vm?.PlayAudioCommand.Execute(null);

        Viewport.PointerPressed += (_, e) =>
        {
            _lastDrag = e.GetPosition(Viewport);
            e.Pointer.Capture(Viewport);
        };
        Viewport.PointerReleased += (_, e) =>
        {
            _lastDrag = null;
            e.Pointer.Capture(null);
        };
        Viewport.PointerMoved += (_, e) =>
        {
            if (_lastDrag is not { } last || Vm is not { } vm) return;
            var p = e.GetPosition(Viewport);
            vm.Rotate((float)(p.X - last.X) * 0.01f, (float)(p.Y - last.Y) * 0.01f);
            _lastDrag = p;
        };
        Viewport.PointerWheelChanged += (_, e) => Vm?.Zoom(e.Delta.Y > 0 ? 1.15f : 1f / 1.15f);
        Viewport.DoubleTapped += (_, _) => Vm?.ResetView();
        Viewport.SizeChanged += (_, e) =>
            Vm?.SetViewport((int)(e.NewSize.Width * RenderScaling), (int)(e.NewSize.Height * RenderScaling));

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        Closed += (_, _) => Vm?.Dispose();
    }

    private PreviewViewModel? Vm => DataContext as PreviewViewModel;
}
