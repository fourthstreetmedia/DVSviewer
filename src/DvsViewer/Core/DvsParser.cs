using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DvsViewer.Core;






public static class DvsParser
{
    private static readonly DateTime Epoch1601 = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] Pic2 = "PIC2DTI4"u8.ToArray();
    private static readonly byte[] Ib = "IB:"u8.ToArray();
    private static readonly byte[] Ic = "IC:"u8.ToArray();
    private static readonly byte[] Zero = { 0 };
    private static readonly byte[] StartCode4 = { 0, 0, 0, 1 };
    private static readonly byte[] StartCode3 = { 0, 0, 1 };
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] SrcTag = "SRC\0"u8.ToArray();

    public static DvsFile Parse(string path, Action<string>? log = null)
    {
        using var buf = FileAccessor.Open(path);
        var file = new DvsFile { FilePath = path };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Stage(string msg) => log?.Invoke($"{msg} ({sw.Elapsed.TotalSeconds:0.0}s)");

        Stage($"Parsing {Path.GetFileName(path)}");
        var (info, plugins) = ParseHeader(buf);
        file.Info = info;
        file.Plugins.AddRange(plugins);

        file.ChannelNames.AddRange(ExtractChannelNames(buf, plugins));
        file.Site = ExtractVehicleInfo(buf);

        file.Gps.AddRange(ExtractGps(buf, plugins));
        Stage($"GPS: {file.Gps.Count} fixes");

        Stage("Scanning video blocks...");
        var (imageBase, imageEnd) = FindImageRegion(buf, plugins);
        file.Frames.AddRange(ExtractFrames(buf, imageBase, imageEnd));
        Stage($"Video: {file.Frames.Count} frames");

        var blocks = ExtractAudioBlocks(buf, plugins);
        file.AudioBlocks.AddRange(blocks);
        file.AudioSources.AddRange(BuildAudioSources(blocks, file.ChannelNames));
        Stage($"Audio: {blocks.Count} blocks, {file.AudioSources.Count} sources");

        foreach (var fr in file.Frames)
        {
            if (file.Vehicle.Length == 0 && fr.Vehicle.Length > 0) file.Vehicle = fr.Vehicle;
        }
        var times = file.Frames.Where(f => f.Utc is not null).Select(f => f.Utc!.Value).ToList();
        if (times.Count > 0)
        {
            file.StartUtc = times.Min();
            file.EndUtc = times.Max();
        }

        BuildChannels(file);
        LinkAudioToVideo(file);
        Stage("Done");
        return file;
    }

    

    private static (DvsInfo, List<PluginRecord>) ParseHeader(FileAccessor buf)
    {
        if (buf.Length < 0x18)
            throw new InvalidDataException("file too small to be DVSS");

        var info = new DvsInfo
        {
            Version = buf.U32(0),
            DeclaredSize = buf.U64(0x08),
            PluginCount = buf.U32(0x10),
            FileSize = buf.Length,
            FileName = "",
        };
        if (info.PluginCount > 256)
            throw new InvalidDataException($"suspicious plugin count: {info.PluginCount}");

        var plugins = new List<PluginRecord>();
        long off = 0x18;
        for (int i = 0; i < info.PluginCount && off + 44 <= buf.Length; i++)
        {
            plugins.Add(new PluginRecord
            {
                Index = i,
                Marker = buf.U32(off),
                V1 = buf.U32(off + 4),
                Count = buf.U32(off + 8),
                V3 = buf.U32(off + 12),
                Base = buf.U32(off + 16),
                V5 = buf.U32(off + 20),
                Size = buf.U32(off + 24),
                V7 = buf.U32(off + 28),
                V8 = buf.U32(off + 32),
                V9 = buf.U32(off + 36),
                V10 = buf.U32(off + 40),
            });
            off += 44;
        }
        return (info, plugins);
    }

    

    private static List<GpsFix> ExtractGps(FileAccessor buf, List<PluginRecord> plugins)
    {
        var entries = new List<GpsFix>();
        PluginRecord? best = null;

        foreach (var p in plugins)
        {
            if (p.Base <= 0 || p.Base + 40 > buf.Length) continue;
            ulong ft = buf.U64(p.Base);
            var v = buf.FourDoubles(p.Base + 8);
            var dt = FiletimeToUtc(ft);
            if (dt is null || dt < new DateTime(2020, 1, 1) || dt > new DateTime(2035, 1, 1)) continue;
            if (v[0] < -90 || v[0] > 90 || v[1] < -180 || v[1] > 180) continue;
            best = p;
            break;
        }

        if (best is null) return entries;

        long baseOff = best.Base;
        long pluginSize = best.Size > 0 ? best.Size : 0x100000;
        long end = Math.Min(baseOff + pluginSize, buf.Length - 40L);
        long limit = best.Count > 0 ? best.Count : long.MaxValue;

        long o = baseOff;
        while (o < end)
        {
            ulong ft = buf.U64(o);
            var v = buf.FourDoubles(o + 8);
            var dt = FiletimeToUtc(ft);
            if (dt is null || dt < new DateTime(2020, 1, 1) || dt > new DateTime(2035, 1, 1)) break;
            if (v[0] < -90 || v[0] > 90 || v[1] < -180 || v[1] > 180) break;

            entries.Add(new GpsFix
            {
                TimeUtc = DtToStr(dt.Value),
                TimestampRaw = ft,
                Lat = v[0],
                Lng = v[1],
                SpeedKmh = v[2],
                Heading = v[3],
                Utc = dt,
            });
            o += 40;
            if (entries.Count >= limit) break;
        }
        return entries;
    }

    

    private static List<string> ExtractChannelNames(FileAccessor buf, List<PluginRecord> plugins)
    {
        var names = new List<string>();
        foreach (var p in plugins)
        {
            if (p.Marker != 1001) continue;
            long baseOff = p.Base;
            if (baseOff <= 0 || baseOff + 64 > buf.Length) continue;

            int firstLen = buf.TrimEndLen(baseOff, 64);
            if (firstLen < 2 || !buf.AllPrintable(baseOff, firstLen)) continue;

            long cnt = p.Count > 0 ? p.Count : (p.V8 > 0 ? p.V8 : 16);
            var candidate = new List<string>();
            for (long i = 0; i < cnt; i++)
            {
                long s = baseOff + i * 64;
                long e = s + 64;
                if (e > buf.Length) break;
                int recLen = buf.TrimEndLen(s, 64);
                if (recLen > 0 && buf.AllPrintable(s, recLen))
                    candidate.Add(buf.Ascii(s, recLen));
            }
            if (candidate.Count > 0)
            {
                names = candidate;
                break;
            }
        }
        return names;
    }

    private static string? ExtractVehicleInfo(FileAccessor buf)
    {
        long start = Math.Min(0x18000, buf.Length);
        long end = Math.Min(0x40000, buf.Length);
        if (end <= start) return null;

        long i = start;
        while (i < end)
        {
            long j = i;
            while (j < end && buf.ReadByte(j) >= 0x20 && buf.ReadByte(j) <= 0x7e) j++;
            int len = (int)(j - i);
            if (len >= 3)
            {
                var s = buf.Latin1(i, len);
                if (s == "GPS" || s == "SF Muni" || s.Contains("Muni"))
                    return s;
            }
            i = j + 1;
        }
        return null;
    }

    

    private static (long Base, long End) FindImageRegion(FileAccessor buf, List<PluginRecord> plugins)
    {
        foreach (var p in plugins)
        {
            long baseOff = p.Base, size = p.Size;
            if (baseOff <= 0 || size <= 0) continue;
            if (baseOff + 64 <= buf.Length && buf.IndexOf(Pic2, baseOff, baseOff + 64) >= 0)
                return (baseOff, Math.Min(baseOff + size, buf.Length));
            if (baseOff + 0x10 + 8 <= buf.Length && buf.EqualsAt(baseOff + 0x10, Pic2))
                return (baseOff, Math.Min(baseOff + size, buf.Length));
        }

        var biggest = plugins.OrderByDescending(p => p.Size).First();
        long be = biggest.Base;
        long es = biggest.Size > 0 ? biggest.Size : 0;
        return (be, Math.Min(be + es, buf.Length));
    }

    private static List<VideoFrame> ExtractFrames(FileAccessor buf, long imageBase, long imageEnd)
    {
        var frames = new List<VideoFrame>();
        if (imageEnd > buf.Length) imageEnd = buf.Length;

        var blockStarts = new List<long>();
        foreach (long rel in buf.FindOccurrences(Pic2, imageBase, imageEnd))
        {
            long start = rel - 0x10;
            if (start >= imageBase) blockStarts.Add(start);
        }
        if (blockStarts.Count == 0) return frames;
        frames.Capacity = Math.Min(int.MaxValue, blockStarts.Count * 12);
        var marker = DetectMetaMarker(buf, imageBase, imageEnd);

        for (int bi = 0; bi < blockStarts.Count; bi++)
        {
            long blk = blockStarts[bi];
            long blkEnd = bi + 1 < blockStarts.Count ? blockStarts[bi + 1] : imageEnd;
            if (blk + 0x40 > buf.Length) continue;

            long dataSize = buf.U32(blk);
            int chId = (int)buf.U32(blk + 4);
            long count = buf.U32(blk + 0x1C);
            if (dataSize <= 0 || dataSize > blkEnd - blk) continue;
            long region = blkEnd;

            var indexRecs = new List<(ulong t, long off, long csz)>();
            long io = blk + 0x40;
            while (io + 24 <= Math.Min(region, buf.Length) && indexRecs.Count <= count)
            {
                ulong t = buf.U64(io);
                var dt = FiletimeToUtc(t);
                if (dt is null || dt < new DateTime(2000, 1, 1) || dt > new DateTime(2040, 1, 1)) break;
                indexRecs.Add((t, buf.U32(io + 16), buf.U32(io + 20)));
                io += 24;
            }

            var markers = buf.FindOccurrences(marker, blk, region);
            int framesInBlock = 0;
            for (int m = 0; m < markers.Count; m++)
            {
                long ib = markers[m];
                long nul = buf.IndexOf(Zero, ib, Math.Min(region, ib + 4096));
                if (nul < 0) continue;

                var meta = ParseMetadata(buf, ib, nul);
                if (!meta.ContainsKey("CN")) continue;

                long frameEnd = m + 1 < markers.Count ? markers[m + 1] : region;

                long scEnd = Math.Min(Math.Min(frameEnd, buf.Length), nul + 64);
                long sc4 = buf.IndexOf(StartCode4, nul, scEnd);
                long sc3 = buf.IndexOf(StartCode3, nul, scEnd);
                long sc = sc4 >= 0 ? sc4 : sc3;
                if (sc < 0) continue;

                long payloadLen = frameEnd - sc;
                if (payloadLen < 8) continue;

                ulong t = 0;
                if (framesInBlock < indexRecs.Count) t = indexRecs[framesInBlock].t;
                var utc = FiletimeToUtc(t);

                string? codec = null;
                long tagEnd = Math.Min(buf.Length, nul + 17);
                if (tagEnd > nul + 1)
                {
                    if (buf.IndexOf("DTIS264I"u8.ToArray(), nul + 1, tagEnd) >= 0 ||
                        buf.IndexOf("DTISH264"u8.ToArray(), nul + 1, tagEnd) >= 0)
                        codec = "H.264";
                }

                frames.Add(new VideoFrame
                {
                    Channel = meta.TryGetValue("CN", out var cn) ? cn : "",
                    ChId = chId,
                    Site = meta.TryGetValue("SN", out var sn) ? sn : "",
                    Vehicle = meta.TryGetValue("VI", out var vi) ? vi : "",
                    Dt = meta.TryGetValue("DT", out var dt) ? dt : "",
                    Ll = meta.TryGetValue("LL", out var ll) ? ll : "",
                    Alarms = meta.TryGetValue("Alarms", out var al) ? al : "",
                    Hash = meta.TryGetValue("hash", out var hh) ? hh : "",
                    Codec = codec,
                    Offset = ib,
                    TimeUs = t == 0 ? 0 : (long)(t / 10.0),
                    TimeUtc = DtToStr(utc),
                    Utc = utc,
                    Block = blk,
                    H264Offset = sc,
                    H264Length = (int)payloadLen,
                });
                framesInBlock++;
            }
        }
        return frames;
    }

    private static byte[] DetectMetaMarker(FileAccessor buf, long imageBase, long imageEnd)
    {
        long sample = Math.Min(imageBase + 512 * 1024, imageEnd);
        for (int letter = 'A'; letter <= 'Z'; letter++)
        {
            var marker = new byte[] { (byte)'I', (byte)letter, (byte)':' };
            if (MarkerPlausible(buf, marker, imageBase, sample))
                return marker;
        }
        return Ib;
    }

    private static bool MarkerPlausible(FileAccessor buf, byte[] marker, long from, long to)
    {
        long pos = from;
        int tries = 0;
        while (pos < to && tries < 32)
        {
            long i = buf.IndexOf(marker, pos, to);
            if (i < 0) return false;
            long o = i + marker.Length;
            int hex = 0;
            while (hex < 256)
            {
                byte b = buf.ReadByte(o + hex);
                if ((b >= '0' && b <= '9') || (b >= 'a' && b <= 'f') || (b >= 'A' && b <= 'F')) hex++;
                else break;
            }
            if (hex >= 8 && buf.ReadByte(o + hex) == ';' && HasMetadataFields(buf, o + hex + 1, 512))
                return true;
            pos = i + 1;
            tries++;
        }
        return false;
    }

    private static bool HasMetadataFields(FileAccessor buf, long off, int max)
    {
        int sn = 0, cn = 0;
        for (int k = 0; k + 2 < max && off + k + 2 < buf.Length; k++)
        {
            byte b = buf.ReadByte(off + k);
            if (b == 0) break;
            if (b == 'S' && buf.ReadByte(off + k + 1) == 'N' && buf.ReadByte(off + k + 2) == ':') sn++;
            else if (b == 'C' && buf.ReadByte(off + k + 1) == 'N' && buf.ReadByte(off + k + 2) == ':') cn++;
            if (sn > 0 && cn > 0) return true;
        }
        return false;
    }

    private static Dictionary<string, string> ParseMetadata(FileAccessor buf, long ib, long nul)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var s = buf.Latin1(ib, (int)(nul - ib));

        int colon = s.IndexOf(':');
        if (colon >= 0)
        {
            int semi = s.IndexOf(';', colon + 1);
            if (semi > colon + 1)
                d["hash"] = s.Substring(colon + 1, semi - (colon + 1));
        }

        int pos = colon >= 0 ? s.IndexOf(';', colon + 1) + 1 : 0;
        while (pos < s.Length)
        {
            int semi2 = s.IndexOf(';', pos);
            int end = semi2 < 0 ? s.Length : semi2;
            int c2 = s.IndexOf(':', pos);
            if (c2 > pos && c2 < end)
            {
                var key = s.Substring(pos, c2 - pos);
                var val = s.Substring(c2 + 1, end - (c2 + 1)).Trim();
                d[key] = val;
            }
            if (semi2 < 0) break;
            pos = semi2 + 1;
        }
        return d;
    }

    

    private static List<AudioBlock> ExtractAudioBlocks(FileAccessor buf, List<PluginRecord> plugins)
    {
        var blocks = new List<AudioBlock>();
        var regions = new List<(long lo, long hi)>();

        foreach (var p in plugins)
        {
            if (p.Marker == 1004 && p.Base > 0 && p.Size > 0)
                regions.Add((p.Base, Math.Min(p.Base + p.Size, buf.Length)));
        }
        if (regions.Count == 0)
        {
            foreach (var p in plugins)
            {
                if (p.Base > 0 && p.Size > 0 && p.Size < 100L * 1024 * 1024)
                    regions.Add((p.Base, Math.Min(p.Base + p.Size, buf.Length)));
            }
        }

        var seen = new HashSet<(long, long)>();
        int globalIndex = 0;
        foreach (var (lo, hi) in regions)
        {
            if (!seen.Add((lo, hi))) continue;

            
            
            
            var blockTimes = ReadSoundIndexTimes(buf, lo, hi);
            int blkIndex = 0;

            foreach (long o in buf.FindOccurrences(Riff, lo, hi))
            {
                var w = ParseWavBlock(buf, o);
                if (w is not null)
                {
                    var (srcId, rate, ch, bits, packets) = w.Value;
                    long riffSize = buf.U32(o + 4);
                    long rawLen = Math.Min(8 + riffSize, buf.Length - o);
                    var raw = new byte[Math.Max(0, rawLen)];
                    if (raw.Length > 0) buf.CopyTo(o, raw, 0, raw.Length);

                    int src = srcId;
                    bool hasIsrc = srcId >= 0;
                    if (src < 0) src = globalIndex % 3;

                    blocks.Add(new AudioBlock
                    {
                        Offset = o,
                        Src = src,
                        HasIsrc = hasIsrc,
                        SampleRate = rate,
                        Channels = ch,
                        Bits = bits,
                        Raw = raw,
                        StartUs = blkIndex < blockTimes.Count ? blockTimes[blkIndex] : 0,
                    });
                    foreach (var p in packets) blocks[^1].Packets.Add(p);
                    globalIndex++;
                    blkIndex++;
                }
            }
        }
        return blocks;
    }

    
    
    
    
    
    
    private static List<long> ReadSoundIndexTimes(FileAccessor buf, long lo, long hi)
    {
        var times = new List<long>();
        long o = lo;
        while (o + 36 <= hi)
        {
            ulong t = buf.U64(o + 8);
            var dt = FiletimeToUtc(t);
            if (dt is null || dt < new DateTime(2000, 1, 1) || dt > new DateTime(2040, 1, 1))
                break;
            times.Add((long)(t / 10.0));
            o += 36;
        }
        return times;
    }

    private static (int Src, int SampleRate, int Channels, int Bits, List<byte[]> Packets)? ParseWavBlock(FileAccessor buf, long off)
    {
        if (off + 12 > buf.Length || buf.U32(off) != 0x46464952) return null;   
        long riffSize = buf.U32(off + 4);
        if (buf.U32(off + 8) != 0x45564157) return null;                          
        if (off + 36 > buf.Length) return null;

        int sampleRate = (int)buf.U32(off + 24);
        int channels = buf.U16(off + 22);
        int bits = buf.U16(off + 34);

        
        long dataEnd = off + 8 + riffSize;
        long from = off + 12;
        long to = Math.Min(dataEnd, buf.Length);
        long dOff = buf.IndexOf(Encoding.ASCII.GetBytes("data"), from, to);
        if (dOff < 0) return null;
        long dSize = Math.Min(buf.U32(dOff + 4), buf.Length - (dOff + 8));
        if (dSize < 0) return null;

        
        int src = -1;
        long srcOff = buf.IndexOf(SrcTag, off, to);
        if (srcOff >= 0 && srcOff + 5 <= buf.Length && buf.ReadByte(srcOff + 4) < 16)
            src = buf.ReadByte(srcOff + 4);

        var packets = new List<byte[]>();
        long pos = 0;
        while (pos + 2 <= dSize)
        {
            int L = buf.U16(dOff + 8 + pos);
            if (L == 0 || pos + 2 + L > dSize) break;
            var p = new byte[L];
            buf.CopyTo(dOff + 8 + pos + 2, p, 0, L);
            packets.Add(p);
            pos += 2 + L;
        }
        return (src, sampleRate, channels, bits, packets);
    }

    private static List<AudioSource> BuildAudioSources(List<AudioBlock> blocks, List<string> channelNames)
    {
        var dict = new SortedDictionary<int, AudioSource>();
        foreach (var b in blocks)
        {
            if (!dict.TryGetValue(b.Src, out var src))
            {
                src = new AudioSource
                {
                    Src = b.Src,
                    SampleRate = b.SampleRate,
                    Channels = b.Channels,
                    Bits = b.Bits,
                };
                dict[b.Src] = src;
            }
            foreach (var p in b.Packets) src.Packets.Add(p);
            src.BlockCount++;
        }
        var list = dict.Values.ToList();
        foreach (var s in list)
        {
            if (s.Src >= 0 && s.Src < channelNames.Count)
                s.LinkedChannelName = channelNames[s.Src];
            s.FirstTimeUs = blocks.Where(b => b.Src == s.Src && b.StartUs > 0)
                                  .Select(b => b.StartUs)
                                  .DefaultIfEmpty(0L)
                                  .Min();
        }
        return list;
    }

    

    private static void BuildChannels(DvsFile file)
    {
        var map = new SortedDictionary<string, VideoChannel>(StringComparer.Ordinal);
        foreach (var fr in file.Frames)
        {
            if (!map.TryGetValue(fr.Channel, out var ch))
            {
                ch = new VideoChannel { Name = fr.Channel };
                map[fr.Channel] = ch;
            }
            ch.Frames.Add(fr);
        }

        foreach (var ch in map.Values)
        {
            var flist = ch.Frames.OrderBy(f => f.Offset).ToList();
            ch.Frames.Clear();
            ch.Frames.AddRange(flist);

            ch.FirstTimeUs = flist.Where(f => f.TimeUs > 0).Select(f => f.TimeUs).DefaultIfEmpty(0).Min();
            ch.LastTimeUs = flist.Where(f => f.TimeUs > 0).Select(f => f.TimeUs).DefaultIfEmpty(0).Max();

            var ts = flist.Where(f => f.TimeUs > 0).Select(f => f.TimeUs).ToList();
            ch.Fps = 15.0;
            if (ts.Count >= 2)
            {
                var deltas = new List<double>();
                for (int i = 1; i < ts.Count; i++)
                {
                    double d = (ts[i] - ts[i - 1]) / 1e6;
                    if (d > 0) deltas.Add(d);
                }
                if (deltas.Count > 0)
                {
                    deltas.Sort();
                    double med = deltas[deltas.Count / 2];
                    if (med > 0) ch.Fps = Math.Round(1.0 / med, 3);
                }
            }
            file.Channels.Add(ch);
        }
    }

    private static void LinkAudioToVideo(DvsFile file)
    {
        foreach (var src in file.AudioSources)
        {
            var ch = file.Channels.FirstOrDefault(c =>
                string.Equals(c.Name, src.LinkedChannelName, StringComparison.OrdinalIgnoreCase));
            if (ch is not null)
            {
                ch.AudioSrc = src.Src;
                file.SrcChannelName[src.Src] = ch.Name;
            }
        }
    }

    

    public static DateTime? FiletimeToUtc(ulong ft)
    {
        if (ft == 0) return null;
        try { return Epoch1601.AddTicks((long)ft); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static string DtToStr(DateTime? dt)
        => dt is null ? "" : dt.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
}