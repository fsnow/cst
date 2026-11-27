using System;
using System.Buffers;
using System.IO;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// SharePoint's (and OneDrive's) content hash, <c>quickXorHash</c>, which Graph reports for every file as
    /// <c>driveItem.file.hashes.quickXorHash</c>. Computing it locally lets the app tell which version of a
    /// source PDF it has on disk, and so compare it with the server's and pick the page mapping recorded for
    /// that version (<c>Sources.Source.ForVersion</c>). (#1136)
    ///
    /// <para>The hash is 160 bits. Byte <c>i</c> of the file is XORed in at bit <c>(11 × i) mod 160</c>,
    /// wrapping round; then the file's length, as 8 little-endian bytes, is XORed into the last 8 bytes; the
    /// result is base64. Since the bit position depends only on <c>i mod 160</c>, the bytes are first folded
    /// into 160 cells by XOR, and the cells are placed once at the end.</para>
    ///
    /// <para>[observed, 2026-11-26] Matches the server's value for the 1957 Netti and Vimativinodanī-2 scans.</para>
    /// </summary>
    public sealed class QuickXorHash
    {
        private const int Width = 160;   // bits
        private const int Shift = 11;

        private readonly byte[] _cells = new byte[Width];
        private long _length;

        public void Append(ReadOnlySpan<byte> data)
        {
            int cell = (int)(_length % Width);
            foreach (byte b in data)
            {
                _cells[cell] ^= b;
                if (++cell == Width) cell = 0;
            }
            _length += data.Length;
        }

        /// <summary>The base64 hash of everything appended so far, as Graph spells it.</summary>
        public string Finish()
        {
            var bits = new byte[Width / 8];
            for (int k = 0; k < Width; k++)
            {
                byte b = _cells[k];
                if (b == 0) continue;
                int at = (k * Shift) % Width;
                int index = at / 8, offset = at % 8;
                bits[index] ^= (byte)(b << offset);
                if (offset != 0)
                    bits[(index + 1) % bits.Length] ^= (byte)(b >> (8 - offset));   // the top cell wraps to bit 0
            }
            for (int i = 0; i < 8; i++)
                bits[bits.Length - 8 + i] ^= (byte)(_length >> (8 * i));
            return Convert.ToBase64String(bits);
        }

        public static string Compute(ReadOnlySpan<byte> data)
        {
            var h = new QuickXorHash();
            h.Append(data);
            return h.Finish();
        }

        public static string ComputeFile(string path)
        {
            var h = new QuickXorHash();
            var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                int n;
                while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
                    h.Append(buffer.AsSpan(0, n));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            return h.Finish();
        }
    }
}
