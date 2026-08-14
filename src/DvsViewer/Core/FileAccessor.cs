using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;

namespace DvsViewer.Core;






internal sealed class FileAccessor : IDisposable
{
    
    private const long InMemoryLimit = (1L << 30) + (1L << 29);

    private readonly byte[]? _arr;
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _acc;
    private byte[]? _scanBuf;

    public long Length { get; }

    private FileAccessor(byte[]? arr, MemoryMappedFile? mmf, MemoryMappedViewAccessor? acc, long length)
    {
        _arr = arr;
        _mmf = mmf;
        _acc = acc;
        Length = length;
    }

    public static FileAccessor Open(string path)
    {
        long len = new FileInfo(path).Length;
        if (len <= InMemoryLimit)
        {
            try { return new FileAccessor(File.ReadAllBytes(path), null, null, len); }
            catch {  }
        }
        var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        var acc = mmf.CreateViewAccessor(0, len, MemoryMappedFileAccess.Read);
        return new FileAccessor(null, mmf, acc, len);
    }

    public void Dispose()
    {
        _acc?.Dispose();
        _mmf?.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte(long o)
    {
        if (_arr is not null) return _arr[(int)o];
        return _acc!.ReadByte(o);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort U16(long o) => (ushort)(ReadByte(o) | (ReadByte(o + 1) << 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint U32(long o) =>
        (uint)(ReadByte(o) | (ReadByte(o + 1) << 8) | (ReadByte(o + 2) << 16) | (ReadByte(o + 3) << 24));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong U64(long o) => U32(o) | ((ulong)U32(o + 4) << 32);

    private double F64(long o) => BitConverter.Int64BitsToDouble(unchecked((long)U64(o)));

    public double[] FourDoubles(long o) => new[] { F64(o), F64(o + 8), F64(o + 16), F64(o + 24) };

    
    public long IndexOf(byte[] needle, long start, long end)
    {
        if (needle.Length == 0 || Length == 0) return -1;
        start = Math.Max(0, start);
        end = Math.Min(end, Length);
        if (end <= start) return -1;
        if (_arr is not null)
        {
            int idx = _arr.AsSpan((int)start, (int)(end - start)).IndexOf(needle);
            return idx < 0 ? -1 : start + idx;
        }
        const int Chunk = 16 * 1024 * 1024;
        int overlap = needle.Length - 1;
        var buf = _scanBuf ??= new byte[Chunk + 64];
        long i = start;
        while (i < end)
        {
            long n = Math.Min(Chunk, end - i);
            _acc!.ReadArray(i, buf, 0, (int)n);
            int idx = ((ReadOnlySpan<byte>)buf).Slice(0, (int)n).IndexOf(needle);
            if (idx >= 0) return i + idx;
            if (n < Chunk) return -1;
            i += n - overlap;
        }
        return -1;
    }

    public bool EqualsAt(long off, byte[] pat)
    {
        if (off + pat.Length > Length) return false;
        for (int i = 0; i < pat.Length; i++)
            if (ReadByte(off + i) != pat[i]) return false;
        return true;
    }

    public List<long> FindOccurrences(byte[] needle, long from, long to)
    {
        var res = new List<long>();
        if (needle.Length == 0 || to <= from) return res;
        from = Math.Max(0, from);
        to = Math.Min(to, Length);
        if (to <= from) return res;
        if (_arr is not null)
        {
            var span = _arr.AsSpan((int)from, (int)(to - from));
            int i = 0;
            while (true)
            {
                int idx = span.Slice(i).IndexOf(needle);
                if (idx < 0) break;
                i += idx;
                res.Add(from + i);
                i += needle.Length;
            }
            return res;
        }
        const int Chunk = 16 * 1024 * 1024;
        var buf = _scanBuf ??= new byte[Chunk + 64];
        long pos = from;
        while (pos < to)
        {
            long n = Math.Min(Chunk, to - pos);
            _acc!.ReadArray(pos, buf, 0, (int)n);
            int si = 0;
            while (true)
            {
                int idx = ((ReadOnlySpan<byte>)buf).Slice(si, (int)n - si).IndexOf(needle);
                if (idx < 0) break;
                si += idx;
                res.Add(pos + si);
                si += needle.Length;
            }
            long adv = n - (needle.Length - 1);
            if (adv <= 0) break;
            pos += adv;
        }
        return res;
    }

    public void CopyTo(long src, byte[] dst, int dstOff, int len)
    {
        if (_arr is not null) { Array.Copy(_arr, (int)src, dst, dstOff, len); return; }
        _acc!.ReadArray(src, dst, dstOff, len);
    }

    public bool AllPrintable(long off, int len)
    {
        for (int i = 0; i < len; i++)
        {
            byte c = ReadByte(off + i);
            if (c < 32 || c >= 127) return false;
        }
        return true;
    }

    
    public int TrimEndLen(long off, int len)
    {
        for (int i = len - 1; i >= 0; i--)
            if (ReadByte(off + i) != 0) return i + 1;
        return 0;
    }

    public string Latin1(long off, int len)
    {
        var tmp = new byte[len];
        CopyTo(off, tmp, 0, len);
        return Encoding.Latin1.GetString(tmp);
    }

    public string Ascii(long off, int len)
    {
        var tmp = new byte[len];
        CopyTo(off, tmp, 0, len);
        return Encoding.ASCII.GetString(tmp);
    }
}