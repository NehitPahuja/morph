using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Morph.Engine;

public sealed class MorphException(string message) : Exception(message);

public sealed record MediaInfo(double Duration, bool HasAudio, bool HasVideo, bool Known);

public static class Ffmpeg
{
    /// <summary>Quiet, machine-readable progress on stdout, and never overwrite (-n).</summary>
    public static readonly string[] Base = ["-hide_banner", "-nostdin", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-n"];

    public static string Require() =>
        Tools.FFmpeg ?? throw new MorphException("ffmpeg isn't installed. Install it with: winget install Gyan.FFmpeg");

    public static MediaInfo Probe(string path)
    {
        var probe = Tools.FFprobe;
        if (probe == null) return new MediaInfo(0, true, true, false);
        try
        {
            var psi = new ProcessStartInfo(probe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type:stream_disposition=attached_pic", "-of", "json", path })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            var json = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            if (p.ExitCode != 0) return new MediaInfo(0, true, true, false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double duration = 0;
            if (root.TryGetProperty("format", out var fmt) && fmt.TryGetProperty("duration", out var d))
                double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
            bool audio = false, video = false;
            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var s in streams.EnumerateArray())
                {
                    var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                    if (type == "audio") audio = true;
                    if (type == "video")
                    {
                        // Album art inside an audio file is a "video" stream too; it doesn't count.
                        var cover = s.TryGetProperty("disposition", out var disp)
                            && disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                        if (!cover) video = true;
                    }
                }
            }
            return new MediaInfo(duration, audio, video, true);
        }
        catch (Exception)
        {
            return new MediaInfo(0, true, true, false);
        }
    }

    /// <summary>
    /// Runs ffmpeg to the end. Reports progress (0..1) when the duration is known. Cancelling kills it.
    /// Fails with ffmpeg's own last error line.
    /// </summary>
    public static async Task RunAsync(IEnumerable<string> args, double duration, IProgress<double>? progress, CancellationToken ct)
    {
        var exe = Require();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in Base.Concat(args)) psi.ArgumentList.Add(a);

        var errors = new Queue<string>();
        using var p = new Process { StartInfo = psi };
        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (errors)
            {
                errors.Enqueue(e.Data.Trim());
                while (errors.Count > 20) errors.Dequeue();
            }
        };
        p.OutputDataReceived += (_, e) =>
        {
            // "out_time_us=12345678" (microseconds). Older builds name it out_time_ms but mean the same.
            var line = e.Data;
            if (line == null || duration <= 0 || progress == null) return;
            var eq = line.IndexOf('=');
            if (eq < 0) return;
            var key = line[..eq];
            if (key != "out_time_us" && key != "out_time_ms") return;
            if (long.TryParse(line[(eq + 1)..], out var us) && us >= 0)
                progress.Report(Math.Min(1.0, us / 1_000_000.0 / duration));
        };

        ct.ThrowIfCancellationRequested();
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        using (ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch (Exception) { } }))
        {
            await p.WaitForExitAsync().ConfigureAwait(false);
        }
        p.WaitForExit(); // lets the output readers finish

        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
        {
            string? last;
            lock (errors) last = errors.LastOrDefault();
            throw new MorphException(Tidy(last) ?? $"ffmpeg stopped with code {p.ExitCode}");
        }
    }

    static string? Tidy(string? line)
    {
        if (line == null) return null;
        if (line.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            return "A file with the output name appeared while converting; nothing was overwritten";
        return line;
    }
}
