using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

/// <summary>Videos-only mode: the W key, going back, and folders with no videos.</summary>
public class VideosOnlyTests
{
    private const string Root = @"C:\Pics";

    private static readonly FakeFileSystem Tree = new(
        @"C:\Pics\1.jpg", @"C:\Pics\2.mp4", @"C:\Pics\3.jpg", @"C:\Pics\4.jpg",
        @"C:\Pics\Trip\5.mov", @"C:\Pics\Trip\6.jpg");

    private static readonly FakeFileSystem PhotosOnlyTree = new(@"C:\Pics\1.jpg", @"C:\Pics\Sub\2.png");

    private static Playlist Sequential(FakeFileSystem tree, MediaLibrary? library = null, string? resumeAfter = null) =>
        new(library ?? new MediaLibrary(), start => new SequentialWalker(tree, Root, start), random: false, resumeAfter);

    // ---- The W key ----

    [Theory]
    [InlineData(VideoMode.Mixed, VideoMode.VideosOnly)]
    [InlineData(VideoMode.PhotosOnly, VideoMode.VideosOnly)]
    [InlineData(VideoMode.VideosOnly, VideoMode.Mixed)]
    public void W_goes_to_videos_only_and_back_to_the_mixed_show(VideoMode from, VideoMode expected) =>
        Assert.Equal(expected, from.ToggleVideosOnly());

    [Fact]
    public void W_maps_to_the_toggle_and_is_in_the_help()
    {
        Assert.Equal(Command.ToggleVideosOnly, KeyMap.Map("W").Command);
        Assert.Contains(KeyMap.Help, row => row.Keys == "W" && row.Description.Contains("Videos only"));
    }

    // ---- Folder order ----

    [Fact]
    public void Folder_order_shows_only_the_videos_and_carries_on_from_the_photo_on_screen()
    {
        var playlist = Sequential(Tree);
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\2.mp4", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed));

        // Turned on while 3.jpg is up: the next video after it, then wrap to the first
        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\2.mp4", playlist.Next(VideoMode.VideosOnly));

        // Turned off: the mixed show carries on from the video on screen
        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Folder_order_resumes_from_the_last_run_with_the_next_video()
    {
        var playlist = Sequential(Tree, resumeAfter: @"C:\Pics\3.jpg");

        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly));
    }

    [Fact]
    public void Folder_order_with_no_videos_ends_instead_of_spinning_and_the_mixed_show_still_works()
    {
        var playlist = Sequential(PhotosOnlyTree);

        Assert.Null(playlist.Next(VideoMode.VideosOnly));
        Assert.Null(playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
        Assert.Equal(@"C:\Pics\Sub\2.png", playlist.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Walker_starts_afresh_after_a_pass_with_nothing_for_the_mode()
    {
        var walker = new SequentialWalker(PhotosOnlyTree, Root);

        Assert.Null(walker.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\1.jpg", walker.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Folder_order_skips_hidden_videos_and_ends_when_every_video_is_hidden()
    {
        var hidden = new HiddenFiles(paths: [@"C:\Pics\2.mp4"]);
        var library = new MediaLibrary(hidden: hidden);
        var playlist = Sequential(Tree, library);

        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly)); // the only one left

        playlist.Hide(@"C:\Pics\Trip\5.mov");
        Assert.Null(playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\3.jpg", playlist.Next(VideoMode.Mixed)); // carries on from where the walk stopped, after 2.mp4
    }

    [Fact]
    public void Folder_order_skips_files_that_failed_this_run()
    {
        var playlist = Sequential(Tree);
        playlist.MarkBad(@"C:\Pics\2.mp4");

        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\Trip\5.mov", playlist.Next(VideoMode.VideosOnly));
    }

    // ---- Random order ----

    [Fact]
    public void Random_draws_only_videos_and_leaves_out_hidden_ones()
    {
        var library = new MediaLibrary(new Random(7), new HiddenFiles(paths: [@"C:\Pics\Trip\5.mov"]));
        foreach (string file in new[] { @"C:\Pics\1.jpg", @"C:\Pics\2.mp4", @"C:\Pics\3.jpg", @"C:\Pics\Trip\5.mov", @"C:\Pics\7.mkv" })
            library.Add(file);
        var playlist = new Playlist(library, start => new SequentialWalker(Tree, Root, start), random: true);

        var drawn = Enumerable.Range(0, 20).Select(_ => playlist.Next(VideoMode.VideosOnly)).ToList();

        Assert.All(drawn, path => Assert.Equal(MediaKind.Video, MediaFiles.Classify(path!)));
        Assert.Equal([@"C:\Pics\2.mp4", @"C:\Pics\7.mkv"], drawn.Select(path => path!).Distinct().Order().ToList());
    }

    [Fact]
    public void Random_with_no_videos_draws_nothing()
    {
        var library = new MediaLibrary(new Random(1));
        library.Add(@"C:\Pics\1.jpg");
        var playlist = new Playlist(library, start => new SequentialWalker(Tree, Root, start), random: true);

        Assert.Null(playlist.Next(VideoMode.VideosOnly));
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Next(VideoMode.Mixed));
    }

    // ---- Going back ----

    [Fact]
    public void Back_in_videos_only_skips_the_photos_shown_before()
    {
        var playlist = Sequential(Tree);
        for (int i = 0; i < 5; i++)
            playlist.Next(VideoMode.Mixed); // 1.jpg 2.mp4 3.jpg 4.jpg 5.mov

        Assert.Equal(@"C:\Pics\2.mp4", playlist.Previous(VideoMode.VideosOnly));
        Assert.Null(playlist.Previous(VideoMode.VideosOnly)); // no earlier video
        Assert.Equal(@"C:\Pics\2.mp4", playlist.Current); // stays put
        Assert.Equal(@"C:\Pics\1.jpg", playlist.Previous(VideoMode.Mixed));
    }

    [Fact]
    public void History_back_with_a_test_stays_put_when_nothing_passes()
    {
        var history = new History();
        foreach (string item in new[] { "a", "b", "c" })
            history.Push(item);

        Assert.Null(history.Back(item => item == "z"));
        Assert.Equal("c", history.Current);
        Assert.Equal("a", history.Back(item => item == "a"));
        Assert.Null(history.Back(_ => true)); // nothing before the first
        Assert.Equal("a", history.Current);
    }

    // ---- Nothing to show ----

    [Theory]
    [InlineData(VideoMode.VideosOnly, true, true, false, EmptyShowAction.KeepLooking)]
    [InlineData(VideoMode.Mixed, true, true, false, EmptyShowAction.KeepLooking)]
    [InlineData(VideoMode.VideosOnly, true, true, true, EmptyShowAction.FallBackToMixed)]
    [InlineData(VideoMode.VideosOnly, true, false, false, EmptyShowAction.FallBackToMixed)] // folder order walked it all
    [InlineData(VideoMode.PhotosOnly, true, false, true, EmptyShowAction.FallBackToMixed)]
    [InlineData(VideoMode.VideosOnly, false, false, true, EmptyShowAction.NothingAtAll)] // already fell back
    [InlineData(VideoMode.Mixed, true, false, true, EmptyShowAction.NothingAtAll)]
    public void Decides_what_to_do_with_nothing_to_show(VideoMode mode, bool canFallBack, bool random, bool scanComplete, EmptyShowAction expected) =>
        Assert.Equal(expected, EmptyShow.Decide(mode, canFallBack, random, scanComplete));

    [Fact]
    public void Fallback_message_names_what_was_missing() =>
        Assert.Equal("No videos here, showing photos and videos", EmptyShow.FallbackMessage(VideoMode.VideosOnly));
}
