using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Morph.Engine;

/// <summary>
/// Images. Decoding and the classic formats go through Windows Imaging (WIC), which reads whatever
/// codecs Windows has (HEIC and WebP included when their extensions are installed). WebP, AVIF and ICO
/// are written by ffmpeg from a lossless intermediate PNG. Files WIC can't read are decoded by ffmpeg.
/// </summary>
public static class ImageConverter
{
    static readonly HashSet<string> ViaFfmpeg = ["webp", "avif", "ico"];

    public static async Task<string> ConvertAsync(string input, OutputFormat f, Quality q, string dir, string baseName, CancellationToken ct)
    {
        var output = Output.FreeFile(dir, baseName, f.Ext);
        var temp = Path.Combine(Path.GetTempPath(), "Morph", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var source = input;
            BitmapSource? image;
            try
            {
                image = await Sta.Run(() => Load(source, q, f.Id));
            }
            catch (Exception) when (Tools.FFmpeg != null)
            {
                // WIC can't read it (AVIF without the codec, an odd TIFF…): let ffmpeg decode the first frame.
                source = Path.Combine(temp, "decoded.png");
                await Ffmpeg.RunAsync(["-i", input, "-frames:v", "1", "-update", "1", source], 0, null, ct);
                image = await Sta.Run(() => Load(source, q, f.Id));
            }
            ct.ThrowIfCancellationRequested();

            if (ViaFfmpeg.Contains(f.Id))
            {
                if (!Formats.IsAvailable(f)) throw new MorphException($"This ffmpeg build can't write {f.Label}");
                var png = Path.Combine(temp, "frame.png");
                await Sta.Run(() => { Save(image, new PngBitmapEncoder(), png); return 0; });
                await Ffmpeg.RunAsync(["-i", png, .. EncodeArgs(f.Id, q), output], 0, null, ct);
            }
            else
            {
                await Sta.Run(() => { Save(image, Encoder(f.Id, q), output); return 0; });
            }
            ct.ThrowIfCancellationRequested();
            return output;
        }
        catch
        {
            Output.TryDelete(output);
            throw;
        }
        finally
        {
            Output.TryDelete(temp);
        }
    }

    static List<string> EncodeArgs(string id, Quality q)
    {
        string Pick(string s, string b, string best) => q switch { Quality.Small => s, Quality.Balanced => b, _ => best };
        return id switch
        {
            "webp" => ["-c:v", "libwebp", "-quality", Pick("65", "82", "95"), "-compression_level", "6"],
            // AV1 4:2:0 wants even sides; the picture loses at most one pixel row/column.
            "avif" => ["-c:v", "libaom-av1", "-still-picture", "1", "-crf", Pick("38", "28", "18"), "-cpu-used", "6",
                       "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-pix_fmt", "yuv420p"],
            _ => ["-c:v", "bmp", "-pix_fmt", "bgra"],
        };
    }

    static BitmapEncoder Encoder(string id, Quality q) => id switch
    {
        "jpg" => new JpegBitmapEncoder { QualityLevel = q switch { Quality.Small => 70, Quality.Balanced => 85, _ => 95 } },
        "png" => new PngBitmapEncoder(),
        "bmp" => new BmpBitmapEncoder(),
        "tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
        "still-gif" => new GifBitmapEncoder(),
        _ => throw new MorphException($"Unknown image format {id}"),
    };

    static void Save(BitmapSource image, BitmapEncoder encoder, string path)
    {
        encoder.Frames.Add(BitmapFrame.Create(image));
        // CreateNew: if a file with this name appeared in the meantime, fail instead of replacing it.
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(fs);
    }

    /// <summary>First frame, turned upright, sized for the preset, in a pixel format every encoder takes.</summary>
    static BitmapSource Load(string path, Quality q, string targetId)
    {
        BitmapFrame frame;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new MorphException("This image has no pictures in it");
            frame = decoder.Frames[0];
        }

        BitmapSource img = Upright(frame, Orientation(frame));

        var maxEdge = targetId == "ico" ? 256 : q == Quality.Small ? 1920 : 0;
        var longest = Math.Max(img.PixelWidth, img.PixelHeight);
        if (maxEdge > 0 && longest > maxEdge)
        {
            var s = (double)maxEdge / longest;
            img = new TransformedBitmap(img, new ScaleTransform(s, s));
        }

        var alpha = HasAlpha(img);
        img = new FormatConvertedBitmap(img, alpha ? PixelFormats.Bgra32 : PixelFormats.Bgr24, null, 0);
        // JPG and BMP have no transparency: put see-through parts on white rather than black.
        if (alpha && (targetId == "jpg" || targetId == "bmp")) img = OnWhite(img);
        // Re-tag at 96 dpi so encoders don't rescale based on the source's resolution.
        img = Normalize(img);
        img.Freeze();
        return img;
    }

    static int Orientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.GetQuery("System.Photo.Orientation") is ushort o) return o;
        }
        catch (Exception) { }
        return 1;
    }

    /// <summary>Applies an EXIF orientation (1-8) so the pixels are the right way up.</summary>
    static BitmapSource Upright(BitmapSource img, int orientation)
    {
        // Applied in order; WPF angles are clockwise.
        Transform[] steps = orientation switch
        {
            2 => [new ScaleTransform(-1, 1)],
            3 => [new RotateTransform(180)],
            4 => [new ScaleTransform(1, -1)],
            5 => [new RotateTransform(90), new ScaleTransform(-1, 1)],  // transpose
            6 => [new RotateTransform(90)],
            7 => [new RotateTransform(90), new ScaleTransform(1, -1)],  // transverse
            8 => [new RotateTransform(270)],
            _ => [],
        };
        if (steps.Length == 0) return img;
        var group = new TransformGroup();
        foreach (var s in steps) group.Children.Add(s);
        return new TransformedBitmap(img, group);
    }

    static bool HasAlpha(BitmapSource img)
    {
        var f = img.Format;
        if (f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
            || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float)
            return true;
        return img.Palette?.Colors.Any(c => c.A < 255) == true;
    }

    static BitmapSource OnWhite(BitmapSource bgra)
    {
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var src = new byte[w * h * 4];
        bgra.CopyPixels(src, w * 4, 0);
        var stride = (w * 3 + 3) & ~3;
        var dst = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var o = y * stride + x * 3;
                var a = src[i + 3];
                for (var c = 0; c < 3; c++) dst[o + c] = (byte)((src[i + c] * a + 255 * (255 - a) + 127) / 255);
            }
        }
        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, dst, stride);
    }

    static BitmapSource Normalize(BitmapSource img)
    {
        if (Math.Abs(img.DpiX - 96) < 0.01 && Math.Abs(img.DpiY - 96) < 0.01) return img;
        var stride = (img.PixelWidth * img.Format.BitsPerPixel + 7) / 8;
        stride = (stride + 3) & ~3;
        var px = new byte[stride * img.PixelHeight];
        img.CopyPixels(px, stride, 0);
        return BitmapSource.Create(img.PixelWidth, img.PixelHeight, 96, 96, img.Format, img.Palette, px, stride);
    }
}

/// <summary>Runs WPF imaging work on a short-lived STA thread, off the window's thread.</summary>
public static class Sta
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }
}
