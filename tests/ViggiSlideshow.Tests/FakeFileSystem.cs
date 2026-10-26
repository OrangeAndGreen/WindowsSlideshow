using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

/// <summary>A folder tree built from a list of file paths. Listings come back unsorted on purpose.</summary>
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, List<string>> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _directories = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Broken { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem(params string[] files)
    {
        foreach (string file in files)
            AddFile(file);
    }

    public void AddFile(string file)
    {
        string directory = Path.GetDirectoryName(file)!;
        if (!_files.TryGetValue(directory, out var list))
            _files[directory] = list = new List<string>();
        list.Insert(0, file);

        // Register every ancestor folder with its parent
        string child = directory;
        string? parent = Path.GetDirectoryName(child);
        while (parent is not null)
        {
            if (!_directories.TryGetValue(parent, out var set))
                _directories[parent] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(child);
            child = parent;
            parent = Path.GetDirectoryName(child);
        }
    }

    public IEnumerable<string> GetFiles(string directory)
    {
        if (Broken.Contains(directory)) throw new UnauthorizedAccessException(directory);
        return _files.TryGetValue(directory, out var list) ? list.ToList() : [];
    }

    public IEnumerable<string> GetDirectories(string directory)
    {
        if (Broken.Contains(directory)) throw new UnauthorizedAccessException(directory);
        return _directories.TryGetValue(directory, out var set) ? set.Reverse().ToList() : [];
    }
}
