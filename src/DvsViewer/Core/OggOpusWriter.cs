using System.IO;

namespace DvsViewer.Core;





public static class OggOpusWriter
{
    public static byte[] BuildOggOpus(IReadOnlyList<byte[]> packets, int channels = 1, int rate = 8000)
    {
        var headPacket = BuildOpusHead(channels, rate);
        var tagsPacket = BuildOpusTags();
        const uint serial = 0x44565331; 

        using var outStream = new MemoryStream();
        outStream.Write(_OggPage(serial, 0, 0, new[] { headPacket }, 0x02));
        outStream.Write(_OggPage(serial, 1, 0, new[] { tagsPacket }, 0x00));

        long granule = 0;
        uint sequence = 2;
        for (int first = 0; first < packets.Count;)
        {
            var pagePackets = new List<byte[]>(50);
            int segments = 0;
            while (first < packets.Count && pagePackets.Count < 50)
            {
                var packet = packets[first];
                int packetSegments = packet.Length / 255 + 1;
                if (segments + packetSegments > 255 && pagePackets.Count > 0) break;
                pagePackets.Add(packet);
                segments += packetSegments;
                granule += PacketDuration48k(packet);
                first++;
            }

            byte headerType = (byte)(first == packets.Count ? 0x04 : 0x00);
            outStream.Write(_OggPage(serial, sequence++, granule, pagePackets, headerType));
        }
        return outStream.ToArray();
    }

    private static int PacketDuration48k(byte[] packet)
    {
        if (packet.Length == 0) return 960;

        int toc = packet[0];
        int config = toc >> 3;
        int frameDuration48k = config >= 16
            ? 120 << (config & 3)                 // 2.5, 5, 10, or 20 ms
            : config >= 12
                ? ((config & 1) == 0 ? 480 : 960) // 10 or 20 ms
                : (config & 3) switch             // 10, 20, 40, or 60 ms
                {
                    0 => 480,
                    1 => 960,
                    2 => 1920,
                    _ => 2880,
                };

        int frameCount = (toc & 3) switch
        {
            0 => 1,
            1 or 2 => 2,
            _ => packet.Length > 1 ? packet[1] & 0x3f : 0,
        };
        int duration = frameDuration48k * frameCount;
        return duration is > 0 and <= 5760 ? duration : 960;
    }

    private static byte[] BuildOpusHead(int channels, int rate)
    {
        
        using var ms = new MemoryStream(19);
        var w = new BinaryWriter(ms);
        w.Write("OpusHead"u8);
        w.Write((byte)1);          
        w.Write((byte)channels);
        w.Write((ushort)0);        
        w.Write((uint)rate);       
        w.Write((short)0);         
        w.Write((byte)0);          
        return ms.ToArray();
    }

    private static byte[] BuildOpusTags()
    {
        using var ms = new MemoryStream(23);
        var w = new BinaryWriter(ms);
        w.Write("OpusTags"u8);
        w.Write((uint)8);
        w.Write("dvs_ext "u8);
        return ms.ToArray();
    }

    private static byte[] _OggPage(uint serial, uint seq, long granule, IReadOnlyList<byte[]> packets, byte headerType)
    {
        var lacing = new List<byte>();
        foreach (var p in packets)
        {
            int n = p.Length;
            while (n >= 255) { lacing.Add(255); n -= 255; }
            lacing.Add((byte)n);
        }

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("OggS"u8);
        w.Write((byte)0);
        w.Write(headerType);
        w.Write(granule);
        w.Write(serial);
        w.Write(seq);
        w.Write((uint)0); 
        w.Write((byte)lacing.Count);
        foreach (var l in lacing) w.Write(l);
        foreach (var p in packets) w.Write(p);

        var page = ms.ToArray();
        uint crc = _OggCrc(page);
        page[22] = (byte)crc;
        page[23] = (byte)(crc >> 8);
        page[24] = (byte)(crc >> 16);
        page[25] = (byte)(crc >> 24);
        return page;
    }

    private static uint _OggCrc(byte[] data)
    {
        uint crc = 0;
        foreach (var b in data)
        {
            crc ^= (uint)b << 24;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x80000000u) != 0)
                    crc = ((crc << 1) ^ 0x04C11DB7u) & 0xFFFFFFFFu;
                else
                    crc = (crc << 1) & 0xFFFFFFFFu;
            }
        }
        return crc;
    }
}
