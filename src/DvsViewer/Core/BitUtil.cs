using System.Runtime.CompilerServices;

namespace DvsViewer.Core;


internal static class BitUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I32(byte[] b, int o) => unchecked((int)U32(b, o));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong U64(byte[] b, int o) =>
        (ulong)U32(b, o) | ((ulong)U32(b, o + 4) << 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double F64(byte[] b, int o) => BitConverter.Int64BitsToDouble(unchecked((long)U64(b, o)));

    public static double[] FourDoubles(byte[] b, int o) => new[]
    {
        F64(b, o), F64(b, o + 8), F64(b, o + 16), F64(b, o + 24),
    };

    
    public static int IndexOf(byte[] hay, byte[] needle, int start, int end)
    {
        if (hay.Length == 0 || needle.Length == 0) return -1;
        start = Math.Max(0, start);
        end = Math.Min(end, hay.Length);
        if (needle.Length == 1)
        {
            if (end <= start) return -1;
            return Array.IndexOf(hay, needle[0], start, end - start);
        }
        int last = end - needle.Length;
        if (last < start) return -1;
        for (int i = start; i <= last; i++)
        {
            if (hay[i] != needle[0]) continue;
            bool ok = true;
            for (int j = 1; j < needle.Length; j++)
            {
                if (hay[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    public static bool AllPrintable(ReadOnlySpan<byte> s)
    {
        foreach (var c in s)
        {
            if (c < 32 || c >= 127) return false;
        }
        return true;
    }
}