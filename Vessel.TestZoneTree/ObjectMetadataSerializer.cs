using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Vessel.Models;
using ZoneTree.Serializers;

namespace Vessel.TestZoneTree;

public class ObjectMetadataSerializer : ISerializer<ObjectMetadata>
{
    public Memory<byte> Serialize(in ObjectMetadata entry)
    {
        int requiredSize = GetRequiredByteCount(entry);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(requiredSize);
        Span<byte> destination = buffer;

        int bytesWritten = 0;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.Size);
        bytesWritten += 8;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.CompressedSize);
        bytesWritten += 8;

        BinaryPrimitives.WriteInt32LittleEndian(destination[bytesWritten..], (int)entry.CompressionType);
        bytesWritten += 4;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.LastModified.ToUnixTimeMilliseconds());
        bytesWritten += 8;

        WriteString(entry.Key, destination, ref bytesWritten);
        WriteString(entry.Hash, destination, ref bytesWritten);
        WriteString(entry.ETag, destination, ref bytesWritten);
        WriteString(entry.FileName, destination, ref bytesWritten);
        WriteString(entry.Extension, destination, ref bytesWritten);
        WriteString(entry.MimeType, destination, ref bytesWritten);

        return buffer.AsMemory(0, bytesWritten);
    }

    public ObjectMetadata Deserialize(Memory<byte> bytes)
    {
        ReadOnlySpan<byte> source = bytes.Span;
        int bytesRead = 0;

        long size = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        bytesRead += 8;

        long compressedSize = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        bytesRead += 8;

        CompressionType compressionType = (CompressionType)BinaryPrimitives.ReadInt32LittleEndian(source[bytesRead..]);
        bytesRead += 4;

        long lastModifiedMs = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        DateTimeOffset lastModified = DateTimeOffset.FromUnixTimeMilliseconds(lastModifiedMs);
        bytesRead += 8;

        string key = ReadString(source, ref bytesRead)!;
        string hash = ReadString(source, ref bytesRead)!;
        string eTag = ReadString(source, ref bytesRead)!;
        string? fileName = ReadString(source, ref bytesRead);
        string? extension = ReadString(source, ref bytesRead);
        string? mimeType = ReadString(source, ref bytesRead);

        return new ObjectMetadata(
            Key: key,
            Hash: hash,
            Size: size,
            CompressionType: compressionType,
            ETag: eTag,
            LastModified: lastModified,
            FileName: fileName,
            Extension: extension,
            MimeType: mimeType,
            CompressedSize: compressedSize
        );
    }

    #region Helpers di Scrittura e Lettura

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteString(string? value, Span<byte> destination, ref int bytesWritten)
    {
        if (value is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[bytesWritten..], -1);
            bytesWritten += 4;
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        BinaryPrimitives.WriteInt32LittleEndian(destination[bytesWritten..], byteCount);
        bytesWritten += 4;

        Encoding.UTF8.GetBytes(value, destination.Slice(bytesWritten, byteCount));
        bytesWritten += byteCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? ReadString(ReadOnlySpan<byte> source, ref int bytesRead)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(source[bytesRead..]);
        bytesRead += 4;

        if (length == -1)
        {
            return null;
        }

        if (length == 0)
        {
            return string.Empty;
        }

        string value = Encoding.UTF8.GetString(source.Slice(bytesRead, length));
        bytesRead += length;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetRequiredByteCount(in ObjectMetadata metadata)
    {
        int size = 28;
        size += GetStringByteCount(metadata.Key);
        size += GetStringByteCount(metadata.Hash);
        size += GetStringByteCount(metadata.ETag);
        size += GetStringByteCount(metadata.FileName);
        size += GetStringByteCount(metadata.Extension);
        size += GetStringByteCount(metadata.MimeType);
        return size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStringByteCount(string? str)
        => str is null ? 4 : 4 + Encoding.UTF8.GetByteCount(str);

    #endregion
}