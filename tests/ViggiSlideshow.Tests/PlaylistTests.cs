using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public class PlaylistTests
{
    private const string Root = @"C:\Pics";

    private static readonly FakeFileSystem Tree = new(
        @"C:\Pics\1.jpg", @"C:\Pics\2.jpg", @"C:\Pics\3.mp4", @"C:\Pics\4.jpg", @"C:\Pics\5.jpg");

    private static Playlist Sequential(string? resumeAfter = null, MediaLibrary? library = null) =>
        new(library ?? new MediaLibrary(), start => new SequentialWalker(Tree, Root, start), random: false, resumeAfter);

    [Fact]
    public void Back_and_forward_replay_history_before_moving_on()
    {
        var playlist = Sequential();
        playlist.Next(VideoMode.Mixed);
        playlist.Next(VideoMode.Mixed);
        playlist.Next(VideoMode.Mixed);

        Assert.Equal(@"C:\Pics\2.jpg", playlist.Previous());
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Previous());
        Assert.Null(playlist.Previous()); // nothing before the first
        Assert.Equal(@"C:\Pics\2.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\3.mp4", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\4.jpg", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Replaying_history_skips_what_the_mode_now_hides()
    {
        var playlist = Sequential();
        for (int i = 0; i < 4; i++) playlist.Next(VideoMode.Mixed);
        playlist.Previous();
        playlist.Previous();
        playlist.Previous(); // back on 1.jpg... 2.jpg, 3.mp4, 4.jpg ahead

        Assert.Equal(@"C:\Pics\2.jpg", playlist.Next(VideoMode.PhotosOnly));
        Assert.Equal(@"C:\Pics\4.jpg", playlist.Next(VideoMode.PhotosOnly));
    }

    [Fact]
    public void Sequential_order_resumes_after_the_last_run()
    {
        var playlist = Sequential(resumeAfter: @"C:\Pics\3.mp4");

        Assert.Equal(@"C:\Pics\4.jpg", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Switching_to_sequential_carries_on_from_the_current_file()
    {
        var library = new MediaLibrary(new Random(3));
        library.Add(@"C:\Pics\2.jpg");
        var playlist = new Playlist(library, start => new SequentialWalker(Tree, Root, start), random: true);

        Assert.Equal(@"C:\Pics\2.jpg", playlist.Next(VideoMode.Mixed));
        playlist.Random = false;
        Assert.Equal(@"C:\Pics\3.mp4", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void A_bad_file_leaves_history_and_is_skipped_in_order()
    {
        var library = new MediaLibrary();
        var playlist = Sequential(library: library);
        playlist.Next(VideoMode.Mixed);
        playlist.Next(VideoMode.Mixed);

        playlist.MarkBad(@"C:\Pics\2.jpg");

        Assert.Equal(@"C:\Pics\1.jpg", playlist.Current);
        Assert.Equal(@"C:\Pics\3.mp4", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\4.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\5.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\3.mp4", playlist.Next(VideoMode.Mixed)); // 2.jpg skipped on the next pass
    }

    [Fact]
    public void History_drops_the_oldest_beyond_its_capacity()
    {
        var history = new History(capacity: 3);
        foreach (string item in new[] { "a", "b", "c", "d" })
            history.Push(item);

        Assert.Equal(3, history.Count);
        Assert.Equal("c", history.Back());
        Assert.Equal("b", history.Back());
        Assert.Equal("b", history.Back()); // "a" is gone
        Assert.Equal(2, history.StepsBack);
    }
}
