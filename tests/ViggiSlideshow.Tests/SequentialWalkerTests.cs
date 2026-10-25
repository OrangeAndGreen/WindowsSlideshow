using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public class SequentialWalkerTests
{
    private const string Root = @"C:\Pics";

    private static readonly FakeFileSystem Tree = new(
        @"C:\Pics\a.jpg",
        @"C:\Pics\b.mp4",
        @"C:\Pics\notes.txt",
        @"C:\Pics\2019\IMG_10.jpg",
        @"C:\Pics\2019\IMG_2.jpg",
        @"C:\Pics\2019\Beach\clip.mov",
        @"C:\Pics\2020\x.png");

    private static List<string> Take(SequentialWalker walker, int count, VideoMode mode = VideoMode.Mixed) =>
        Enumerable.Range(0, count).Select(_ => walker.Next(mode)!).ToList();

    [Fact]
    public void Walks_files_then_folders_in_natural_order_and_wraps()
    {
        var walker = new SequentialWalker(Tree, Root);

        Assert.Equal(
        [
            @"C:\Pics\a.jpg", @"C:\Pics\b.mp4",
            @"C:\Pics\2019\IMG_2.jpg", @"C:\Pics\2019\IMG_10.jpg",
            @"C:\Pics\2019\Beach\clip.mov",
            @"C:\Pics\2020\x.png",
            @"C:\Pics\a.jpg",
        ], Take(walker, 7));
    }

    [Fact]
    public void Skips_what_the_mode_hides()
    {
        var walker = new SequentialWalker(Tree, Root);

        Assert.Equal([@"C:\Pics\b.mp4", @"C:\Pics\2019\Beach\clip.mov", @"C:\Pics\b.mp4"],
            Take(walker, 3, VideoMode.VideosOnly));
    }

    [Fact]
    public void Starts_after_a_file_deep_in_the_tree()
    {
        var walker = new SequentialWalker(Tree, Root, @"C:\Pics\2019\IMG_2.jpg");

        Assert.Equal([@"C:\Pics\2019\IMG_10.jpg", @"C:\Pics\2019\Beach\clip.mov", @"C:\Pics\2020\x.png", @"C:\Pics\a.jpg"],
            Take(walker, 4));
    }

    [Fact]
    public void Starts_where_a_deleted_file_would_have_been()
    {
        var walker = new SequentialWalker(Tree, Root, @"C:\Pics\2019\IMG_5.jpg");

        Assert.Equal(@"C:\Pics\2019\IMG_10.jpg", walker.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Starts_after_a_missing_folder_at_the_next_one()
    {
        var walker = new SequentialWalker(Tree, Root, @"C:\Pics\2019b\gone.jpg");

        Assert.Equal(@"C:\Pics\2020\x.png", walker.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Starts_at_the_beginning_for_a_file_outside_the_root()
    {
        var walker = new SequentialWalker(Tree, Root, @"D:\Elsewhere\z.jpg");

        Assert.Equal(@"C:\Pics\a.jpg", walker.Next(VideoMode.Mixed));
    }

    [Fact]
    public void Returns_null_when_nothing_matches()
    {
        var walker = new SequentialWalker(new FakeFileSystem(@"C:\Pics\a.jpg", @"C:\Pics\b\c.png"), Root, @"C:\Pics\a.jpg");

        Assert.Null(walker.Next(VideoMode.VideosOnly));
    }

    [Fact]
    public void Carries_on_past_an_unreadable_folder()
    {
        var tree = new FakeFileSystem(@"C:\Pics\a\1.jpg", @"C:\Pics\b\2.jpg", @"C:\Pics\c\3.jpg");
        tree.Broken.Add(@"C:\Pics\b");
        var walker = new SequentialWalker(tree, Root);

        Assert.Equal([@"C:\Pics\a\1.jpg", @"C:\Pics\c\3.jpg"], Take(walker, 2));
    }
}
