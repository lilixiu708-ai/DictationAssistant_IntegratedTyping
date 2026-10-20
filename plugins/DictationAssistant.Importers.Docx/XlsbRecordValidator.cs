// SPDX-License-Identifier: AGPL-3.0-or-later
// Modified 2026-10-20: validate BIFF12 record sizes before ExcelDataReader allocates record buffers.
using System.IO.Compression;
using System.Xml;

namespace DictationAssistant.Importers.Docx;

internal static class XlsbRecordValidator
{
    private const string TransitionalRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    /// <summary>
    /// Resolves binary parts through the same root/workbook relationships used by
    /// ExcelDataReader 3.9.0 ZipWorker, including nonstandard part names. Call only
    /// after the complete ZIP has passed the importer's byte/XML preflight.
    /// Returned names are the actual ZIP entry names, not URI-normalized guesses.
    /// </summary>
    public static IReadOnlySet<string> FindWorkbookParts(ZipArchive archive, CancellationToken token)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!entries.TryAdd(entry.FullName.Replace('\\', '/'), entry))
                throw new InvalidDataException("Excel 包内路径规范化后存在重名项目，无法可靠读取。");
        }

        string? workbook = null;
        if (entries.TryGetValue("_rels/.rels", out var rootRelationships))
        {
            foreach (var relation in ReadRelationships(rootRelationships, token))
            {
                if (!IsRelationship(relation.Type, "officeDocument")) continue;
                workbook = relation.Target;
                break;
            }
        }
        if (workbook is null || !entries.ContainsKey(workbook))
        {
            workbook = entries.ContainsKey("xl/workbook.xml") ? "xl/workbook.xml"
                : entries.ContainsKey("xl/workbook.bin") ? "xl/workbook.bin" : null;
        }

        var result = new HashSet<string>(StringComparer.Ordinal);
        if (workbook is null) return result;
        AddBinary(workbook);
        var segments = workbook.Split('/');
        var basePath = segments.Length <= 1 ? string.Empty : string.Join('/', segments, 0, segments.Length - 1) + "/";
        var relationshipPath = basePath + "_rels/" + segments[^1] + ".rels";
        if (!entries.TryGetValue(relationshipPath, out var workbookRelationships)) return result;

        string? styles = null;
        string? sharedStrings = null;
        var worksheets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relation in ReadRelationships(workbookRelationships, token))
        {
            if (relation.Id is null || relation.Target is null) continue;
            // Match the pinned reader's actual lookup behavior: one leading
            // slash is removed; relative targets are concatenated as written.
            var path = relation.Target.StartsWith('/') ? relation.Target[1..] : basePath + relation.Target;
            if (IsRelationship(relation.Type, "worksheet")) worksheets[relation.Id] = path;
            else if (IsRelationship(relation.Type, "styles")) styles = path;
            else if (IsRelationship(relation.Type, "sharedStrings")) sharedStrings = path;
        }
        AddBinary(styles);
        AddBinary(sharedStrings);
        foreach (var path in worksheets.Values)
            AddBinary(path.StartsWith("/xl/", StringComparison.OrdinalIgnoreCase) ? path[1..] : path);
        return result;

        void AddBinary(string? path)
        {
            if (path is not null && entries.TryGetValue(path, out var entry) &&
                entry.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                result.Add(entry.FullName);
        }
    }

    /// <summary>Checks record headers and drains payloads with a fixed-size buffer; never rents the declared record size.</summary>
    public static void Validate(Stream stream, long declaredPartLength, CancellationToken token)
    {
        if (declaredPartLength is < 0 or > ExcelPackage.MaximumPartBytes)
            throw new InvalidDataException("Excel 二进制部件超过 32 MiB 或长度无效。");
        var consumed = 0L;
        var buffer = new byte[8192];
        while (ReadVariable(stream, ref consumed, declaredPartLength, token, allowEnd: true, out _))
        {
            ReadVariable(stream, ref consumed, declaredPartLength, token, allowEnd: false, out var length);
            if (length > ExcelPackage.MaximumPartBytes || length > declaredPartLength - consumed)
                throw new InvalidDataException("Excel 二进制记录声明长度超过 32 MiB 或超出部件剩余内容。");
            var remaining = (long)length;
            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new InvalidDataException("Excel 二进制记录内容不完整。");
                remaining -= read;
                consumed += read;
            }
        }
        if (consumed != declaredPartLength)
            throw new InvalidDataException("Excel 二进制部件的实际长度与声明不一致。");
    }

    private static bool ReadVariable(Stream stream, ref long consumed, long declaredPartLength,
        CancellationToken token, bool allowEnd, out uint value)
    {
        value = 0;
        // The pinned BiffReader reads at most four seven-bit groups for both
        // record type and record length; reproduce that framing exactly.
        for (var index = 0; index < 4; index++)
        {
            token.ThrowIfCancellationRequested();
            var next = stream.ReadByte();
            if (next < 0)
            {
                if (index == 0 && allowEnd) return false;
                throw new InvalidDataException("Excel 二进制记录头不完整。");
            }
            if (++consumed > declaredPartLength)
                throw new InvalidDataException("Excel 二进制部件的实际长度超过声明。");
            value |= (uint)(next & 0x7f) << (7 * index);
            if ((next & 0x80) == 0) return true;
        }
        return true;
    }

    private static bool IsRelationship(string? type, string suffix) =>
        type == TransitionalRelationships + suffix || type == StrictRelationships + suffix;

    private static IEnumerable<Relationship> ReadRelationships(ZipArchiveEntry entry, CancellationToken token)
    {
        if (entry.Length > ExcelPackage.MaximumPartBytes)
            throw new InvalidDataException("Excel 关系部件超过 32 MiB。");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = ExcelPackage.MaximumPartBytes, MaxCharactersFromEntities = 1024,
            IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = false
        });
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > 128) throw new InvalidDataException("Excel 关系 XML 嵌套深度超过 128 层。");
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
                yield return new Relationship(reader.GetAttribute("Id"), reader.GetAttribute("Type"), reader.GetAttribute("Target"));
        }
    }

    private sealed record Relationship(string? Id, string? Type, string? Target);
}
