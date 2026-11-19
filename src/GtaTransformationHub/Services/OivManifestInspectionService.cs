using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GtaTransformationHub.Services;

/// <summary>
/// Reads an OpenIV OIV assembly.xml manifest and reports the install operations.
/// It never applies those operations or opens/modifies RPF archives.
/// </summary>
public static class OivManifestInspectionService
{
    private const long MaximumManifestBytes = 4 * 1024 * 1024;
    private const int MaximumOperations = 5000;

    public static OivManifestInspectionResult Inspect(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var manifests = archive.Entries
            .Where(entry => string.Equals(entry.FullName, "assembly.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (manifests.Count != 1)
            throw new InvalidDataException("An OIV package must contain exactly one root-level assembly.xml manifest.");

        var manifestEntry = manifests[0];
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
            throw new InvalidDataException("The OIV assembly.xml manifest is empty or exceeds the 4 MiB inspection limit.");

        XDocument document;
        using (var stream = manifestEntry.Open())
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumManifestBytes,
            MaxCharactersFromEntities = 1024,
            IgnoreComments = false,
            IgnoreProcessingInstructions = true
        }))
        {
            document = XDocument.Load(reader, LoadOptions.None);
        }

        var root = document.Root;
        if (root is null || !NameIs(root, "package"))
            throw new InvalidDataException("The OIV manifest root element must be <package>.");

        var formatVersion = RequiredAttribute(root, "version");
        if (formatVersion is not ("2.0" or "2.1" or "2.2"))
            throw new InvalidDataException("Unsupported OIV package format version: " + formatVersion);

        var packageId = RequiredAttribute(root, "id");
        if (!Guid.TryParse(packageId.Trim('{', '}'), out _))
            throw new InvalidDataException("The OIV package ID must be a GUID.");

        var target = RequiredAttribute(root, "target");
        var metadata = root.Elements().SingleOrDefault(element => NameIs(element, "metadata"));
        var content = root.Elements().SingleOrDefault(element => NameIs(element, "content"));
        if (metadata is null || content is null)
            throw new InvalidDataException("The OIV manifest must contain one <metadata> and one <content> element.");

        var name = RequiredValue(metadata, "name", "OIV package name");
        var authorNode = metadata.Elements().FirstOrDefault(element => NameIs(element, "author"));
        var author = authorNode?.Elements().FirstOrDefault(element => NameIs(element, "displayName"))?.Value.Trim();
        if (string.IsNullOrWhiteSpace(author))
            throw new InvalidDataException("The OIV manifest is missing metadata/author/displayName.");

        var versionNode = metadata.Elements().FirstOrDefault(element => NameIs(element, "version"));
        var major = versionNode?.Elements().FirstOrDefault(element => NameIs(element, "major"))?.Value.Trim();
        var minor = versionNode?.Elements().FirstOrDefault(element => NameIs(element, "minor"))?.Value.Trim();
        if (string.IsNullOrWhiteSpace(major) || string.IsNullOrWhiteSpace(minor))
            throw new InvalidDataException("The OIV manifest is missing its mod version major/minor fields.");
        var modVersion = major + "." + minor;
        var tag = versionNode?.Elements().FirstOrDefault(element => NameIs(element, "tag"))?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(tag))
            modVersion += " " + tag;

        var licence = metadata.Elements().FirstOrDefault(element => NameIs(element, "licence"))?.Value.Trim() ?? string.Empty;
        var operations = new List<OivOperationSummary>();
        var unsupported = new List<string>();
        var sourceIssues = new List<string>();
        var payloadPaths = archive.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(path => path.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        InspectCommands(content, string.Empty, payloadPaths, operations, unsupported, sourceIssues);

        return new OivManifestInspectionResult(
            formatVersion,
            packageId,
            target,
            name,
            author,
            modVersion,
            licence,
            string.Equals(target, "Five", StringComparison.OrdinalIgnoreCase),
            operations,
            unsupported.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceIssues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void InspectCommands(
        XElement parent,
        string containingArchive,
        IReadOnlyList<string> payloadPaths,
        List<OivOperationSummary> operations,
        List<string> unsupported,
        List<string> sourceIssues)
    {
        foreach (var element in parent.Elements())
        {
            if (operations.Count >= MaximumOperations)
                throw new InvalidDataException("The OIV manifest exceeds the 5,000-operation inspection limit.");

            var command = element.Name.LocalName.ToLowerInvariant();
            switch (command)
            {
                case "archive":
                {
                    var archivePath = NormalizeTargetPath(RequiredAttribute(element, "path"), "archive path");
                    var fullArchivePath = CombineTargetPaths(containingArchive, archivePath);
                    operations.Add(new OivOperationSummary("archive", containingArchive, fullArchivePath, null));
                    InspectCommands(element, fullArchivePath, payloadPaths, operations, unsupported, sourceIssues);
                    break;
                }
                case "add":
                case "replace":
                {
                    var source = NormalizeSourcePath(RequiredAttribute(element, "source"));
                    var target = NormalizeTargetPath(element.Value, command + " destination");
                    if (!PayloadSourceExists(payloadPaths, source))
                        sourceIssues.Add(command + " source is not present in the OIV content folder: " + source);
                    operations.Add(new OivOperationSummary(command, containingArchive, target, source));
                    break;
                }
                case "delete":
                {
                    var target = NormalizeTargetPath(element.Value, "delete destination");
                    operations.Add(new OivOperationSummary(command, containingArchive, target, null));
                    break;
                }
                case "xml":
                case "text":
                {
                    var target = NormalizeTargetPath(RequiredAttribute(element, "path"), command + " patch target");
                    operations.Add(new OivOperationSummary(command + "Patch", containingArchive, target, null));
                    break;
                }
                case "defragmentation":
                {
                    var target = NormalizeTargetPath(RequiredAttribute(element, "archive"), "defragmentation archive");
                    operations.Add(new OivOperationSummary(command, containingArchive, target, null));
                    break;
                }
                default:
                    unsupported.Add(element.Name.LocalName);
                    break;
            }
        }
    }

    private static bool PayloadSourceExists(IReadOnlyList<string> payloadPaths, string source)
    {
        var sourceSegments = source.Split('/');
        foreach (var payloadPath in payloadPaths)
        {
            var segments = payloadPath.Split('/');
            for (var offset = 1; offset + sourceSegments.Length <= segments.Length; offset++)
            {
                var candidate = string.Join("/", segments.Skip(offset).Take(sourceSegments.Length));
                if (string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase) &&
                    offset + sourceSegments.Length == segments.Length)
                    return true;
            }
        }

        return false;
    }

    private static string NormalizeSourcePath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        return NormalizeRelativePath(normalized, "OIV payload source");
    }

    private static string NormalizeTargetPath(string value, string label)
    {
        var normalized = value.Trim().Replace('\\', '/');
        return NormalizeRelativePath(normalized, label);
    }

    private static string NormalizeRelativePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 512 ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(value) ||
            value.IndexOf((char)0) >= 0)
            throw new InvalidDataException("The OIV manifest contains an empty, rooted, or overlong " + label + ".");

        var segments = value.Split('/');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.EndsWith(" ", StringComparison.Ordinal) ||
                segment.EndsWith(".", StringComparison.Ordinal) ||
                segment.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0 ||
                segment.Any(char.IsControl) ||
                Path.GetInvalidFileNameChars().Any(character => segment.Contains(character)))
                throw new InvalidDataException("The OIV manifest contains an unsafe " + label + ": " + value);
        }

        return string.Join("/", segments);
    }

    private static string CombineTargetPaths(string parent, string child) =>
        string.IsNullOrEmpty(parent) ? child : parent + "/" + child;

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The OIV manifest is missing a required '" + name + "' attribute.");
        return value.Trim();
    }

    private static string RequiredValue(XElement parent, string childName, string label)
    {
        var value = parent.Elements().FirstOrDefault(element => NameIs(element, childName))?.Value.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException("The OIV manifest is missing " + label + ".")
            : value;
    }

    private static bool NameIs(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase);
}

public sealed record OivOperationSummary(
    string Command,
    string ArchivePath,
    string TargetPath,
    string? SourcePath);

public sealed record OivManifestInspectionResult(
    string FormatVersion,
    string PackageId,
    string Target,
    string Name,
    string Author,
    string ModVersion,
    string Licence,
    bool IsGtaVTarget,
    IReadOnlyList<OivOperationSummary> Operations,
    IReadOnlyList<string> UnsupportedCommands,
    IReadOnlyList<string> PayloadSourceIssues)
{
    public bool RequiresManualReview =>
        !IsGtaVTarget || UnsupportedCommands.Count > 0 || PayloadSourceIssues.Count > 0;

    public int FileAddCount => Operations.Count(operation =>
        operation.Command is "add" or "replace");

    public int DeleteCount => Operations.Count(operation =>
        operation.Command == "delete");

    public int ArchiveEditCount => Operations.Count(operation =>
        operation.Command == "archive");
}
