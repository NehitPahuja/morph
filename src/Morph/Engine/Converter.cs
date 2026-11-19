using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Morph.Engine;

/// <summary>The single entry point: converts one file and returns the file or folder it wrote.</summary>
public static class Converter
{
    public static async Task<string> ConvertAsync(
        string input, OutputFormat format, Quality quality, string? folder,
        IProgress<double>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(input)) throw new MorphException("The file is gone");
        var kind = Formats.KindOf(input) ?? throw new MorphException("Morph doesn't know this kind of file");
        var allowed = Formats.CategoriesFor([kind]);
        if (!allowed.Contains(format.Category))
        {
            var what = kind switch { SourceKind.Audio => "An audio file", SourceKind.Video => "A video", SourceKind.Image => "An image", _ => "A PDF" };
            throw new MorphException($"{what} can't be converted to {format.Label}");
        }

        var dir = Output.FolderFor(input, folder);
        var baseName = Path.GetFileNameWithoutExtension(input);

        return format.Category switch
        {
            Category.Audio or Category.Video => await MediaConverter.ConvertAsync(input, format, quality, dir, baseName, progress, ct),
            Category.Image => await ImageConverter.ConvertAsync(input, format, quality, dir, baseName, ct),
            _ => await PdfPages.ConvertAsync(input, format, quality, dir, baseName, progress, ct),
        };
    }
}
