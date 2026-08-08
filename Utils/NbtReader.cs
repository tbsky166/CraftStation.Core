using System.Text;

namespace CraftStation.Core.Utils;

public static class NbtReader
{
    public static Dictionary<string, object?> Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var tagType = reader.ReadByte();
        if (tagType == 0)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        ReadName(reader);
        var result = ReadPayload(reader, tagType);
        return result as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private static string ReadName(BinaryReader reader)
    {
        var length = ReadBigEndianUInt16(reader);
        return length == 0 ? "" : Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static object? ReadPayload(BinaryReader reader, byte tagType)
    {
        switch (tagType)
        {
            case 1:
                return reader.ReadSByte();
            case 2:
                return ReadBigEndianInt16(reader);
            case 3:
                return ReadBigEndianInt32(reader);
            case 4:
                return ReadBigEndianInt64(reader);
            case 5:
                return ReadBigEndianSingle(reader);
            case 6:
                return ReadBigEndianDouble(reader);
            case 7:
                return reader.ReadBytes(ReadBigEndianInt32(reader));
            case 8:
            {
                var len = ReadBigEndianUInt16(reader);
                return Encoding.UTF8.GetString(reader.ReadBytes(len));
            }
            case 9:
            {
                var childType = reader.ReadByte();
                var count = ReadBigEndianInt32(reader);
                var list = new List<object?>(count);
                for (var i = 0; i < count; i++)
                    list.Add(ReadPayload(reader, childType));
                return list;
            }
            case 10:
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var childType = reader.ReadByte();
                    if (childType == 0)
                        break;
                    var name = ReadName(reader);
                    dict[name] = ReadPayload(reader, childType);
                }
                return dict;
            }
            case 11:
            {
                var count = ReadBigEndianInt32(reader);
                var arr = new int[count];
                for (var i = 0; i < count; i++)
                    arr[i] = ReadBigEndianInt32(reader);
                return arr;
            }
            case 12:
            {
                var count = ReadBigEndianInt32(reader);
                var arr = new long[count];
                for (var i = 0; i < count; i++)
                    arr[i] = ReadBigEndianInt64(reader);
                return arr;
            }
            default:
                throw new InvalidDataException($"Unsupported NBT tag type {tagType}");
        }
    }

    private static short ReadBigEndianInt16(BinaryReader r)
    {
        var b = r.ReadBytes(2);
        return (short)((b[0] << 8) | b[1]);
    }

    private static ushort ReadBigEndianUInt16(BinaryReader r)
    {
        var b = r.ReadBytes(2);
        return (ushort)((b[0] << 8) | b[1]);
    }

    private static int ReadBigEndianInt32(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    private static long ReadBigEndianInt64(BinaryReader r)
    {
        var b = r.ReadBytes(8);
        long v = 0;
        foreach (var x in b)
            v = (v << 8) | x;
        return v;
    }

    private static float ReadBigEndianSingle(BinaryReader r)
    {
        var b = r.ReadBytes(4);
        Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }

    private static double ReadBigEndianDouble(BinaryReader r)
    {
        var b = r.ReadBytes(8);
        Array.Reverse(b);
        return BitConverter.ToDouble(b, 0);
    }
}
