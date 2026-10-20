// Bounded plain-text word-list reading, 2026-10-20. AGPL-3.0-or-later.
using System.Text;

namespace DictationAssistant.App.Importing;

internal static class WordListTextReader
{
    private const string TooLargeMessage = "词表超过 64 MiB，请拆分后再打开。";

    public static Task<string> ReadAsync(Func<Task<Stream>> openReadAsync, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = await openReadAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!stream.CanRead)
                throw new InvalidDataException("无法读取这个文件。");
            if (stream.CanSeek && stream.Length > DocumentImportService.MaximumInputBytes)
                throw new InvalidDataException(TooLargeMessage);

            // Enforce the same byte limit for file-provider streams without Length.
            // The outer scope owns the source; disposing the reader only closes its wrapper.
            using var limited = new LimitedReadStream(stream);
            using var reader = new StreamReader(limited, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            if (text.Contains('\0'))
                throw new InvalidDataException("这个文件包含二进制内容，请使用对应的文档导入插件。");
            return text;
        }, cancellationToken);

    private sealed class LimitedReadStream(Stream source) : Stream
    {
        private long _bytesRead;

        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = source.Read(buffer[..ReadSize(buffer.Length)]);
            CountBytes(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await source.ReadAsync(buffer[..ReadSize(buffer.Length)], cancellationToken).ConfigureAwait(false);
            CountBytes(read);
            return read;
        }

        private int ReadSize(int requested) =>
            (int)Math.Min(requested, DocumentImportService.MaximumInputBytes - _bytesRead + 1);

        private void CountBytes(int count)
        {
            _bytesRead += count;
            if (_bytesRead > DocumentImportService.MaximumInputBytes)
                throw new InvalidDataException(TooLargeMessage);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
