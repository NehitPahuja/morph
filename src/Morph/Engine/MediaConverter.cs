using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Morph.Engine;

/// <summary>Audio and video through ffmpeg.</summary>
public static class MediaConverter
{
    public static async Task<string> ConvertAsync(
        string input, OutputFormat f, Quality q, string dir, string baseName,
        IProgress<double>? progress, CancellationToken ct)
    {
        Ffmpeg.Require();
        if (!Formats.IsAvailable(f)) throw new MorphException($"This ffmpeg build has no {f.Encoder} encoder");
        var info = await Task.Run(() => Ffmpeg.Probe(input), ct);
        if (info.Known && f.Category == Category.Audio && !info.HasAudio) throw new MorphException("This file has no sound to convert");
        if (info.Known && f.Category == Category.Video && !info.HasVideo) throw new MorphException("This file has no picture to convert");

        var output = Output.FreeFile(dir, baseName, f.Ext);
        var args = new List<string> { "-i", input };
        args.AddRange(f.Category == Category.Audio ? AudioArgs(f.Id, q) : VideoArgs(f.Id, q));
        args.Add(output);
        try
        {
            await Ffmpeg.RunAsync(args, info.Duration, progress, ct);
        }
        catch
        {
            Output.TryDelete(output);
            throw;
        }
        return output;
    }

    /// <summary>First audio stream only; tags carried over; video, subtitles and data dropped.</summary>
    public static List<string> AudioArgs(string id, Quality q)
    {
        var a = new List<string> { "-map", "0:a:0", "-vn", "-sn", "-dn", "-map_metadata", "0" };
        string Pick(string small, string balanced, string best) => q switch { Quality.Small => small, Quality.Balanced => balanced, _ => best };
        switch (id)
        {
            case "mp3":
                a.AddRange(["-c:a", "libmp3lame"]);
                // Balanced uses LAME's V2 preset (variable, about 190 kbps); the others are constant rates.
                a.AddRange(q == Quality.Balanced ? ["-q:a", "2"] : ["-b:a", Pick("128k", "", "320k")]);
                a.AddRange(["-id3v2_version", "3"]);
                break;
            case "m4a":
                a.AddRange(["-c:a", "aac", "-b:a", Pick("128k", "192k", "256k"), "-movflags", "+faststart"]);
                break;
            case "opus":
                a.AddRange(["-c:a", "libopus", "-b:a", Pick("64k", "128k", "192k")]);
                break;
            case "ogg":
                a.AddRange(["-c:a", "libvorbis", "-q:a", Pick("3", "5", "8")]);
                break;
            case "flac":
                a.AddRange(["-c:a", "flac", "-compression_level", "8"]);
                break;
            case "wav":
                a.AddRange(["-c:a", "pcm_s16le"]);
                break;
            default:
                throw new MorphException($"Unknown audio format {id}");
        }
        return a;
    }

    public static List<string> VideoArgs(string id, Quality q)
    {
        string Pick(string small, string balanced, string best) => q switch { Quality.Small => small, Quality.Balanced => balanced, _ => best };
        var v = new List<string> { "-map", "0:v:0", "-sn", "-dn", "-map_metadata", "0" };

        if (id == "anim-gif")
        {
            var fps = Pick("10", "12", "15");
            var width = Pick("360", "480", "640");
            // Two passes over the frames in one graph: build a palette for this clip, then map onto it.
            var graph = $"fps={fps},scale=min({width}\\,iw):-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=256[p];[b][p]paletteuse=dither=bayer:bayer_scale=4";
            v.AddRange(["-an", "-vf", graph, "-loop", "0"]);
            return v;
        }

        // H.264 and VP9 in 4:2:0 need even dimensions; Small also caps the height at 720.
        var scale = q == Quality.Small
            ? "scale=-2:trunc(min(720\\,ih)/2)*2"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        v.AddRange(["-vf", scale, "-pix_fmt", "yuv420p"]);
        // Sound is optional ("?"): a silent video converts too.
        v.AddRange(["-map", "0:a:0?"]);

        switch (id)
        {
            case "mp4":
            case "mkv":
            case "mov":
                v.AddRange(["-c:v", "libx264", "-preset", Pick("medium", "medium", "slow"), "-crf", Pick("28", "23", "18")]);
                v.AddRange(["-c:a", "aac", "-b:a", Pick("128k", "160k", "192k")]);
                if (id != "mkv") v.AddRange(["-movflags", "+faststart"]);
                break;
            case "webm":
                v.AddRange(["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", Pick("40", "33", "27"),
                    "-row-mt", "1", "-deadline", "good", "-cpu-used", Pick("4", "4", "2")]);
                v.AddRange(["-c:a", "libopus", "-b:a", Pick("96k", "128k", "160k")]);
                break;
            default:
                throw new MorphException($"Unknown video format {id}");
        }
        return v;
    }
}
