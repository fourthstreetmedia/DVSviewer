using System.IO;
using System.Text;

namespace DvsViewer.Core;


public sealed class ExportOptions
{
    public bool Video { get; set; } = true;
    public bool Audio { get; set; } = true;
    public bool Gps { get; set; } = true;
    public bool Frames { get; set; } = true;
    public bool Report { get; set; } = true;
    public bool Mux { get; set; } = true;
    public bool WriteRawBlocks { get; set; } = true;

    
    public string? Channel { get; set; }

    
    public List<string>? Channels { get; set; }

    
    public int? Src { get; set; }

    
    public List<int>? Srcs { get; set; }

    
    public Dictionary<int, string>? AudioMap { get; set; }
}

public sealed class ChannelMedia
{
    public required string ChannelName { get; init; }
    public required string H264Path { get; init; }
    public required string Mp4Path { get; init; }
    public required double Fps { get; init; }
    public required long DurationMs { get; init; }
    public required int FrameCount { get; init; }
    public int? AudioSrc { get; init; }

    
    public long FirstTimeUs { get; init; }

    public string SafeName => AudioSource.Sanitize(ChannelName);
}

public sealed class SourceMedia
{
    public required int Src { get; init; }
    public required string Label { get; init; }
    public required string OggPath { get; init; }
    public required string WavPath { get; init; }
    public required int SampleRate { get; init; }
    public required int Channels { get; init; }
    public required int PacketCount { get; init; }
    public string Status { get; set; } = "";
    public double? DecodedRate { get; set; }
    public double? DurationSeconds { get; set; }

    
    public long FirstTimeUs { get; set; }
}

public sealed class ExportResult
{
    public List<string> Files { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<ChannelMedia> Channels { get; } = new();
    public List<SourceMedia> Sources { get; } = new();
    public List<(int Src, string Channel, bool Muxed, string Reason)> Mux { get; } = new();
    public Dictionary<int, string> SrcToChannel { get; } = new();
}





public sealed class ExportService
{
    private readonly Ffmpeg? _ffmpeg;
    public Ffmpeg? Ffmpeg => _ffmpeg;

    public ExportService(Ffmpeg? ffmpeg) => _ffmpeg = ffmpeg;

    

    public ExportResult ExportEverything(DvsFile file, string outDir, ExportOptions opt, Action<string>? log = null)
    {
        var result = new ExportResult();
        Directory.CreateDirectory(outDir);

        if (opt.Gps && file.Gps.Count > 0)
        {
            var gpsCsv = Path.Combine(outDir, "gps.csv");
            Exporter.WriteGpsCsv(gpsCsv, file.Gps);
            Exporter.WriteGpx(Path.Combine(outDir, "gps.gpx"), file.Gps);
            Exporter.WriteKml(Path.Combine(outDir, "gps.kml"), file.Gps);
            result.Files.AddRange(new[] { gpsCsv, Path.Combine(outDir, "gps.gpx"), Path.Combine(outDir, "gps.kml") });
            log?.Invoke($"GPS: {file.Gps.Count} fixes -> gps.csv/.gpx/.kml");
        }

        if (opt.Frames)
        {
            Exporter.WriteFramesCsv(Path.Combine(outDir, "frames.csv"), file.Frames);
            Exporter.WriteFrameMetadata(Path.Combine(outDir, "frame_metadata.txt"), file.Frames);
            result.Files.AddRange(new[] { Path.Combine(outDir, "frames.csv"), Path.Combine(outDir, "frame_metadata.txt") });
        }

        if (opt.Video)
        {
            var vdir = Path.Combine(outDir, "video");
            Directory.CreateDirectory(vdir);
            var media = ExportVideoTo(file, vdir, opt, log);
            result.Channels.AddRange(media);
            result.Files.AddRange(media.Where(m => File.Exists(m.H264Path)).Select(m => m.H264Path));
            result.Files.AddRange(media.Where(m => File.Exists(m.Mp4Path)).Select(m => m.Mp4Path));

            if (opt.Audio && opt.Mux)
                MuxAudioIntoVideo(file, media, Path.Combine(outDir, "audio"), result, log);
        }

        if (opt.Audio)
        {
            var adir = Path.Combine(outDir, "audio");
            Directory.CreateDirectory(adir);
            var sources = ExportAudioTo(file, adir, opt, log);
            result.Sources.AddRange(sources);
            foreach (var s in sources)
            {
                result.Files.Add(s.OggPath);
                if (File.Exists(s.WavPath)) result.Files.Add(s.WavPath);
            }
        }

        if (opt.Report)
        {
            WriteReport(file, outDir, result);
            result.Files.Add(Path.Combine(outDir, "report.json"));
        }
        return result;
    }

    

    public List<ChannelMedia> ExportVideoTo(DvsFile file, string vdir, ExportOptions opt, Action<string>? log = null)
    {
        Directory.CreateDirectory(vdir);
        var list = new List<ChannelMedia>();

        foreach (var ch in file.Channels)
        {
            if (opt.Channel is not null &&
                !string.Equals(ch.Name, opt.Channel, StringComparison.OrdinalIgnoreCase))
                continue;
            if (opt.Channels is { Count: > 0 } &&
                !opt.Channels.Contains(ch.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            var flist = ch.Frames.OrderBy(f => f.Offset).ToList();
            if (flist.Count == 0) continue;

            var h264Path = Path.Combine(vdir, ch.SafeName + ".h264");
            using (var fa = FileAccessor.Open(file.FilePath))
            using (var fs = new FileStream(h264Path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[1 << 20];
                foreach (var f in flist)
                {
                    long off = f.H264Offset, remaining = f.H264Length;
                    while (remaining > 0)
                    {
                        int n = (int)Math.Min(buf.Length, remaining);
                        fa.CopyTo(off, buf, 0, n);
                        fs.Write(buf, 0, n);
                        off += n;
                        remaining -= n;
                    }
                }
            }

            var mp4Path = Path.Combine(vdir, ch.SafeName + ".mp4");
            bool mp4Ok = false;
            if (_ffmpeg is not null)
            {
                if (_ffmpeg.MuxH264ToMp4(h264Path, mp4Path, ch.Fps))
                {
                    mp4Ok = true;
                    log?.Invoke($"video '{ch.Name}': {flist.Count} frames, {ch.Fps:0.###} fps -> {Path.GetFileName(mp4Path)}");
                }
                else
                    resultWarning(list, log, $"video '{ch.Name}': mp4 mux failed (raw .h264 kept)");
            }

            
            
            if (mp4Ok)
            {
                File.Delete(h264Path);
                log?.Invoke($"removed raw {Path.GetFileName(h264Path)}");
            }

            list.Add(new ChannelMedia
            {
                ChannelName = ch.Name,
                H264Path = h264Path,
                Mp4Path = mp4Path,
                Fps = ch.Fps,
                DurationMs = ch.DurationMs,
                FrameCount = flist.Count,
                AudioSrc = ch.AudioSrc,
                FirstTimeUs = ch.FirstTimeUs,
            });
        }
        return list;
    }

    private static void resultWarning(List<ChannelMedia> _, Action<string>? log, string msg) => log?.Invoke(msg);

    

    public List<SourceMedia> ExportAudioTo(DvsFile file, string adir, ExportOptions opt, Action<string>? log = null)
    {
        Directory.CreateDirectory(adir);
        var list = new List<SourceMedia>();

        foreach (var src in file.AudioSources)
        {
            if (opt.Src is not null && opt.Src.Value != src.Src) continue;
            if (opt.Srcs is { Count: > 0 } && !opt.Srcs.Contains(src.Src)) continue;
            if (src.Packets.Count == 0) continue;

            string label = LabelFor(src, opt);
            string safe = AudioSource.Sanitize(label);
            string fname = $"{safe}_src{src.Src}";
            string oggPath = Path.Combine(adir, fname + ".opus");
            string wavPath = Path.Combine(adir, fname + ".wav");

            var media = new SourceMedia
            {
                Src = src.Src,
                Label = label,
                OggPath = oggPath,
                WavPath = wavPath,
                SampleRate = src.SampleRate,
                Channels = src.Channels,
                PacketCount = src.Packets.Count,
                FirstTimeUs = src.FirstTimeUs,
            };

            try
            {
                File.WriteAllBytes(oggPath, OggOpusWriter.BuildOggOpus(src.Packets, src.Channels, src.SampleRate));
            }
            catch (Exception e)
            {
                log?.Invoke($"audio src{src.Src} '{label}': ogg write failed: {e.Message}");
            }

            if (_ffmpeg is not null && _ffmpeg.DecodeOggToPcmWav(oggPath, wavPath))
            {
                media.Status = "decoded";
                media.DecodedRate = src.SampleRate;
                media.DurationSeconds = (double)src.Packets.Count * 0.02;
                log?.Invoke($"audio src{src.Src} '{label}': {src.Packets.Count} packets -> {Path.GetFileName(wavPath)}");
            }
            else
            {
                media.Status = "raw-only";
                log?.Invoke($"audio src{src.Src} '{label}': {src.Packets.Count} packets -> .opus only (ffmpeg {( _ffmpeg is null ? "not found" : "decode failed")})");
            }
            list.Add(media);

            if (opt.WriteRawBlocks)
            {
                var rawDir = Path.Combine(adir, "raw_blocks");
                Directory.CreateDirectory(rawDir);
                for (int i = 0; i < file.AudioBlocks.Count; i++)
                {
                    if (file.AudioBlocks[i].Src != src.Src) continue;
                    File.WriteAllBytes(Path.Combine(rawDir, $"block_{i:000}_src{src.Src}.bin"), file.AudioBlocks[i].Raw);
                }
            }
        }
        return list;
    }

    public static string LabelFor(AudioSource src, ExportOptions opt)
    {
        if (opt.AudioMap is not null && opt.AudioMap.TryGetValue(src.Src, out var name))
            return name;
        return src.Label;
    }

    

    
    
    
    
    public void MuxAudioIntoVideo(DvsFile file, List<ChannelMedia> channels, string audioDir, ExportResult result, Action<string>? log = null, List<int>? srcs = null)
    {
        if (_ffmpeg is null) return;

        var srcMedia = ExportAudioTo(file, audioDir, new ExportOptions { Video = false, Gps = false, Frames = false, Report = false, Mux = false, WriteRawBlocks = false, Srcs = srcs }, log);
        foreach (var s in srcMedia)
        {
            if (s.Status != "decoded" || !File.Exists(s.WavPath)) continue;

            var ch = channels.FirstOrDefault(c =>
                string.Equals(c.ChannelName, s.Label, StringComparison.OrdinalIgnoreCase));
            if (ch is null)
            {
                result.Mux.Add((s.Src, s.Label, false, "no video for this audio subchannel"));
                continue;
            }
            if (!File.Exists(ch.Mp4Path))
            {
                result.Mux.Add((s.Src, s.Label, false, "mp4 not produced"));
                continue;
            }
            string outPath = Path.Combine(Path.GetDirectoryName(ch.Mp4Path)!, $"{Path.GetFileNameWithoutExtension(ch.Mp4Path)}_with_audio.mp4");
            
            
            
            double audioOffsetSec = 0;
            if (ch.FirstTimeUs > 0 && s.FirstTimeUs > 0)
                audioOffsetSec = (ch.FirstTimeUs - s.FirstTimeUs) / 1e6;
            if (_ffmpeg.MuxVideoWithAudio(ch.Mp4Path, s.WavPath, outPath, audioOffsetSec))
            {
                result.Mux.Add((s.Src, s.Label, true, ""));
                result.Files.Add(outPath);
                log?.Invoke(audioOffsetSec > 0.001
                    ? $"muxed src{s.Src} audio into {Path.GetFileName(outPath)} (trimmed {audioOffsetSec:0.000}s audio lead)"
                    : audioOffsetSec < -0.001
                        ? $"muxed src{s.Src} audio into {Path.GetFileName(outPath)} (delayed {(-audioOffsetSec):0.000}s audio lag)"
                        : $"muxed src{s.Src} audio into {Path.GetFileName(outPath)}");
            }
            else
            {
                result.Mux.Add((s.Src, s.Label, false, "mux failed"));
            }
        }
    }

    

    private void WriteReport(DvsFile file, string outDir, ExportResult result)
    {
        var channels = new Dictionary<string, object?>();
        foreach (var ch in file.Channels)
            channels[ch.Name] = ch.Frames.Count;

        var audioSources = new Dictionary<string, object?>();
        foreach (var s in file.AudioSources)
            audioSources[s.Src.ToString()] = s.BlockCount;

        var report = new Dictionary<string, object?>
        {
            ["file"] = Path.GetFileName(file.FilePath),
            ["file_size"] = file.Info.FileSize,
            ["version"] = file.Info.Version,
            ["declared_size"] = file.Info.DeclaredSize,
            ["plugin_count"] = file.Info.PluginCount,
            ["channel_names"] = file.ChannelNames,
            ["vehicle"] = string.IsNullOrEmpty(file.Vehicle) ? file.Site : file.Vehicle,
            ["gps_count"] = file.Gps.Count,
            ["frame_count"] = file.Frames.Count,
            ["channels"] = channels,
            ["audio_block_count"] = file.AudioBlocks.Count,
            ["audio_sources"] = audioSources,
            ["audio_outputs"] = result.Sources.Select(s => new Dictionary<string, object?>
            {
                ["src"] = s.Src,
                ["label"] = s.Label,
                ["packets"] = s.PacketCount,
                ["status"] = s.Status,
            }).ToList(),
            ["audio_mux"] = result.Mux.Select(m => new Dictionary<string, object?>
            {
                ["src"] = m.Src,
                ["channel"] = m.Channel,
                ["muxed"] = m.Muxed,
                ["reason"] = m.Reason,
            }).ToList(),
        };
        Exporter.WriteReportJson(Path.Combine(outDir, "report.json"), report);
    }
}
