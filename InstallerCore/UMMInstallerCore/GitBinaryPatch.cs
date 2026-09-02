using System.IO.Compression;
using System.Text;

namespace UMMInstallerCore;

internal static class GitBinaryPatch
{
    private const string Base85Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~";
    private static readonly int[] Base85Decode = BuildBase85Decode();

    public static byte[] ApplyForwardDelta(byte[] source, string patchPath)
    {
        string[] lines = File.ReadAllLines(patchPath, Encoding.ASCII);
        int marker = Array.IndexOf(lines, "GIT binary patch");
        if (marker < 0)
            throw new InvalidDataException("Missing GIT binary patch marker.");

        int index = marker + 1;
        while (index < lines.Length && lines[index].Length == 0)
            index++;

        if (index >= lines.Length || !lines[index].StartsWith("delta ", StringComparison.Ordinal))
            throw new InvalidDataException("Forward binary patch block is not a delta block.");

        if (!int.TryParse(lines[index].AsSpan(6), out int declaredDeltaSize) || declaredDeltaSize < 0)
            throw new InvalidDataException("Invalid delta block size.");
        index++;

        using MemoryStream compressed = new();
        while (index < lines.Length && lines[index].Length != 0)
        {
            if (lines[index].StartsWith("delta ", StringComparison.Ordinal) ||
                lines[index].StartsWith("literal ", StringComparison.Ordinal))
                break;

            byte[] decodedLine = DecodeBase85Line(lines[index]);
            compressed.Write(decodedLine);
            index++;
        }

        compressed.Position = 0;
        using MemoryStream deltaStream = new();
        using (ZLibStream inflater = new(compressed, CompressionMode.Decompress, leaveOpen: true))
            inflater.CopyTo(deltaStream);

        byte[] delta = deltaStream.ToArray();
        if (delta.Length != declaredDeltaSize)
            throw new InvalidDataException($"Inflated delta size mismatch: {delta.Length} != {declaredDeltaSize}.");

        return ApplyGitDelta(source, delta);
    }

    private static byte[] DecodeBase85Line(string line)
    {
        if (line.Length < 2)
            throw new InvalidDataException("Invalid base85 line.");

        int decodedLength = line[0] switch
        {
            >= 'A' and <= 'Z' => line[0] - 'A' + 1,
            >= 'a' and <= 'z' => line[0] - 'a' + 27,
            _ => throw new InvalidDataException("Invalid base85 line length marker.")
        };

        string encoded = line[1..];
        int groups = (decodedLength + 3) / 4;
        if (encoded.Length != groups * 5)
            throw new InvalidDataException("Invalid base85 encoded line length.");

        byte[] result = new byte[decodedLength];
        int written = 0;
        Span<byte> group = stackalloc byte[4];
        for (int i = 0; i < encoded.Length; i += 5)
        {
            uint accumulator = 0;
            checked
            {
                for (int j = 0; j < 5; j++)
                {
                    char ch = encoded[i + j];
                    int value = ch < Base85Decode.Length ? Base85Decode[ch] : -1;
                    if (value < 0)
                        throw new InvalidDataException($"Invalid base85 character: {ch}");
                    accumulator = accumulator * 85u + (uint)value;
                }
            }

            group[0] = (byte)(accumulator >> 24);
            group[1] = (byte)(accumulator >> 16);
            group[2] = (byte)(accumulator >> 8);
            group[3] = (byte)accumulator;

            int count = Math.Min(4, decodedLength - written);
            group[..count].CopyTo(result.AsSpan(written));
            written += count;
        }

        return result;
    }

    private static byte[] ApplyGitDelta(byte[] source, byte[] delta)
    {
        int position = 0;
        ulong baseSize = ReadVariableLengthInteger(delta, ref position);
        ulong resultSize = ReadVariableLengthInteger(delta, ref position);

        if (baseSize != (ulong)source.LongLength)
            throw new InvalidDataException($"Delta base size mismatch: {baseSize} != {source.LongLength}.");
        if (resultSize > int.MaxValue)
            throw new InvalidDataException("Delta result is too large for this installer build.");

        using MemoryStream output = new((int)resultSize);

        while (position < delta.Length)
        {
            byte command = delta[position++];
            if ((command & 0x80) != 0)
            {
                uint offset = 0;
                uint size = 0;

                if ((command & 0x01) != 0) offset |= ReadByte(delta, ref position);
                if ((command & 0x02) != 0) offset |= (uint)ReadByte(delta, ref position) << 8;
                if ((command & 0x04) != 0) offset |= (uint)ReadByte(delta, ref position) << 16;
                if ((command & 0x08) != 0) offset |= (uint)ReadByte(delta, ref position) << 24;
                if ((command & 0x10) != 0) size |= ReadByte(delta, ref position);
                if ((command & 0x20) != 0) size |= (uint)ReadByte(delta, ref position) << 8;
                if ((command & 0x40) != 0) size |= (uint)ReadByte(delta, ref position) << 16;
                if (size == 0) size = 0x10000;

                ulong end = (ulong)offset + size;
                if (end > (ulong)source.LongLength)
                    throw new InvalidDataException("Delta copy instruction reads outside source data.");

                output.Write(source, checked((int)offset), checked((int)size));
            }
            else if (command != 0)
            {
                int literalSize = command;
                if (position + literalSize > delta.Length)
                    throw new InvalidDataException("Delta literal instruction is truncated.");

                output.Write(delta, position, literalSize);
                position += literalSize;
            }
            else
            {
                throw new InvalidDataException("Reserved Git delta opcode 0 encountered.");
            }
        }

        if ((ulong)output.Length != resultSize)
            throw new InvalidDataException($"Delta output size mismatch: {output.Length} != {resultSize}.");

        return output.ToArray();
    }

    private static ulong ReadVariableLengthInteger(byte[] data, ref int position)
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte current = ReadByte(data, ref position);
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return value;

            shift += 7;
            if (shift > 63)
                throw new InvalidDataException("Oversized Git delta variable-length integer.");
        }
    }

    private static byte ReadByte(byte[] data, ref int position)
    {
        if ((uint)position >= (uint)data.Length)
            throw new InvalidDataException("Unexpected end of Git delta data.");
        return data[position++];
    }

    private static int[] BuildBase85Decode()
    {
        int[] table = Enumerable.Repeat(-1, 128).ToArray();
        for (int i = 0; i < Base85Alphabet.Length; i++)
            table[Base85Alphabet[i]] = i;
        return table;
    }
}
