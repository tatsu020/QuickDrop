using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace QuickDrop.Core;

public static class ProtocolIO
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(QuickDropConstants.ProtocolMagic);

    public static async Task WriteJsonAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(MagicBytes, cancellationToken).ConfigureAwait(false);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Default);
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, jsonBytes.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(jsonBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<T> ReadJsonAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var magic = await ReadExactlyAsync(stream, MagicBytes.Length, cancellationToken).ConfigureAwait(false);
        if (!magic.SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid QuickDrop protocol magic.");
        }

        var lengthBytes = await ReadExactlyAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (length <= 0 || length > 1024 * 1024)
        {
            throw new InvalidDataException("Invalid QuickDrop JSON header length.");
        }

        var jsonBytes = await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<T>(jsonBytes, JsonOptions.Default);
        return result ?? throw new InvalidDataException("QuickDrop JSON header was empty.");
    }

    public static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of QuickDrop stream.");
            }

            offset += read;
        }

        return buffer;
    }
}
