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
        for (int i = 0; i < packets.Count; i++)
        {
            granule += 160;
            byte headerType = (byte)(i == packets.Count - 1 ? 0x04 : 0x00);
            outStream.Write(_OggPage(serial, (uint)(i + 2), granule, new[] { packets[i] }, headerType));
        }
        return outStream.ToArray();
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