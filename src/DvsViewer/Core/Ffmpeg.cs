using System.IO;
using System.Diagnostics;
using System.Globalization;

namespace DvsViewer.Core;


public sealed class Ffmpeg
{
public string ExePath { get; }
    public Ffmpeg(string exePath) => ExePath = exePath;

    public static Ffmpeg? Find()
    {
        string? exe = FindExecutable();
        return exe is null ? null : new Ffmpeg(exe);
    }

    
    
    
    
    public static string? FindExecutable()
    {
        var env = Environment.GetEnvironmentVariable("DVSS_FFMPEG");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null)
        {
            foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
        }

        foreach (var cand in new[]
        {
            @"C:\ffmpeg\bin\ffmpeg.exe",
            @"C:\Tools\ffmpeg\bin\ffmpeg.exe",
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
        })
        {
            if (File.Exists(cand)) return cand;
        }

        var tempFfmpeg = Path.Combine(Path.GetTempPath(), "opencode", "ffmpeg");
        if (Directory.Exists(tempFfmpeg))
        {
            foreach (var f in Directory.EnumerateFiles(tempFfmpeg, "ffmpeg*.exe", SearchOption.AllDirectories))
                return f;
        }
        return null;
    }

    public bool MuxH264ToMp4(string h264Path, string outMp4, double fps)
        => Run("-y", "-hide_banner", "-loglevel", "error", "-r", fps.ToString("0.###"),
               "-i", h264Path, "-c", "copy", "-r", fps.ToString("0.###"), outMp4);

    public bool DecodeOggToPcmWav(string oggPath, string outWav)
        => Run("-y", "-hide_banner", "-loglevel", "error", "-i", oggPath, "-c:a", "pcm_s16le", outWav);

    
    
    
    
    
    
    public bool MuxVideoWithAudio(string videoPath, string audioPath, string outPath, double audioOffsetSec = 0)
    {
        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", videoPath,
        };
        if (audioOffsetSec > 0.001)
        {
            args.Add("-ss");
            args.Add(audioOffsetSec.ToString("0.000", CultureInfo.InvariantCulture));
        }
        args.Add("-i");
        args.Add(audioPath);
        args.Add("-map");
        args.Add("0:v:0");
        args.Add("-map");
        args.Add("1:a:0");
        args.Add("-c:v");
        args.Add("copy");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add("48k");
        if (audioOffsetSec < -0.001)
        {
            args.Add("-af");
            args.Add($"adelay={(-audioOffsetSec * 1000.0):0}:all=1");
        }
        args.Add("-shortest");
        args.Add(outPath);
        return Run(args.ToArray());
    }

    public bool Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(ExePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return false;

            
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            bool exited = proc.WaitForExit(TimeSpan.FromMinutes(15));
            if (!exited)
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
