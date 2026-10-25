namespace ViggiSlideshow.Core;

/// <summary>The directory listing the scanner and walker need, so tests can supply a fake tree.</summary>
public interface IFileSystem
{
    IEnumerable<string> GetFiles(string directory);
    IEnumerable<string> GetDirectories(string directory);
}

public sealed class RealFileSystem : IFileSystem
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        RecurseSubdirectories = false,
    };

    public IEnumerable<string> GetFiles(string directory) => Directory.EnumerateFiles(directory, "*", Options);

    public IEnumerable<string> GetDirectories(string directory) => Directory.EnumerateDirectories(directory, "*", Options);
}
