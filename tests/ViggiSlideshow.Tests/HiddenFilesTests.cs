using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public sealed class HiddenFilesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ViggiSlideshowTests", Guid.NewGuid().ToString("N"));

    private string ListPath => Path.Combine(_folder, HiddenFiles.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Parse_skips_blanks_and_comments_and_tidies_paths()
    {
        string[] lines =
        [
            "# hidden by X",
            "",
            "   ",
            @"  C:\Pics\a.jpg  ",
            "\"C:\\Pics\\b.jpg\"",
            "C:/Pics/sub/c.jpg",
        ];

        Assert.Equal([@"C:\Pics\a.jpg", @"C:\Pics\b.jpg", @"C:\Pics\sub\c.jpg"], HiddenFiles.Parse(lines));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        var hidden = new HiddenFiles(paths: [@"C:\Pics\Holiday\IMG_0001.JPG"]);

        Assert.True(hidden.Contains(@"c:\pics\holiday\img_0001.jpg"));
        Assert.False(hidden.Contains(@"C:\Pics\Holiday\IMG_0002.JPG"));
        Assert.False(hidden.Contains(""));
    }

    [Fact]
    public void Add_saves_one_path_per_line_and_loads_back()
    {
        var hidden = HiddenFiles.Load(ListPath); // no file yet, so the folder is made on first save
        Assert.Equal(0, hidden.Count);

        Assert.True(hidden.Add(@"C:\Pics\a.jpg"));
        Assert.True(hidden.Add(@"C:\Pics\b.mp4"));
        Assert.False(hidden.Add(@"c:\PICS\A.JPG")); // already hidden

        Assert.Equal([@"C:\Pics\a.jpg", @"C:\Pics\b.mp4"], File.ReadAllLines(ListPath));

        var reloaded = HiddenFiles.Load(ListPath);
        Assert.Equal(2, reloaded.Count);
        Assert.True(reloaded.Contains(@"C:\Pics\B.MP4"));
    }

    [Fact]
    public void Add_starts_a_new_line_after_a_hand_edit_without_one()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(ListPath, "# mine\r\nC:\\Pics\\a.jpg");

        var hidden = HiddenFiles.Load(ListPath);
        hidden.Add(@"C:\Pics\b.jpg");

        Assert.Equal(["# mine", @"C:\Pics\a.jpg", @"C:\Pics\b.jpg"], File.ReadAllLines(ListPath));
    }

    [Fact]
    public void In_memory_list_never_touches_disk()
    {
        var hidden = new HiddenFiles();

        Assert.True(hidden.Add(@"C:\Pics\a.jpg"));
        Assert.True(hidden.Contains(@"C:\Pics\a.jpg"));
        Assert.Null(hidden.FilePath);
    }

    [Fact]
    public void The_library_leaves_hidden_files_out_of_the_scan()
    {
        var library = new MediaLibrary(new Random(1), new HiddenFiles(paths: [@"C:\p\b.jpg"]));
        library.Add(@"C:\p\a.jpg");
        library.Add(@"C:\P\B.JPG");

        Assert.Equal(1, library.ImageCount);
        Assert.All(Enumerable.Range(0, 5), _ => Assert.Equal(@"C:\p\a.jpg", library.DrawRandom(VideoMode.Mixed)));
    }

    [Fact]
    public void Hiding_takes_a_file_out_of_the_shuffle_at_once()
    {
        var library = new MediaLibrary(new Random(1));
        library.Add(@"C:\p\a.jpg");
        library.Add(@"C:\p\b.jpg");

        Assert.True(library.Hide(@"C:\p\b.jpg"));
        Assert.False(library.Hide(@"C:\p\b.jpg"));

        Assert.All(Enumerable.Range(0, 6), _ => Assert.Equal(@"C:\p\a.jpg", library.DrawRandom(VideoMode.Mixed)));
    }

    [Fact]
    public void Folder_order_skips_files_hidden_on_an_earlier_run()
    {
        var tree = new FakeFileSystem(@"C:\Pics\1.jpg", @"C:\Pics\2.jpg", @"C:\Pics\3.jpg");
        var library = new MediaLibrary(hidden: new HiddenFiles(paths: [@"c:\pics\2.JPG"]));
        var playlist = new Playlist(library, start => new SequentialWalker(tree, @"C:\Pics", start), random: false);

        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Hiding_in_folder_order_drops_it_from_history_and_moves_on()
    {
        var tree = new FakeFileSystem(@"C:\Pics\1.jpg", @"C:\Pics\2.jpg", @"C:\Pics\3.jpg");
        var playlist = new Playlist(new MediaLibrary(), start => new SequentialWalker(tree, @"C:\Pics", start), random: false);
        playlist.Next(VideoMode.Mixed);
        playlist.Next(VideoMode.Mixed);

        Assert.True(playlist.Hide(@"C:\Pics\2.jpg"));

        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Previous());
        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed)); // replayed from history
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed)); // wrapped, 2.jpg left out
    }

    [Fact]
    public void The_x_key_hides_the_file()
    {
        Assert.Equal(Command.HideFile, KeyMap.Map("X").Command);
        Assert.Contains(KeyMap.Help, row => row.Keys == "X");
    }
}
