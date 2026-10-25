using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public class MediaLibraryTests
{
    private static MediaLibrary LibraryWith(params string[] files)
    {
        var library = new MediaLibrary(new Random(42));
        foreach (string file in files)
            library.Add(file);
        return library;
    }

    [Fact]
    public void Shows_everything_once_before_repeating()
    {
        string[] files = Enumerable.Range(0, 50).Select(i => $@"C:\p\{i}.jpg").ToArray();
        var library = LibraryWith(files);

        var firstRound = Enumerable.Range(0, 50).Select(_ => library.DrawRandom(VideoMode.Mixed)).ToList();
        var secondRound = Enumerable.Range(0, 50).Select(_ => library.DrawRandom(VideoMode.Mixed)).ToList();

        Assert.Equal(files.Order(), firstRound.Order());
        Assert.Equal(files.Order(), secondRound.Order());
        Assert.NotEqual(files, firstRound); // shuffled, not in order
    }

    [Fact]
    public void Honours_the_video_mode()
    {
        var library = LibraryWith(@"C:\p\a.jpg", @"C:\p\b.png", @"C:\p\c.mp4", @"C:\p\d.avi", @"C:\p\e.txt");

        var photos = Enumerable.Range(0, 10).Select(_ => library.DrawRandom(VideoMode.PhotosOnly)).Distinct().Order();
        var videos = Enumerable.Range(0, 10).Select(_ => library.DrawRandom(VideoMode.VideosOnly)).Distinct().Order();

        Assert.Equal([@"C:\p\a.jpg", @"C:\p\b.png"], photos);
        Assert.Equal([@"C:\p\c.mp4", @"C:\p\d.avi"], videos);
        Assert.Equal(2, library.ImageCount);
        Assert.Equal(2, library.VideoCount);
    }

    [Fact]
    public void Returns_null_until_something_is_found()
    {
        var library = LibraryWith(@"C:\p\a.jpg");

        Assert.Null(library.DrawRandom(VideoMode.VideosOnly));
        Assert.Null(new MediaLibrary().DrawRandom(VideoMode.Mixed));
    }

    [Fact]
    public void Never_draws_a_bad_file()
    {
        var library = LibraryWith(@"C:\p\a.jpg", @"C:\p\b.jpg", @"C:\p\c.jpg");
        library.MarkBad(@"C:\p\b.jpg");

        var drawn = Enumerable.Range(0, 20).Select(_ => library.DrawRandom(VideoMode.Mixed)).ToList();

        Assert.DoesNotContain(@"C:\p\b.jpg", drawn);
        Assert.All(drawn, Assert.NotNull);
    }

    [Fact]
    public void Returns_null_when_every_file_is_bad()
    {
        var library = LibraryWith(@"C:\p\a.jpg", @"C:\p\b.jpg");
        library.MarkBad(@"C:\p\a.jpg");
        library.MarkBad(@"C:\p\b.jpg");

        Assert.Null(library.DrawRandom(VideoMode.Mixed));
    }

    [Fact]
    public void Scanner_finds_media_in_every_folder_and_skips_system_folders()
    {
        var tree = new FakeFileSystem(
            @"C:\p\a.jpg", @"C:\p\x\b.mp4", @"C:\p\x\y\c.png", @"C:\p\x\readme.txt",
            @"C:\p\$RECYCLE.BIN\d.jpg", @"C:\p\broken\e.jpg");
        tree.Broken.Add(@"C:\p\broken");
        var library = new MediaLibrary();
        var errors = new List<string>();

        MediaScanner.Scan(@"C:\p", tree, library, new Random(1), CancellationToken.None, (dir, _) => errors.Add(dir));

        Assert.True(library.ScanComplete);
        Assert.Equal(2, library.ImageCount);
        Assert.Equal(1, library.VideoCount);
        Assert.Equal([@"C:\p\broken"], errors);
    }
}
