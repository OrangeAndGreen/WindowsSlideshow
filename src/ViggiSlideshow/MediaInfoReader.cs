using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using ViggiSlideshow.Core;
using Directory = System.IO.Directory;

namespace ViggiSlideshow;

/// <summary>Reads what the info label shows: size, date taken and camera.</summary>
internal static class MediaInfoReader
{
    public static MediaInfo Read(string path)
    {
        var file = new FileInfo(path);
        MediaKind kind = MediaFiles.Classify(path);

        DateTime? taken = null;
        string? camera = null;

        ReadExif(path, ref taken, ref camera);

        // Old Canon cameras keep a video's EXIF in a .THM thumbnail beside it
        if (kind == MediaKind.Video && taken is null)
        {
            string thumbnail = Path.ChangeExtension(path, ".THM");
            if (File.Exists(thumbnail))
                ReadExif(thumbnail, ref taken, ref camera);
        }

        int? filesInFolder = null;
        try
        {
            filesInFolder = Directory.EnumerateFiles(file.DirectoryName!).Count(MediaFiles.IsMedia);
        }
        catch (Exception)
        {
            // The count is a nicety
        }

        return new MediaInfo
        {
            Path = path,
            Kind = kind,
            Bytes = file.Exists ? file.Length : 0,
            Taken = taken,
            FileDate = file.Exists ? Earliest(file.CreationTime, file.LastWriteTime) : null,
            Camera = camera,
            FilesInFolder = filesInFolder,
        };
    }

    private static void ReadExif(string path, ref DateTime? taken, ref string? camera)
    {
        try
        {
            IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(path);

            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime original))
                taken ??= original;

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null)
                camera ??= LabelText.CameraName(ifd0.GetString(ExifDirectoryBase.TagMake), ifd0.GetString(ExifDirectoryBase.TagModel));

            // Phone and camera videos: the movie header's creation time, which is UTC
            var movie = directories.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
            if (taken is null && movie is not null
                && movie.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out DateTime created)
                && created.Year > 1971)
            {
                taken = DateTime.SpecifyKind(created, DateTimeKind.Utc).ToLocalTime();
            }
        }
        catch (Exception)
        {
            // Plenty of files have no readable metadata
        }
    }

    private static DateTime Earliest(DateTime a, DateTime b) => a < b ? a : b;
}
