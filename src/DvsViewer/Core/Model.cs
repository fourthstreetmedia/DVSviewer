using System.Text;

namespace DvsViewer.Core;


public sealed class DvsFile
{
    public string FilePath { get; set; } = "";
    public DvsInfo Info { get; set; } = new();
    public List<PluginRecord> Plugins { get; } = new();
    public List<string> ChannelNames { get; } = new();
    public List<GpsFix> Gps { get; } = new();
    public List<VideoFrame> Frames { get; } = new();
    public List<AudioBlock> AudioBlocks { get; } = new();
    public List<AudioSource> AudioSources { get; } = new();
    public string? Site { get; set; }

    
    public string Vehicle { get; set; } = "";

    
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }

    public List<VideoChannel> Channels { get; } = new();

    
    public Dictionary<int, string> SrcChannelName { get; } = new();
}

public sealed class DvsInfo
{
    public uint Version { get; set; }
    public ulong DeclaredSize { get; set; }
    public uint PluginCount { get; set; }
    public long FileSize { get; set; }
    public string FileName { get; set; } = "";

    public static readonly Dictionary<uint, string> Kinds = new()
    {
        [1001] = "index/system",
        [1002] = "gps-or-image",
        [1003] = "sound-index",
        [1004] = "sound-data",
        [1006] = "event-history",
    };
}

public sealed class PluginRecord
{
    public int Index { get; set; }
    public uint Marker { get; set; }
    public uint V1 { get; set; }
    public uint Count { get; set; }
    public uint V3 { get; set; }
    public long Base { get; set; }
    public uint V5 { get; set; }
    public long Size { get; set; }
    public uint V7 { get; set; }
    public uint V8 { get; set; }
    public uint V9 { get; set; }
    public uint V10 { get; set; }

    public string Hint => DvsInfo.Kinds.TryGetValue(Marker, out var h) ? h : "?";
}

public sealed class GpsFix
{
    public string TimeUtc { get; set; } = "";
    public ulong TimestampRaw { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public double SpeedKmh { get; set; }
    public double Heading { get; set; }
    public DateTime? Utc { get; set; }
}


public sealed class VideoFrame
{
    public string Channel { get; set; } = "";
    public int ChId { get; set; }
    public string Site { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string Dt { get; set; } = "";
    public string Ll { get; set; } = "";
    public string Alarms { get; set; } = "";
    public string Hash { get; set; } = "";
    public string? Codec { get; set; }
    public long Offset { get; set; }
    public long TimeUs { get; set; }
    public string TimeUtc { get; set; } = "";
    public DateTime? Utc { get; set; }
    public long Block { get; set; }
    public long H264Offset { get; set; }
    public int H264Length { get; set; }
}


public sealed class AudioBlock
{
    public long Offset { get; set; }
    public int Src { get; set; }
    public bool HasIsrc { get; set; }
    public List<byte[]> Packets { get; } = new();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Bits { get; set; }
    public byte[] Raw { get; set; } = Array.Empty<byte>();

    
    public long StartUs { get; set; }
}


public sealed class AudioSource
{
    public int Src { get; set; }
    public List<byte[]> Packets { get; } = new();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Bits { get; set; }
    public int BlockCount { get; set; }

    public string? LinkedChannelName { get; set; }

    
    public long FirstTimeUs { get; set; }

    public string Label => LinkedChannelName ?? $"src{Src}";
    public int PacketCount => Packets.Count;
    public long PayloadBytes { get { long t = 0; foreach (var p in Packets) t += p.Length; return t; } }
    public bool HasPackets => Packets.Count > 0;

    public string SafeName => Sanitize(LinkedChannelName ?? $"src{Src}");

    public static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        var r = sb.ToString();
        return string.IsNullOrEmpty(r) ? "unknown" : r;
    }
}


public sealed class VideoChannel
{
    public string Name { get; set; } = "";
    public List<VideoFrame> Frames { get; } = new();
    public int? AudioSrc { get; set; }

    public string SafeName => AudioSource.Sanitize(Name);
    public long FirstTimeUs { get; set; }
    public long LastTimeUs { get; set; }
    public double Fps { get; set; } = 15.0;

    public long DurationMs => LastTimeUs > FirstTimeUs ? (LastTimeUs - FirstTimeUs) / 1000 : 0;
    public bool HasAudio => AudioSrc is not null;
}