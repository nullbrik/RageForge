/* RAGEFORGE - Crash Log */

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace RageForge.Gui;

public static class CrashLog
{
    public static string PathName { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RageForge", "crash.log");

    public static Action<string>? Report { get; set; }

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("fatal", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("background task", e.Exception);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Write("ui", e.Exception);
            e.Handled = true;
            Report?.Invoke($"Error: {e.Exception.GetBaseException().Message} (details in {PathName})");
        };
    }

    public static void Write(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            File.AppendAllText(PathName, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where} ({Environment.OSVersion}, {typeof(CrashLog).Assembly.GetName().Version})\n{ex}\n\n");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
        }
    }
}
