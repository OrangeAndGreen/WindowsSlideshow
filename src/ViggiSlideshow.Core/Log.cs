namespace ViggiSlideshow.Core;

/// <summary>
/// A small rolling log in %LOCALAPPDATA%\ViggiSlideshow. Errors go here instead of onto the
/// screen, and logging itself never throws.
/// </summary>
public static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Lock = new();

    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViggiSlideshow");

    public static string FilePath => Path.Combine(Folder, "log.txt");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e is null ? message : $"{message}: {e.GetType().Name}: {e.Message}{Environment.NewLine}{e.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Folder, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Nowhere left to report it
        }
    }
}
