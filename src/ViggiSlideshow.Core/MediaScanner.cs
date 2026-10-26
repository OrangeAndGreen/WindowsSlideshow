namespace ViggiSlideshow.Core;

/// <summary>
/// Walks the folder tree and adds every media file to a <see cref="MediaLibrary"/>.
/// Folders are visited in random order, so random draws are varied from the first second
/// instead of all coming from the first folder while a large tree is still being scanned.
/// </summary>
public static class MediaScanner
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", ".thumbnails", "@eaDir",
    };

    public static void Scan(string root, IFileSystem fileSystem, MediaLibrary library, Random random,
        CancellationToken cancel, Action<string, Exception>? onError = null)
    {
        var pending = new List<string> { root };
        while (pending.Count > 0 && !cancel.IsCancellationRequested)
        {
            int index = random.Next(pending.Count);
            string directory = pending[index];
            pending[index] = pending[^1];
            pending.RemoveAt(pending.Count - 1);

            try
            {
                foreach (string file in fileSystem.GetFiles(directory))
                    library.Add(file);

                foreach (string subdirectory in fileSystem.GetDirectories(directory))
                {
                    if (!SkippedFolders.Contains(Path.GetFileName(subdirectory)))
                        pending.Add(subdirectory);
                }
            }
            catch (Exception e)
            {
                onError?.Invoke(directory, e);
            }
        }

        if (!cancel.IsCancellationRequested)
            library.ScanComplete = true;
    }

    public static bool IsSkippedFolder(string name) => SkippedFolders.Contains(name);
}
