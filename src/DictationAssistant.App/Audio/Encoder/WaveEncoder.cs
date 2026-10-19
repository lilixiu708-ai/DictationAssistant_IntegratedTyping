namespace DictationAssistant.App.Audio.Encoder;

public class WaveEncoder : IAudioEncoder
{
    private class WaveEncodeStream : Stream
    {
        private readonly Stream _baseStream;
        private PcmFormatInfo _format;
        private int _headerSize;

        public WaveEncodeStream(Stream baseStream, PcmFormatInfo format)
        {
            _baseStream = baseStream;
            _format = format;
            if (!_baseStream.CanWrite)
                throw new ArgumentException("Base stream must be writable", nameof(baseStream));
            if (!_baseStream.CanSeek)
                throw new ArgumentException("Base stream must be seekable", nameof(baseStream));
            WriteWaveHeader(_baseStream, format, 0);
            _headerSize = (int)_baseStream.Position;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
            var originalPosition = _baseStream.Position;
            var dataLength = _baseStream.Length - _headerSize;
            _baseStream.Position = 0;
            WriteWaveHeader(_baseStream, _format, dataLength);
            _baseStream.Position = originalPosition;
            _baseStream.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            var originalPosition = _baseStream.Position;
            var dataLength = _baseStream.Length - _headerSize;
            _baseStream.Position = 0;
            WriteWaveHeader(_baseStream, _format, dataLength);
            _baseStream.Position = originalPosition;
            return _baseStream.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _baseStream.Write(buffer, offset, count);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _baseStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Flush();
                _baseStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public PcmAudio RawAudio { get; }

    public WaveEncoder(PcmFormatInfo format, string path)
    {
        RawAudio = new PcmAudio()
        {
            Data = new WaveEncodeStream(File.Create(path), format),
            Format = format
        };
    }

    public async Task FinalizeAsync()
    {
        await RawAudio.Data.DisposeAsync();
    }

    public static void WriteWaveHeader(Stream output, PcmFormatInfo format, long dataLength)
    {
        if (format.SampleFormat != PcmSampleFormat.U8 && format.SampleFormat != PcmSampleFormat.S16LE)
            throw new NotSupportedException("Only U8 and S16LE formats are supported");

        var bytesPerSample = format.SampleFormat == PcmSampleFormat.U8 ? 1 : 2;
        var blockAlign = (ushort)(format.Channels * bytesPerSample);
        var byteRate = format.SampleRate * blockAlign;
        var bitsPerSample = (ushort)(bytesPerSample * 8);

        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write("RIFF"u8.ToArray());
        writer.Write((uint)(36 + dataLength));
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)format.Channels);
        writer.Write(format.SampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write((uint)dataLength);
    }
}