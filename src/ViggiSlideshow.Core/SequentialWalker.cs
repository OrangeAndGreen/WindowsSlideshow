namespace ViggiSlideshow.Core;

/// <summary>
/// Steps through the folder tree in Explorer order (a folder's files, then its subfolders,
/// each sorted naturally), reading folders only as it reaches them. It can start just after
/// a given file, which is how sequential mode picks up where the last run, or the random
/// picture on screen, left off. At the end it wraps back to the start.
/// </summary>
public sealed class SequentialWalker
{
    private sealed class Frame
    {
        public required string Directory;
        public required string[] Files;
        public required string[] Subdirectories;
        public int FileIndex;
        public int SubdirectoryIndex;
    }

    private readonly IFileSystem _fileSystem;
    private readonly string _root;
    private readonly Stack<Frame> _stack = new();

    // True once a pass has begun at the root, so a whole pass with no match means there is none
    private bool _passFromRoot;
    private bool _foundThisPass;

    public SequentialWalker(IFileSystem fileSystem, string root, string? startAfter = null)
    {
        _fileSystem = fileSystem;
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (startAfter is null || !PositionAfter(Path.GetFullPath(startAfter)))
        {
            _stack.Clear();
            _stack.Push(Read(_root));
            _passFromRoot = true;
        }
    }

    /// <summary>The next file the mode allows, or null when the tree has none.</summary>
    public string? Next(VideoMode mode)
    {
        while (true)
        {
            if (_stack.Count == 0)
            {
                if (_passFromRoot && !_foundThisPass)
                    return null;
                _stack.Push(Read(_root));
                _passFromRoot = true;
                _foundThisPass = false;
            }

            Frame frame = _stack.Peek();
            if (frame.FileIndex < frame.Files.Length)
            {
                string file = frame.Files[frame.FileIndex++];
                if (MediaFiles.Matches(MediaFiles.Classify(file), mode))
                {
                    _foundThisPass = true;
                    return file;
                }
            }
            else if (frame.SubdirectoryIndex < frame.Subdirectories.Length)
            {
                _stack.Push(Read(frame.Subdirectories[frame.SubdirectoryIndex++]));
            }
            else
            {
                _stack.Pop();
            }
        }
    }

    // Builds the stack as if the walk had just returned startAfter. False when it isn't under the root.
    private bool PositionAfter(string startAfter)
    {
        string relative = Path.GetRelativePath(_root, startAfter);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        string[] segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        Frame frame = Read(_root);
        _stack.Push(frame);

        for (int i = 0; i < segments.Length - 1; i++)
        {
            // Inside a subfolder, this folder's own files have all come before it
            frame.FileIndex = frame.Files.Length;
            int position = Position(frame.Subdirectories, segments[i], out bool found);
            if (!found)
            {
                frame.SubdirectoryIndex = position;
                return true;
            }
            frame.SubdirectoryIndex = position + 1;
            frame = Read(frame.Subdirectories[position]);
            _stack.Push(frame);
        }

        int filePosition = Position(frame.Files, segments[^1], out bool fileFound);
        frame.FileIndex = fileFound ? filePosition + 1 : filePosition;
        return true;
    }

    private static int Position(string[] paths, string name, out bool found)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            int c = NaturalComparer.Instance.Compare(Path.GetFileName(paths[i]), name);
            if (c == 0 && string.Equals(Path.GetFileName(paths[i]), name, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return i;
            }
            if (c > 0)
            {
                found = false;
                return i;
            }
        }
        found = false;
        return paths.Length;
    }

    private Frame Read(string directory)
    {
        string[] files, subdirectories;
        try
        {
            files = _fileSystem.GetFiles(directory).Where(MediaFiles.IsMedia).ToArray();
            subdirectories = _fileSystem.GetDirectories(directory)
                .Where(d => !MediaScanner.IsSkippedFolder(Path.GetFileName(d))).ToArray();
        }
        catch (Exception)
        {
            files = [];
            subdirectories = [];
        }

        Array.Sort(files, (a, b) => NaturalComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        Array.Sort(subdirectories, (a, b) => NaturalComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));
        return new Frame { Directory = directory, Files = files, Subdirectories = subdirectories };
    }
}
