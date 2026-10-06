using System.Text;

namespace MovieRental.Modules.Cinema.Infrastructure;

/// <summary>
/// A small QR Code encoder: byte mode, error-correction level M, versions 1 to 10 (up to 213
/// bytes). That covers every ticket payload by a wide margin, and it lets the server draw the
/// same QR code the browser shows — inside the PDF ticket it e-mails — without a package.
///
/// Follows ISO/IEC 18004 closely enough for any phone camera or scanner to read the result;
/// the structure mirrors Project Nayuki's reference implementation (MIT), cut down to the one
/// mode and one level this project needs.
/// </summary>
public sealed class QrCode
{
    public const int MinVersion = 1;
    public const int MaxVersion = 10;

    // Level M, indexed by version (0 unused).
    private static readonly int[] EccCodewordsPerBlock = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] NumErrorCorrectionBlocks = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private const int EccFormatBitsM = 0;

    public int Version { get; }
    public int Size { get; }
    public int Mask { get; private set; }

    private readonly bool[,] _modules;     // [y, x]; true = dark
    private readonly bool[,] _isFunction;

    public bool this[int x, int y] => x >= 0 && x < Size && y >= 0 && y < Size && _modules[y, x];

    public static QrCode EncodeText(string text) => EncodeBytes(Encoding.UTF8.GetBytes(text));

    public static QrCode EncodeBytes(byte[] data)
    {
        for (var version = MinVersion; version <= MaxVersion; version++)
        {
            var capacityBits = DataCodewords(version) * 8;
            var countBits = version <= 9 ? 8 : 16;
            var needed = 4 + countBits + data.Length * 8;
            if (needed > capacityBits) continue;

            var bits = new BitBuffer();
            bits.Append(0b0100, 4);                    // byte mode
            bits.Append(data.Length, countBits);
            foreach (var b in data) bits.Append(b, 8);

            bits.Append(0, Math.Min(4, capacityBits - bits.Count));   // terminator
            bits.Append(0, (8 - bits.Count % 8) % 8);                  // byte align
            for (var pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11)
                bits.Append(pad, 8);

            return new QrCode(version, bits.ToBytes());
        }

        throw new ArgumentException($"Too long for a version {MaxVersion} QR code ({data.Length} bytes).", nameof(data));
    }

    private QrCode(int version, byte[] dataCodewords)
    {
        Version = version;
        Size = version * 4 + 17;
        _modules = new bool[Size, Size];
        _isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        var allCodewords = AddEccAndInterleave(dataCodewords);
        DrawCodewords(allCodewords);

        // Try every mask and keep the one with the lowest penalty, as the standard asks.
        var best = 0;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            var penalty = Penalty();
            if (penalty < bestPenalty) { best = mask; bestPenalty = penalty; }
            ApplyMask(mask);                         // XOR again to undo
        }

        Mask = best;
        ApplyMask(best);
        DrawFormatBits(best);
    }

    // ---------------------------------------------------------------- function patterns

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions(Version);
        var n = positions.Length;
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)) continue;
                DrawAlignment(positions[i], positions[j]);
            }

        DrawFormatBits(0);   // reserves the area; overwritten once the mask is chosen
        DrawVersion();
    }

    private void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                    SetFunction(xx, yy, dist != 2 && dist != 4);
            }
    }

    private void DrawAlignment(int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    private void DrawFormatBits(int mask)
    {
        var data = EccFormatBitsM << 3 | mask;
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = (data << 10 | rem) ^ 0x5412;

        for (var i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (var i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

        for (var i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
        for (var i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
        SetFunction(8, Size - 8, true);   // the dark module
    }

    private void DrawVersion()
    {
        if (Version < 7) return;

        var rem = Version;
        for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var bits = Version << 12 | rem;

        for (var i = 0; i < 18; i++)
        {
            var bit = Bit(bits, i);
            int a = Size - 11 + i % 3, b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    internal static int[] AlignmentPositions(int version)
    {
        if (version == 1) return [];
        var numAlign = version / 7 + 2;
        var step = (version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;
        var result = new int[numAlign];
        result[0] = 6;
        for (int i = numAlign - 1, pos = version * 4 + 10; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    private void SetFunction(int x, int y, bool dark)
    {
        _modules[y, x] = dark;
        _isFunction[y, x] = true;
    }

    // ---------------------------------------------------------------- codewords

    internal static int RawDataModules(int version)
    {
        var result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            var numAlign = version / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    internal static int DataCodewords(int version) =>
        RawDataModules(version) / 8 - EccCodewordsPerBlock[version] * NumErrorCorrectionBlocks[version];

    private byte[] AddEccAndInterleave(byte[] data)
    {
        var numBlocks = NumErrorCorrectionBlocks[Version];
        var blockEccLen = EccCodewordsPerBlock[Version];
        var rawCodewords = RawDataModules(Version) / 8;
        var numShortBlocks = numBlocks - rawCodewords % numBlocks;
        var shortBlockLen = rawCodewords / numBlocks;

        var divisor = ReedSolomonDivisor(blockEccLen);
        var blocks = new List<byte[]>();

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            var datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            var dat = data.AsSpan(k, datLen).ToArray();
            k += datLen;

            var ecc = ReedSolomonRemainder(dat, divisor);
            var block = new byte[shortBlockLen + 1];
            dat.CopyTo(block, 0);
            // Short blocks carry one unused slot so every block has the same length.
            ecc.CopyTo(block, block.Length - blockEccLen);
            blocks.Add(block);
        }

        var result = new List<byte>(rawCodewords);
        for (var i = 0; i < blocks[0].Length; i++)
            for (var j = 0; j < blocks.Count; j++)
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result.Add(blocks[j][i]);

        return [.. result];
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static int Multiply(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z & 0xFF;
    }

    private void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vert : vert;
                    if (!_isFunction[y, x] && i < data.Length * 8)
                    {
                        _modules[y, x] = Bit(data[i >> 3], 7 - (i & 7));
                        i++;
                    }
                }
        }
    }

    // ---------------------------------------------------------------- masking

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                if (_isFunction[y, x]) continue;
                var invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                };
                if (invert) _modules[y, x] = !_modules[y, x];
            }
    }

    /// <summary>Penalty rules 1, 2 and 4 of the standard (runs, 2×2 blocks, dark balance).
    /// Rule 3, the finder-lookalike search, is left out: any mask decodes correctly, and the
    /// other three already steer away from the patterns that trouble cheap scanners.</summary>
    private int Penalty()
    {
        var result = 0;

        for (var y = 0; y < Size; y++)
        {
            int runX = 1, runY = 1;
            for (var x = 1; x < Size; x++)
            {
                if (_modules[y, x] == _modules[y, x - 1]) { runX++; if (runX == 5) result += 3; else if (runX > 5) result++; }
                else runX = 1;
                if (_modules[x, y] == _modules[x - 1, y]) { runY++; if (runY == 5) result += 3; else if (runY > 5) result++; }
                else runY = 1;
            }
        }

        for (var y = 0; y < Size - 1; y++)
            for (var x = 0; x < Size - 1; x++)
            {
                var c = _modules[y, x];
                if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) result += 3;
            }

        var dark = 0;
        foreach (var m in _modules) if (m) dark++;
        var total = Size * Size;
        var k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        result += Math.Max(0, k) * 10;

        return result;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    private sealed class BitBuffer
    {
        private readonly List<bool> _bits = [];
        public int Count => _bits.Count;

        public void Append(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--) _bits.Add(((value >> i) & 1) != 0);
        }

        public byte[] ToBytes()
        {
            var result = new byte[(_bits.Count + 7) / 8];
            for (var i = 0; i < _bits.Count; i++)
                if (_bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
            return result;
        }
    }
}
