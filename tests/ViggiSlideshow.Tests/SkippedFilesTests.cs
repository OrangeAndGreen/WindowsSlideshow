using ViggiSlideshow.Core;

namespace ViggiSlideshow.Tests;

public sealed class SkippedFilesTests : IDisposable
{
    private static readonly DateTime Monday = new(2026, 10, 26, 22, 15, 3);
    private static readonly DateTime Tuesday = new(2026, 10, 27, 1, 2, 9);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ViggiSlideshowTests", Guid.NewGuid().ToString("N"));

    private string ListPath => Path.Combine(_folder, SkippedFiles.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_first_skip_starts_a_record()
    {
        var record = SkippedFiles.Merge(null, @"C:\Pics\a.mp4", "VLC error", Monday);

        Assert.Equal(new SkippedFile(@"C:\Pics\a.mp4", "VLC error", 1, Monday, Monday), record);
    }

    [Fact]
    public void A_later_skip_counts_up_and_keeps_the_latest_reason()
    {
        var first = SkippedFiles.Merge(null, @"C:\Pics\a.mp4", "VLC error", Monday);

        var second = SkippedFiles.Merge(first, @"C:\Pics\a.mp4", "stalled", Tuesday);

        Assert.Equal(new SkippedFile(@"C:\Pics\a.mp4", "stalled", 2, Monday, Tuesday), second);
    }

    [Fact]
    public void Format_then_parse_round_trips()
    {
        SkippedFile[] records =
        [
            new(@"C:\Pics\b.jpg", "took too long to load", 3, Monday, Tuesday),
            new(@"C:\Pics\a.mp4", "VLC error", 1, Tuesday, Tuesday),
        ];

        var parsed = SkippedFiles.Parse(SkippedFiles.Format(records)).ToList();

        Assert.Equal([records[1], records[0]], parsed); // sorted by path
    }

    [Fact]
    public void Format_writes_a_header_and_tab_separated_lines_in_natural_path_order()
    {
        var lines = SkippedFiles.Format(
        [
            new(@"C:\Pics\10.jpg", "bad", 1, Monday, Monday),
            new(@"C:\Pics\9.jpg", "bad", 2, Monday, Tuesday),
        ]).ToList();

        Assert.All(lines.Take(2), line => Assert.StartsWith("#", line));
        Assert.Equal("2\t2026-10-26 22:15:03\t2026-10-27 01:02:09\tC:\\Pics\\9.jpg\tbad", lines[2]);
        Assert.EndsWith(@"C:\Pics\10.jpg" + "\tbad", lines[3]);
    }

    [Fact]
    public void Reasons_lose_their_tabs_and_line_breaks()
    {
        var lines = SkippedFiles.Update([], @"C:\Pics\a.jpg", "NotSupportedException:\tNo imaging\r\ncomponent ", Monday).ToList();

        Assert.Equal("NotSupportedException: No imaging  component", SkippedFiles.Parse(lines).Single().Reason);
    }

    [Fact]
    public void Parse_skips_comments_blanks_and_lines_it_cant_read()
    {
        string[] lines =
        [
            "# a note",
            "",
            "2\t2026-10-26 22:15:03\t2026-10-27 01:02:09\tC:\\Pics\\a.jpg\tbad",
            "not a record",
            "x\t2026-10-26 22:15:03\t2026-10-27 01:02:09\tC:\\Pics\\b.jpg\tbad",
            "1\tyesterday\t2026-10-27 01:02:09\tC:\\Pics\\c.jpg\tbad",
            "1\t2026-10-26 22:15:03\t2026-10-27 01:02:09\t  \tbad",
            "1\t2026-10-26 22:15:03\t2026-10-27 01:02:09\tC:\\Pics\\d.jpg",
        ];

        var parsed = SkippedFiles.Parse(lines).ToList();

        Assert.Equal([@"C:\Pics\a.jpg", @"C:\Pics\d.jpg"], parsed.Select(r => r.Path));
    }

    [Fact]
    public void Update_matches_paths_whatever_their_case_and_leaves_others_alone()
    {
        var lines = SkippedFiles.Update([], @"C:\Pics\a.jpg", "bad", Monday);
        lines = SkippedFiles.Update(lines, @"C:\Pics\b.jpg", "worse", Monday);
        lines = SkippedFiles.Update(lines, @"c:\pics\A.JPG", "still bad", Tuesday);

        var parsed = SkippedFiles.Parse(lines).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal(new SkippedFile(@"C:\Pics\a.jpg", "still bad", 2, Monday, Tuesday), parsed[0]);
        Assert.Equal(new SkippedFile(@"C:\Pics\b.jpg", "worse", 1, Monday, Monday), parsed[1]);
    }

    [Fact]
    public void Update_folds_a_path_listed_twice_into_one_line()
    {
        string[] lines =
        [
            "2\t2026-10-26 22:15:03\t2026-10-26 22:15:03\tC:\\Pics\\a.jpg\told",
            "1\t2026-10-27 01:02:09\t2026-10-27 01:02:09\tC:\\PICS\\A.jpg\tnewer",
        ];

        var record = SkippedFiles.Parse(SkippedFiles.Update(lines, @"C:\Pics\a.jpg", "newest", Tuesday.AddHours(1))).Single();

        Assert.Equal(4, record.Count);
        Assert.Equal(Monday, record.FirstSeen);
        Assert.Equal("newest", record.Reason);
    }

    [Fact]
    public void Record_saves_to_disk_and_counts_across_instances()
    {
        new SkippedFiles(ListPath).Record(@"C:\Pics\a.mp4", "VLC error", Monday); // makes the folder
        new SkippedFiles(ListPath).Record(@"C:\Pics\a.mp4", "stalled", Tuesday);

        var record = new SkippedFiles(ListPath).Load().Single();
        Assert.Equal(new SkippedFile(@"C:\Pics\a.mp4", "stalled", 2, Monday, Tuesday), record);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Record_from_many_threads_loses_no_counts()
    {
        var first = new SkippedFiles(ListPath);
        var second = new SkippedFiles(ListPath); // as if another screensaver process

        Parallel.For(0, 40, i => (i % 2 == 0 ? first : second).Record($@"C:\Pics\{i % 4}.jpg", "bad"));

        var records = first.Load();
        Assert.Equal(4, records.Count);
        Assert.All(records, r => Assert.Equal(10, r.Count));
    }

    [Fact]
    public void A_missing_list_loads_empty()
    {
        Assert.Empty(new SkippedFiles(ListPath).Load());
    }
}
