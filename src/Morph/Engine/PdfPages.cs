using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Morph.Engine;

/// <summary>PDF → one image per page, rendered by Windows' own PDF engine, into a new folder.</summary>
public static class PdfPages
{
    public static async Task<string> ConvertAsync(
        string input, OutputFormat f, Quality q, string dir, string baseName,
        IProgress<double>? progress, CancellationToken ct)
    {
        PdfDocument doc;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input));
            doc = await PdfDocument.LoadFromFileAsync(file);
        }
        catch (Exception e) when (e.HResult == unchecked((int)0x8007052B))
        {
            throw new MorphException("This PDF is password-protected");
        }
        catch (Exception e)
        {
            var why = string.IsNullOrWhiteSpace(e.Message) ? $"{e.GetType().Name} 0x{e.HResult:X8}" : e.Message.Trim();
            throw new MorphException($"Couldn't open this PDF ({why})");
        }
        if (doc.PageCount == 0) throw new MorphException("This PDF has no pages");

        var dpi = q switch { Quality.Small => 96.0, Quality.Balanced => 150.0, _ => 300.0 };
        var jpg = f.Ext == "jpg";
        var folder = Output.FreeFolder(dir, $"{baseName} pages");
        Directory.CreateDirectory(folder);
        var digits = Math.Max(2, doc.PageCount.ToString().Length);
        try
        {
            for (uint i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var page = doc.GetPage(i);
                // Page size is in 1/96 inch units.
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96)),
                    DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * dpi / 96)),
                };
                if (jpg) options.BitmapEncoderId = BitmapEncoder.JpegEncoderId;
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);

                var bytes = new byte[stream.Size];
                using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    await reader.LoadAsync((uint)stream.Size);
                    reader.ReadBytes(bytes);
                }
                var name = $"page {(i + 1).ToString().PadLeft(digits, '0')}.{f.Ext}";
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, ct);
                progress?.Report((i + 1.0) / doc.PageCount);
            }
        }
        catch
        {
            Output.TryDelete(folder);
            throw;
        }
        return folder;
    }
}
