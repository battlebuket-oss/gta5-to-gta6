using System.IO.Compression;
using System.Text;
using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class OivManifestInspectionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "GtaHubOivTests", Guid.NewGuid().ToString("N"));

    public OivManifestInspectionServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Inspect_ReportsNestedArchiveCommandsAndResolvesPayloads()
    {
        var archivePath = CreateOiv(
            "valid.oiv",
            Manifest("""
                <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
                  <metadata>
                    <name>Example HUD</name>
                    <version><major>2</major><minor>8</minor><tag>Legacy</tag></version>
                    <author><displayName>Example Author</displayName></author>
                    <description><![CDATA[Example package description]]></description>
                    <licence>Personal use</licence>
                  </metadata>
                  <colors><headerBackground useBlackTextColor="False">$FF000000</headerBackground><iconBackground>$FFFFFFFF</iconBackground></colors>
                  <content>
                    <add source="scripts/example.asi">scripts/example.asi</add>
                    <archive path="update/update.rpf" type="RPF7" createIfNotExist="False">
                      <archive path="x64/dlcpacks/example/dlc.rpf" type="RPF7" createIfNotExist="False">
                        <add source="example/dlc.rpf">dlc.rpf</add>
                      </archive>
                      <xml path="common/data/dlclist.xml">
                        <add xpath="/Paths" append="Last"><Item>dlcpacks:/example/</Item></add>
                      </xml>
                    </archive>
                    <delete>scripts/obsolete.ini</delete>
                  </content>
                </package>
                """),
            ("content/9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43/scripts/example.asi", "payload"),
            ("content/9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43/example/dlc.rpf", "rpf"));

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var result = OivManifestInspectionService.Inspect(archive);

        Assert.Equal("2.2", result.FormatVersion);
        Assert.Equal("Example HUD", result.Name);
        Assert.Equal("Example Author", result.Author);
        Assert.Equal("2.8 Legacy", result.ModVersion);
        Assert.True(result.IsGtaVTarget);
        Assert.False(result.RequiresManualReview);
        Assert.Equal(2, result.FileAddCount);
        Assert.Equal(1, result.DeleteCount);
        Assert.Equal(2, result.ArchiveEditCount);
        Assert.Contains(result.Operations, item =>
            item.Command == "xmlPatch" && item.ArchivePath == "update/update.rpf" &&
            item.TargetPath == "common/data/dlclist.xml");
        Assert.Empty(result.PayloadSourceIssues);
    }

    [Fact]
    public void Inspect_RejectsDocumentTypeDeclarations()
    {
        var archivePath = CreateOiv(
            "doctype.oiv",
            """
            <!DOCTYPE package [<!ENTITY x SYSTEM "file:///c:/windows/win.ini">]>
            <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
              <metadata><name>&x;</name><version><major>1</major><minor>0</minor></version>
                <author><displayName>Author</displayName></author></metadata>
              <colors/><content/>
            </package>
            """);

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Throws<System.Xml.XmlException>(() => OivManifestInspectionService.Inspect(archive));
    }

    [Fact]
    public void Inspect_RejectsTraversalInDestinationPath()
    {
        var archivePath = CreateOiv(
            "traversal.oiv",
            Manifest("""
                <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
                  <metadata><name>Unsafe</name><version><major>1</major><minor>0</minor></version>
                    <author><displayName>Author</displayName></author></metadata>
                  <description><![CDATA[Traversal test]]></description>
                  <colors><headerBackground useBlackTextColor="False">$FF000000</headerBackground><iconBackground>$FFFFFFFF</iconBackground></colors>
                  <content><delete>../../outside.txt</delete></content>
                </package>
                """));

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => OivManifestInspectionService.Inspect(archive));
    }

    [Fact]
    public void Inspect_EnumeratesXmlPsoAndTextPatchOperations()
    {
        var archivePath = CreateOiv(
            "patches.oiv",
            Manifest("""
                <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
                  <metadata>
                    <name>Patch inventory</name>
                    <version><major>1</major><minor>0</minor></version>
                    <author><displayName>Author</displayName></author>
                    <description><![CDATA[Patch inventory test]]></description>
                  </metadata>
                  <colors>
                    <headerBackground useBlackTextColor="False">$FF000000</headerBackground>
                    <iconBackground>$FFFFFFFF</iconBackground>
                  </colors>
                  <content>
                    <xml path="update/update.rpf/common/data/dlclist.xml">
                      <add xpath="/Paths" append="Before"><Item>dlcpacks:/sample/</Item></add>
                      <remove xpath="/Paths/Item[2]" />
                    </xml>
                    <text path="data/info.dat" createIfNotExist="False">
                      <add>line one</add>
                      <insert where="After" line="line one" condition="Equal">line two</insert>
                      <replace line="line three" condition="StartWith">replacement</replace>
                      <delete line="#*" condition="Mask" />
                    </text>
                    <pso path="x64/data/carcols.ymt">
                      <replace xpath="/CHandlingDataMgr/Item"><Item /></replace>
                    </pso>
                  </content>
                </package>
                """));

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var result = OivManifestInspectionService.Inspect(archive);

        Assert.False(result.RequiresManualReview);
        Assert.Equal(7, result.PatchOperationCount);
        Assert.Contains(result.Operations, item => item.Command == "xml:add");
        Assert.Contains(result.Operations, item => item.Command == "xml:remove");
        Assert.Contains(result.Operations, item => item.Command == "text:insert");
        Assert.Contains(result.Operations, item => item.Command == "text:delete");
        Assert.Contains(result.Operations, item => item.Command == "pso:replace");
    }

    [Fact]
    public void Inspect_RejectsUnsupportedOiv22XmlAppendMode()
    {
        var archivePath = CreateOiv(
            "bad-append.oiv",
            Manifest("""
                <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
                  <metadata>
                    <name>Invalid append</name>
                    <version><major>1</major><minor>0</minor></version>
                    <author><displayName>Author</displayName></author>
                    <description><![CDATA[Invalid append test]]></description>
                  </metadata>
                  <colors>
                    <headerBackground useBlackTextColor="False">$FF000000</headerBackground>
                    <iconBackground>$FFFFFFFF</iconBackground>
                  </colors>
                  <content>
                    <xml path="dlclist.xml"><add xpath="/Paths" append="Middle"><Item /></add></xml>
                  </content>
                </package>
                """));

        using var file = File.OpenRead(archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => OivManifestInspectionService.Inspect(archive));
    }

    [Fact]
    public void Inspect_RejectsMissingAssemblyManifest()
    {
        var archivePath = Path.Combine(_root, "missing.oiv");
        using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("payload.bin").Open());
            writer.Write("data");
        }

        using var input = File.OpenRead(archivePath);
        using var readArchive = new ZipArchive(input, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => OivManifestInspectionService.Inspect(readArchive));
    }

    [Fact]
    public async Task ImportAsync_PersistsOivInspectionWithoutApprovingPackage()
    {
        var archivePath = CreateOiv(
            "import-test.oiv",
            Manifest("""
                <package version="2.2" id="{9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43}" target="Five">
                  <metadata><name>Import Test</name><version><major>1</major><minor>0</minor></version>
                    <author><displayName>Author</displayName></author><licence>Personal use</licence></metadata>
                  <description><![CDATA[Import test description]]></description>
                  <colors><headerBackground useBlackTextColor="False">$FF000000</headerBackground><iconBackground>$FFFFFFFF</iconBackground></colors>
                  <content><add source="example.ini">example.ini</add></content>
                </package>
                """),
            ("content/9a4c2e61-7b3f-4d08-8e15-2c6b0f9a7d43/example.ini", "setting=true"));

        var importRoot = Path.Combine(_root, "imports");
        Directory.CreateDirectory(importRoot);
        var result = await new ModArchiveImportService().ImportAsync(
            archivePath,
            importRoot,
            new ModArchiveMetadata(
                "oiv-test",
                "OIV test",
                "Author",
                new Uri("https://example.com/oiv"),
                "Personal use",
                "1.0"));

        var manifest = await File.ReadAllTextAsync(result.ManifestPath);
        Assert.Contains("\"Status\": \"Researching\"", manifest);
        Assert.Contains("\"OivInspection\"", manifest);
        Assert.Contains("\"FormatVersion\": \"2.2\"", manifest);
        Assert.Contains("\"FileAddCount\": 1", manifest);
    }

    private string CreateOiv(string name, string manifest, params (string Path, string Content)[] payloads)
    {
        var path = Path.Combine(_root, name);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        WriteEntry(archive, "assembly.xml", manifest);
        foreach (var (entryPath, content) in payloads)
            WriteEntry(archive, entryPath, content);
        return path;
    }

    private static string Manifest(string inner) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + inner;

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        using var stream = archive.CreateEntry(path).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
