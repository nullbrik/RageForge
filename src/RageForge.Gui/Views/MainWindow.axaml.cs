/* RAGEFORGE - Main Window */

using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using RageForge.Gui.Models;
using RageForge.Gui.ViewModels;

namespace RageForge.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        MenuOpenFolder.Click += async (_, _) => await PickGameFolderAsync();
        MenuExit.Click += (_, _) => Close();
        MenuOpen.Click += (_, _) => Vm?.Open(List.SelectedItem as RowItem);
        CtxOpen.Click += (_, _) => Vm?.Open(List.SelectedItem as RowItem);
        MenuCopyName.Click += async (_, _) => await CopyAsync(r => r.Name);
        CtxCopyName.Click += async (_, _) => await CopyAsync(r => r.Name);
        MenuCopyPath.Click += async (_, _) => await CopyAsync(r => Vm!.PathOf(r));
        CtxCopyPath.Click += async (_, _) => await CopyAsync(r => Vm!.PathOf(r));
        MenuProperties.Click += (_, _) => Properties();
        CtxProperties.Click += (_, _) => Properties();
        CtxOpenWorld.Click += (_, _) => Vm?.OpenInWorld(List.SelectedItem as RowItem);
        PlaceList.DoubleTapped += (_, _) => Vm?.World.LoadSelectedCommand.Execute(null);
        WorldViewport.CameraMoved += (eye, height) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Vm?.World.UpdateCamera(eye, height));
        WorldViewport.SceneLost += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => Vm?.World.Reload());
        WorldViewport.StatsUpdated += text => Avalonia.Threading.Dispatcher.UIThread.Post(() => WorldStats.Text = text);

        List.DoubleTapped += (_, _) => Vm?.Open(List.SelectedItem as RowItem);
        List.KeyDown += OnListKeyDown;
        SearchBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Down || List.ItemCount == 0) return;
            if (List.SelectedIndex < 0) List.SelectedIndex = 0;
            (List.ContainerFromIndex(List.SelectedIndex) as Control ?? List).Focus();
            e.Handled = true;
        };
        AddHandler(KeyDownEvent, OnWindowKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.ViewerRequested += ShowViewer;
            vm.World.ChunkReady += (chunk, scene) => WorldViewport.AddChunk(chunk, scene, chunk.Level);
            vm.World.ChunkDropped += cell => WorldViewport.RemoveChunk(cell);
            vm.World.UploadBacklog = () => WorldViewport.UploadBacklog;
            vm.World.NativeFormats = WorldViewport.IsNative;
            WorldViewport.TextureSource = vm.World.TextureMips;
            WorldViewport.UploadsDrained += () => Avalonia.Threading.Dispatcher.UIThread.Post(vm.World.ResumeStreaming);
            vm.World.StreamStarted += (target, radius, camera) => WorldViewport.BeginStreaming(target, radius, camera);
        };
        Closed += (_, _) => Vm?.Dispose();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.Open(List.SelectedItem as RowItem);
                e.Handled = true;
                break;
            case Key.Back:
                vm.BackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is { } mode && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.D1 or Key.D2 or Key.D3)
        {
            if (e.Key == Key.D1) mode.IsExplorerMode = true;
            else if (e.Key == Key.D2) mode.IsWorldMode = true;
            else mode.IsPedsMode = true;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ShowViewer(PreviewViewModel vm)
    {
        var w = new ViewerWindow { DataContext = vm };
        w.Show(this);
    }

    private void Properties()
    {
        if (Vm is not { } vm) return;
        var row = List.SelectedItem as RowItem ?? (vm.CurrentNode is { } n ? RowItem.ForFolder(n) : null);
        vm.ShowProperties(row);
    }

    private async Task PickGameFolderAsync()
    {
        if (Vm is not { } vm) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Game folder (contains the .rpf archives)",
                AllowMultiple = false,
            });
            if (folders.Count == 0) return;
            if (folders[0].TryGetLocalPath() is not { } path)
            {
                vm.Status = "Pick a folder on a local or mapped drive.";
                return;
            }
            await vm.OpenGameFolderAsync(path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("open game folder", ex);
            vm.Status = $"Cannot open the folder: {ex.Message} (details in {CrashLog.PathName})";
        }
    }

    private async Task CopyAsync(Func<RowItem, string> pick)
    {
        var rows = List.SelectedItems?.OfType<RowItem>().ToList() ?? [];
        if (rows.Count == 0 || Clipboard is null) return;
        await Clipboard.SetTextAsync(string.Join(Environment.NewLine, rows.Select(pick)));
    }
}
