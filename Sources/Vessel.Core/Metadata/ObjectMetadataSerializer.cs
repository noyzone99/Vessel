using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using ZoneTree.Serializers;

namespace Vessel.Metadata;

/// <summary>
/// Serializes and deserializes <see cref="ObjectMetadata"/> values for the ZoneTree metadata databases.
/// </summary>
internal class ObjectMetadataSerializer : ISerializer<ObjectMetadata>
{
    /// <summary>
    /// Converts an object metadata value into an immutable binary payload suitable for storage.
    /// </summary>
    /// <param name="entry">The metadata entry to serialize.</param>
    /// <returns>A binary representation of the supplied metadata.</returns>
    public Memory<byte> Serialize(in ObjectMetadata entry)
    {
        int requiredSize = GetRequiredByteCount(entry);

        var buffer = new byte[requiredSize];
        Span<byte> destination = buffer.AsSpan();

        int bytesWritten = 0;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.OriginalSize);
        bytesWritten += 8;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.CompressedSize);
        bytesWritten += 8;

        BinaryPrimitives.WriteInt32LittleEndian(destination[bytesWritten..], (int)entry.CompressionType);
        bytesWritten += 4;

        BinaryPrimitives.WriteInt64LittleEndian(destination[bytesWritten..], entry.CreationDate.ToUnixTimeMilliseconds());
        bytesWritten += 8;

        WriteString(entry.Key, destination, ref bytesWritten);
        WriteString(entry.Hash, destination, ref bytesWritten);
        WriteString(entry.ETag, destination, ref bytesWritten);
        WriteString(entry.FileName, destination, ref bytesWritten);
        WriteString(entry.Extension, destination, ref bytesWritten);
        WriteString(entry.MimeType, destination, ref bytesWritten);

        return buffer.AsMemory(0, bytesWritten);
    }

    /// <summary>
    /// Restores an object metadata value from its binary representation.
    /// </summary>
    /// <param name="bytes">The serialized metadata bytes to decode.</param>
    /// <returns>The deserialized metadata entry.</returns>
    public ObjectMetadata Deserialize(Memory<byte> bytes)
    {
        ReadOnlySpan<byte> source = bytes.Span;
        if (source.Length < 52) throw new InvalidDataException("The metadata record is incomplete.");
        int bytesRead = 0;

        long originalSize = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        bytesRead += 8;

        long compressedSize = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        bytesRead += 8;

        CompressionType compressionType = (CompressionType)BinaryPrimitives.ReadInt32LittleEndian(source[bytesRead..]);
        bytesRead += 4;
        if (originalSize < 0 || compressedSize < 0)
            throw new InvalidDataException("The metadata record contains an invalid size.");

        long lastModifiedMs = BinaryPrimitives.ReadInt64LittleEndian(source[bytesRead..]);
        DateTimeOffset creationDate;
        try { creationDate = DateTimeOffset.FromUnixTimeMilliseconds(lastModifiedMs); }
        catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("The metadata timestamp is invalid.", error); }
        bytesRead += 8;

        string key = ReadString(source, ref bytesRead)!;
        string hash = ReadString(source, ref bytesRead)!;
        string eTag = ReadString(source, ref bytesRead)!;
        string? fileName = ReadString(source, ref bytesRead);
        string? extension = ReadString(source, ref bytesRead);
        string? mimeType = ReadString(source, ref bytesRead);
        if (bytesRead != source.Length || (key is not null && (hash is null || eTag is null)))
            throw new InvalidDataException("The metadata record has an invalid layout.");

        return new ObjectMetadata(
            Key: key!,
            Hash: hash,
            OriginalSize: originalSize,
            CompressionType: compressionType,
            ETag: eTag,
            CreationDate: creationDate,
            FileName: fileName,
            Extension: extension,
            MimeType: mimeType,
            CompressedSize: compressedSize
        );
    }

    #region Helpers di Scrittura e Lettura

    /// <summary>
    /// Writes a nullable string to the binary buffer using a length-prefixed UTF-8 encoding.
    /// </summary>
    /// <param name="value">The string value to write, or <see langword="null"/> for a null marker.</param>
    /// <param name="destination">The destination buffer that receives the serialized bytes.</param>
    /// <param name="bytesWritten">The current byte offset in the destination buffer.</param>
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

    /// <summary>
    /// Reads a nullable string from the binary buffer using the length-prefixed UTF-8 encoding.
    /// </summary>
    /// <param name="source">The source buffer that contains the serialized data.</param>
    /// <param name="bytesRead">The current byte offset in the source buffer.</param>
    /// <returns>The value encoded in the buffer, or <see langword="null"/> when the entry is absent.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? ReadString(ReadOnlySpan<byte> source, ref int bytesRead)
    {
        if (source.Length - bytesRead < 4) throw new InvalidDataException("The metadata string length is missing.");
        int length = BinaryPrimitives.ReadInt32LittleEndian(source[bytesRead..]);
        bytesRead += 4;

        if (length == -1)
        {
            return null;
        }

        if (length < 0 || length > source.Length - bytesRead) throw new InvalidDataException("The metadata string length is invalid.");

        string value = Encoding.UTF8.GetString(source.Slice(bytesRead, length));
        bytesRead += length;
        return value;
    }

    /// <summary>
    /// Determines the number of bytes required to serialize the supplied metadata value.
    /// </summary>
    /// <param name="metadata">The metadata value whose serialized originalSize should be calculated.</param>
    /// <returns>The total number of bytes required by the serialization format.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetRequiredByteCount(in ObjectMetadata metadata)
    {
        return checked(28 + GetStringByteCount(metadata.Key) + GetStringByteCount(metadata.Hash) +
            GetStringByteCount(metadata.ETag) + GetStringByteCount(metadata.FileName) +
            GetStringByteCount(metadata.Extension) + GetStringByteCount(metadata.MimeType));
    }

    /// <summary>
    /// Computes the serialized byte originalSize of a nullable string value.
    /// </summary>
    /// <param name="str">The string value to measure.</param>
    /// <returns>The number of bytes required to encode the supplied string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStringByteCount(string? str)
        => str is null ? 4 : checked(4 + Encoding.UTF8.GetByteCount(str));

    #endregion
}
