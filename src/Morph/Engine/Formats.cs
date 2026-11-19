using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Morph.Engine;

/// <summary>What a source file is, decided by its extension.</summary>
public enum SourceKind { Audio, Video, Image, Pdf }

/// <summary>A family of output formats; it becomes one tab in the window.</summary>
public enum Category { Video, Audio, Image, Pages }

/// <summary>One preset applied to every format: smaller files, a middle ground, or the best result.</summary>
public enum Quality { Small, Balanced, Best }

/// <param name="Id">Stable identifier, used on the command line and in settings.</param>
/// <param name="Encoder">The ffmpeg encoder this format needs, or null when it is written without ffmpeg.</param>
public sealed record OutputFormat(string Id, string Label, string Ext, Category Category, string Blurb, string? Encoder);

public static class Formats
{
    static readonly Dictionary<string, SourceKind> ByExtension = Build();

    static Dictionary<string, SourceKind> Build()
    {
        var map = new Dictionary<string, SourceKind>(StringComparer.OrdinalIgnoreCase);
        void Add(SourceKind kind, string list)
        {
            foreach (var ext in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) map[ext] = kind;
        }
        Add(SourceKind.Audio, "mp3 wav flac m4a aac ogg oga opus wma aiff aif aifc ape wv amr ac3 dts mka caf mp2 m4b weba");
        Add(SourceKind.Video, "mp4 m4v mov mkv webm avi wmv flv mpg mpeg ts mts m2ts 3gp ogv vob mxf");
        Add(SourceKind.Image, "jpg jpeg jfif jpe png gif bmp dib tif tiff webp heic heif avif ico jxr wdp");
        Add(SourceKind.Pdf, "pdf");
        return map;
    }

    /// <summary>Every extension Morph accepts, for the Explorer menu and the open dialog.</summary>
    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;

    public static SourceKind? KindOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        return ByExtension.TryGetValue(ext, out var kind) ? kind : null;
    }

    public static readonly IReadOnlyList<OutputFormat> All =
    [
        new("mp4", "MP4", "mp4", Category.Video, "H.264, plays anywhere", "libx264"),
        new("webm", "WebM", "webm", Category.Video, "VP9 + Opus for the web", "libvpx-vp9"),
        new("mkv", "MKV", "mkv", Category.Video, "H.264 in Matroska", "libx264"),
        new("mov", "MOV", "mov", Category.Video, "H.264 for editors", "libx264"),
        new("anim-gif", "GIF", "gif", Category.Video, "Silent looping clip", "gif"),

        new("mp3", "MP3", "mp3", Category.Audio, "Works on everything", "libmp3lame"),
        new("m4a", "AAC", "m4a", Category.Audio, "Compact, phone-friendly", "aac"),
        new("opus", "Opus", "opus", Category.Audio, "Smallest at good quality", "libopus"),
        new("ogg", "OGG", "ogg", Category.Audio, "Open Vorbis audio", "libvorbis"),
        new("flac", "FLAC", "flac", Category.Audio, "Lossless, compressed", "flac"),
        new("wav", "WAV", "wav", Category.Audio, "Uncompressed, for editing", "pcm_s16le"),

        new("jpg", "JPG", "jpg", Category.Image, "Photos, small files", null),
        new("png", "PNG", "png", Category.Image, "Lossless, keeps transparency", null),
        new("webp", "WebP", "webp", Category.Image, "Modern web images", "libwebp"),
        new("avif", "AVIF", "avif", Category.Image, "Smallest modern format", "libaom-av1"),
        new("bmp", "BMP", "bmp", Category.Image, "Plain bitmap", null),
        new("tiff", "TIFF", "tiff", Category.Image, "For print and archives", null),
        new("still-gif", "GIF", "gif", Category.Image, "256 colours, one frame", null),
        new("ico", "ICO", "ico", Category.Image, "Windows icon, max 256 px", "bmp"),

        new("pages-png", "PNG", "png", Category.Pages, "One image per page", null),
        new("pages-jpg", "JPG", "jpg", Category.Pages, "One image per page, smaller", null),
    ];

    public static OutputFormat? ById(string id) => All.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The categories every one of these kinds can be converted to, in tab order.</summary>
    public static IReadOnlyList<Category> CategoriesFor(IEnumerable<SourceKind> kinds)
    {
        HashSet<Category>? common = null;
        foreach (var kind in kinds.Distinct())
        {
            var mine = kind switch
            {
                SourceKind.Audio => new HashSet<Category> { Category.Audio },
                SourceKind.Video => new HashSet<Category> { Category.Video, Category.Audio },
                SourceKind.Image => new HashSet<Category> { Category.Image },
                _ => new HashSet<Category> { Category.Pages },
            };
            if (common == null) common = mine;
            else common.IntersectWith(mine);
        }
        if (common == null) return [];
        return Enum.GetValues<Category>().Where(common.Contains).ToList();
    }

    /// <summary>Whether this format can be produced right now (its ffmpeg encoder is installed).</summary>
    public static bool IsAvailable(OutputFormat f) => f.Encoder == null || Tools.HasEncoder(f.Encoder);

    public static string CategoryName(Category c) => c switch
    {
        Category.Video => "Video",
        Category.Audio => "Audio",
        Category.Image => "Image",
        _ => "Page images",
    };

    /// <summary>One line saying what the quality preset means for this category.</summary>
    public static string QualityHint(Category c, Quality q) => (c, q) switch
    {
        (Category.Video, Quality.Small) => "Up to 720p, compact file",
        (Category.Video, Quality.Balanced) => "Original resolution, good quality",
        (Category.Video, Quality.Best) => "Original resolution, near-lossless, slower",
        (Category.Audio, Quality.Small) => "About 128 kbps (FLAC and WAV are always lossless)",
        (Category.Audio, Quality.Balanced) => "About 192 kbps (FLAC and WAV are always lossless)",
        (Category.Audio, Quality.Best) => "About 256-320 kbps (FLAC and WAV are always lossless)",
        (Category.Image, Quality.Small) => "Longest side up to 1920 px, stronger compression",
        (Category.Image, Quality.Balanced) => "Original size, good compression",
        (Category.Image, Quality.Best) => "Original size, highest quality",
        (Category.Pages, Quality.Small) => "96 dpi, screen size",
        (Category.Pages, Quality.Balanced) => "150 dpi, sharp on screen",
        _ => "300 dpi, print quality",
    };
}
