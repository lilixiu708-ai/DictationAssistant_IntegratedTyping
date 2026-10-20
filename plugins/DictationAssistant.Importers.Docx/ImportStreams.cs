// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: share bounded input copying across document formats.
using System.Buffers;

namespace DictationAssistant.Importers.Docx;

/// <summary>Materializes bounded input without taking ownership of the caller's stream.</summary>
internal static class ImportStreams
{
    public static async Task<MemoryStream> CopyBoundedAsync(
        Stream input, long maximum, string error, CancellationToken cancellationToken)
    {
        var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    break;
                if (output.Length + count > maximum)
                    throw new InvalidDataException(error);
                output.Write(buffer, 0, count);
            }
            output.Position = 0;
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
