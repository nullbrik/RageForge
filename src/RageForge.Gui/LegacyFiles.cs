/* RAGEFORGE - Legacy Files */

namespace RageForge.Gui;

public static class LegacyFiles
{
    public static string VideoDir { get; } = Path.Combine(Path.GetTempPath(), "RageForge");

    public static string UpdateDir { get; } = Path.Combine(Path.GetTempPath(), "RageForge-update");

    public static void Clean()
    {
        try
        {
            if (Directory.Exists(VideoDir))
            {
                foreach (var f in Directory.EnumerateFiles(VideoDir, "*.bk2"))
                    File.Delete(f);
                if (!Directory.EnumerateFileSystemEntries(VideoDir).Any())
                    Directory.Delete(VideoDir);
            }
            if (Directory.Exists(UpdateDir))
                Directory.Delete(UpdateDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
