using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GtaTransformationHub.Services;

/// <summary>
/// Reads an OpenIV OIV assembly.xml manifest and reports its install operations.
/// It never applies those operations, executes scripts, or opens/modifies RPF archives.
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
        var metadata = ExactlyOneChild(root, "metadata", "metadata");
        var colors = ExactlyOneChild(root, "colors", "colors");
        var content = ExactlyOneChild(root, "content", "content");

        var name = RequiredValue(metadata, "name", "OIV package name");
        var authorNode = ExactlyOneChild(metadata, "author", "metadata/author");
        var author = RequiredValue(authorNode, "displayName", "metadata/author/displayName");
        _ = RequiredValue(metadata, "description", "metadata/description");
        var versionNode = ExactlyOneChild(metadata, "version", "metadata/version");
        var major = RequiredInteger(versionNode, "major", "metadata/version/major");
        var minor = RequiredInteger(versionNode, "minor", "metadata/version/minor");
        var modVersion = major.ToString(CultureInfo.InvariantCulture) + "." +
                         minor.ToString(CultureInfo.InvariantCulture);
        var tag = versionNode.Elements().FirstOrDefault(element => NameIs(element, "tag"))?.Value.Trim();
        if (!string.IsNullOrWhiteSpace(tag))
            modVersion += " " + tag;

        // The base OpenIV 2.x specification defines target="Five" but not a
        // Legacy/Enhanced marker. CodeWalker's OIV installer extension reads an
        // optional <gameversion>Legacy|Enhanced|Any</gameversion> under <metadata>.
        // Never infer game edition from the package <version>/<tag>.
        var gameVersionNodes = metadata.Elements()
            .Where(element => NameIs(element, "gameversion"))
            .ToArray();
        if (gameVersionNodes.Length > 1)
            throw new InvalidDataException("The OIV metadata contains multiple gameversion markers.");
        var gameVersionNode = gameVersionNodes.SingleOrDefault();
        var gameVersionRequirement = gameVersionNode is null
            ? "Unspecified"
            : ClassifyGameVersionRequirement(gameVersionNode.Value.Trim());

        ValidateColors(colors);
        var licence = metadata.Elements().FirstOrDefault(element => NameIs(element, "licence"))?.Value.Trim() ?? string.Empty;
        var operations = new List<OivOperationSummary>();
        var unsupported = new List<string>();
        var sourceIssues = new List<string>();
        if (gameVersionNode is not null && gameVersionRequirement == "Unspecified")
            unsupported.Add("unrecognised gameversion extension value; verify game edition independently");

        var contentEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (contentEntries.Length == 0)
            throw new InvalidDataException("The OIV package is missing the required content folder.");

        var payloadPaths = contentEntries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToArray();

        InspectCommands(content, string.Empty, payloadPaths, formatVersion, operations, unsupported, sourceIssues);

        return new OivManifestInspectionResult(
            formatVersion,
            packageId,
            target,
            name,
            author,
            modVersion,
            tag ?? string.Empty,
            gameVersionRequirement,
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
        string formatVersion,
        List<OivOperationSummary> operations,
        List<string> unsupported,
        List<string> sourceIssues)
    {
        foreach (var element in parent.Elements())
        {
            var command = element.Name.LocalName.ToLowerInvariant();
            switch (command)
            {
                case "archive":
                {
                    var archivePath = NormalizeTargetPath(RequiredAttribute(element, "path"), "archive path");
                    var createIfNotExist = RequiredAttribute(element, "createIfNotExist");
                    if (!bool.TryParse(createIfNotExist, out _))
                        throw new InvalidDataException("An OIV archive command has an invalid createIfNotExist value.");

                    var archiveType = RequiredAttribute(element, "type");
                    if (!string.Equals(archiveType, "RPF7", StringComparison.OrdinalIgnoreCase))
                        unsupported.Add("archive type: " + archiveType);

                    var fullArchivePath = CombineTargetPaths(containingArchive, archivePath);
                    AddOperation(operations, new OivOperationSummary("archive", containingArchive, fullArchivePath, null));
                    InspectCommands(element, fullArchivePath, payloadPaths, formatVersion, operations, unsupported, sourceIssues);
                    break;
                }
                case "add":
                case "replace":
                {
                    var source = NormalizeSourcePath(RequiredAttribute(element, "source"));
                    var target = NormalizeTargetPath(element.Value, command + " destination");
                    if (!PayloadSourceExists(payloadPaths, source))
                        sourceIssues.Add(command + " source is not present in the OIV content folder: " + source);
                    AddOperation(operations, new OivOperationSummary(command, containingArchive, target, source));
                    break;
                }
                case "delete":
                {
                    var target = NormalizeTargetPath(element.Value, "delete destination");
                    AddOperation(operations, new OivOperationSummary(command, containingArchive, target, null));
                    break;
                }
                case "xml":
                case "pso":
                {
                    var target = NormalizeTargetPath(RequiredAttribute(element, "path"), command + " patch target");
                    AddOperation(operations, new OivOperationSummary(command + "Patch", containingArchive, target, null));
                    InspectNodePatchCommands(element, command, containingArchive, target, formatVersion, operations, unsupported);
                    break;
                }
                case "text":
                {
                    var target = NormalizeTargetPath(RequiredAttribute(element, "path"), "text patch target");
                    RequiredBooleanAttribute(element, "createIfNotExist");
                    AddOperation(operations, new OivOperationSummary("textPatch", containingArchive, target, null));
                    InspectTextPatchCommands(element, containingArchive, target, operations, unsupported);
                    break;
                }
                case "defragmentation":
                {
                    var archivePath = NormalizeTargetPath(RequiredAttribute(element, "archive"), "defragmentation archive");
                    var target = CombineTargetPaths(containingArchive, archivePath);
                    AddOperation(operations, new OivOperationSummary("defragmentation", containingArchive, target, null));
                    break;
                }
                default:
                    unsupported.Add(element.Name.LocalName);
                    break;
            }
        }
    }

    private static void InspectNodePatchCommands(
        XElement patch,
        string patchKind,
        string containingArchive,
        string target,
        string formatVersion,
        List<OivOperationSummary> operations,
        List<string> unsupported)
    {
        foreach (var element in patch.Elements())
        {
            var command = element.Name.LocalName.ToLowerInvariant();
            if (command is not ("add" or "replace" or "remove"))
            {
                unsupported.Add(patchKind + " command: " + element.Name.LocalName);
                continue;
            }

            _ = RequiredAttribute(element, "xpath");
            if (command == "add")
                ValidateXmlAppendAttribute(element, formatVersion, unsupported);

            AddOperation(operations, new OivOperationSummary(
                patchKind + ":" + command, containingArchive, target, null));
        }
    }

    private static void InspectTextPatchCommands(
        XElement patch,
        string containingArchive,
        string target,
        List<OivOperationSummary> operations,
        List<string> unsupported)
    {
        foreach (var element in patch.Elements())
        {
            var command = element.Name.LocalName.ToLowerInvariant();
            switch (command)
            {
                case "add":
                    AddOperation(operations, new OivOperationSummary("text:add", containingArchive, target, null));
                    break;
                case "insert":
                    ValidateTextSearchAttributes(element, requireWhere: true);
                    AddOperation(operations, new OivOperationSummary("text:insert", containingArchive, target, null));
                    break;
                case "replace":
                    ValidateTextSearchAttributes(element, requireWhere: false);
                    AddOperation(operations, new OivOperationSummary("text:replace", containingArchive, target, null));
                    break;
                case "delete":
                    if (string.IsNullOrWhiteSpace(element.Value))
                        throw new InvalidDataException("An OIV text delete command must contain the line or mask to delete.");
                    ValidateTextCondition(RequiredAttribute(element, "condition"));
                    AddOperation(operations, new OivOperationSummary("text:delete", containingArchive, target, null));
                    break;
                default:
                    unsupported.Add("text command: " + element.Name.LocalName);
                    break;
            }
        }
    }

    private static void ValidateTextSearchAttributes(XElement element, bool requireWhere)
    {
        _ = RequiredAttribute(element, "line");
        ValidateTextCondition(RequiredAttribute(element, "condition"));
        if (requireWhere)
        {
            var where = RequiredAttribute(element, "where");
            if (where is not ("Before" or "After"))
                throw new InvalidDataException("An OIV text insert command has an invalid where value.");
        }
    }

    private static void ValidateTextCondition(string condition)
    {
        if (condition is not ("Equal" or "StartWith" or "Mask"))
            throw new InvalidDataException("An OIV text command has an invalid condition value.");
    }

    private static void ValidateXmlAppendAttribute(
        XElement element,
        string formatVersion,
        List<string> unsupported)
    {
        var append = (string?)element.Attribute("append");
        if (string.IsNullOrWhiteSpace(append))
        {
            // The specification marks append as required, but its own 2.2 example contains
            // an add command without it. Preserve the inventory and require human review.
            unsupported.Add("XML add command omits the specification-required append attribute");
            return;
        }

        var allowed = formatVersion == "2.2"
            ? new[] { "First", "Last", "Before", "After" }
            : new[] { "First", "Last" };
        if (!allowed.Contains(append, StringComparer.Ordinal))
            throw new InvalidDataException("An OIV XML add command has an invalid append value.");
    }

    private static void ValidateColors(XElement colors)
    {
        var header = ExactlyOneChild(colors, "headerBackground", "colors/headerBackground");
        var icon = ExactlyOneChild(colors, "iconBackground", "colors/iconBackground");
        var headerValue = header.Value.Trim();
        var iconValue = icon.Value.Trim();
        if (headerValue.Length != 9 || headerValue[0] != '$' || !uint.TryParse(
                headerValue.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("OIV colors/headerBackground must be an ARGB value such as $FF23366A.");

        if (iconValue.Length != 9 || iconValue[0] != '$' || !uint.TryParse(
                iconValue.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("OIV colors/iconBackground must be an ARGB value such as $FF3B5998.");

        if (!bool.TryParse(RequiredAttribute(header, "useBlackTextColor"), out _))
            throw new InvalidDataException("OIV colors/headerBackground requires a Boolean useBlackTextColor attribute.");
    }

    private static bool PayloadSourceExists(IReadOnlyList<string> payloadPaths, string source)
    {
        foreach (var payloadPath in payloadPaths)
        {
            var normalized = payloadPath.Replace('\\', '/');
            var contentIndex = normalized.IndexOf("content/", StringComparison.OrdinalIgnoreCase);
            if (contentIndex < 0)
                continue;

            var contentRelative = normalized[(contentIndex + "content/".Length)..];
            if (string.Equals(contentRelative, source, StringComparison.OrdinalIgnoreCase) ||
                contentRelative.EndsWith("/" + source, StringComparison.OrdinalIgnoreCase))
                return true;
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

    private static string ClassifyGameVersionRequirement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unspecified";

        var normalized = value.Trim();
        if (normalized.Contains("enhanced", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("gen9", StringComparison.OrdinalIgnoreCase))
            return "Enhanced";
        if (normalized.Contains("legacy", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("gen8", StringComparison.OrdinalIgnoreCase))
            return "Legacy";
        if (normalized.Equals("any", StringComparison.OrdinalIgnoreCase))
            return "Any";
        return "Unspecified";
    }

    private static string CombineTargetPaths(string parent, string child) =>
        string.IsNullOrEmpty(parent) ? child : parent + "/" + child;

    private static XElement ExactlyOneChild(XElement parent, string childName, string label)
    {
        var children = parent.Elements().Where(element => NameIs(element, childName)).ToArray();
        if (children.Length != 1)
            throw new InvalidDataException("The OIV manifest must contain exactly one " + label + " element.");
        return children[0];
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        var value = (string?)element.Attribute(name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("The OIV manifest is missing a required '" + name + "' attribute.");
        return value.Trim();
    }

    private static void RequiredBooleanAttribute(XElement element, string name)
    {
        var value = RequiredAttribute(element, name);
        if (!bool.TryParse(value, out _))
            throw new InvalidDataException("The OIV manifest has an invalid Boolean '" + name + "' attribute.");
    }

    private static string RequiredValue(XElement parent, string childName, string label)
    {
        var value = parent.Elements().FirstOrDefault(element => NameIs(element, childName))?.Value.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException("The OIV manifest is missing " + label + ".")
            : value;
    }

    private static int RequiredInteger(XElement parent, string childName, string label)
    {
        var value = RequiredValue(parent, childName, label);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            throw new InvalidDataException("The OIV manifest contains an invalid integer at " + label + ".");
        return parsed;
    }

    private static void AddOperation(List<OivOperationSummary> operations, OivOperationSummary operation)
    {
        if (operations.Count >= MaximumOperations)
            throw new InvalidDataException("The OIV manifest exceeds the 5,000-operation inspection limit.");
        operations.Add(operation);
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
    string VersionTag,
    string GameVersionRequirement,
    string Licence,
    bool IsGtaVTarget,
    IReadOnlyList<OivOperationSummary> Operations,
    IReadOnlyList<string> UnsupportedCommands,
    IReadOnlyList<string> PayloadSourceIssues)
{
    public bool IsExplicitlyLegacy =>
        string.Equals(GameVersionRequirement, "Legacy", StringComparison.OrdinalIgnoreCase);

    public bool IsExplicitlyEnhanced =>
        string.Equals(GameVersionRequirement, "Enhanced", StringComparison.OrdinalIgnoreCase);

    public bool IsAnyGameVersion =>
        string.Equals(GameVersionRequirement, "Any", StringComparison.OrdinalIgnoreCase);

    public bool RequiresManualReview =>
        !IsGtaVTarget ||
        GameVersionRequirement is not ("Legacy" or "Any") ||
        UnsupportedCommands.Count > 0 ||
        PayloadSourceIssues.Count > 0;

    public int FileAddCount => Operations.Count(operation =>
        operation.Command is "add" or "replace");

    public int DeleteCount => Operations.Count(operation =>
        operation.Command == "delete");

    public int ArchiveEditCount => Operations.Count(operation =>
        operation.Command == "archive");

    public int PatchOperationCount => Operations.Count(operation =>
        operation.Command.Contains(":", StringComparison.Ordinal));
}
