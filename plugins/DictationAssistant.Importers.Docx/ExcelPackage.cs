// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: isolate Excel package validation and guarded library reads.
using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace DictationAssistant.Importers.Docx;

/// <summary>Validates package resources before the workbook reader allocates or decodes them.</summary>
internal static class ExcelPackage
{
    internal const long MaximumPartBytes = 32L * 1024 * 1024;
    private const long MaxInflatedBytes = 128L * 1024 * 1024;
    private const int MaxArchiveEntries = 4096;
    private const int MaximumSharedStrings = 500_000;

    // Disposing this wrapper leaves the underlying package open for its owner.
    public static Stream OpenRead(Stream package, CancellationToken token) => new GuardedReadStream(package, token);

    public static void Validate(MemoryStream package, CancellationToken token)
    {
        if (package.Length < 4 || package.GetBuffer()[0] != 'P' || package.GetBuffer()[1] != 'K')
            throw new InvalidDataException("此 Excel 文件不是可读取的工作簿包，或设有打开密码。请先在 Excel 中解密并另存。");
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException("Excel 包内项目超过 4096 个。");
        var budget = new InflationBudget();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[8192];
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(entry.FullName)) throw new InvalidDataException("Excel 包包含重名项目，无法可靠读取。");
            if (entry.Length > MaximumPartBytes) throw new InvalidDataException("Excel 包内单个项目超过 32 MiB。");
            using var original = entry.Open();
            using var stream = new GuardedReadStream(original, token, budget);
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                using var xml = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = MaximumPartBytes, MaxCharactersFromEntities = 1024,
                    IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = false
                });
                while (xml.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (xml.Depth > 128) throw new InvalidDataException("Excel XML 嵌套深度超过 128 层。");
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sst" &&
                        xml.GetAttribute("uniqueCount") is { } count &&
                        (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var unique) || unique > MaximumSharedStrings))
                        throw new InvalidDataException("Excel 共享文本项目数量超过 500000 或无效。");
                }
                // Account for trailing bytes even if an XML reader finishes before the ZIP stream does.
            }
            while (stream.Read(buffer) > 0) { }
        }
        // BIFF12 rents a buffer using each record's declared size. Check the
        // actual relationship-selected binary parts before the library can do so.
        foreach (var path in XlsbRecordValidator.FindWorkbookParts(archive, token))
        {
            var entry = archive.GetEntry(path)!;
            using var original = entry.Open();
            using var stream = new GuardedReadStream(original, token);
            XlsbRecordValidator.Validate(stream, entry.Length, token);
        }
    }

    private sealed class InflationBudget { public long Bytes; }

    /// <summary>Checks cancellation inside the library's synchronous probe, metadata and cell reads.</summary>
    private sealed class GuardedReadStream(Stream inner, CancellationToken token, InflationBudget? budget = null) : Stream
    {
        private long _partBytes;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set { token.ThrowIfCancellationRequested(); inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            token.ThrowIfCancellationRequested();
            var count = inner.Read(buffer);
            if (budget is not null)
            {
                _partBytes += count;
                budget.Bytes += count;
                if (_partBytes > MaximumPartBytes || budget.Bytes > MaxInflatedBytes)
                    throw new InvalidDataException("Excel 实际解压超过单项 32 MiB 或总计 128 MiB 限制。");
            }
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) { token.ThrowIfCancellationRequested(); return inner.Seek(offset, origin); }
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // The caller owns the wrapped stream, matching the plugin's leave-open contract.
        protected override void Dispose(bool disposing) { }
    }
}
