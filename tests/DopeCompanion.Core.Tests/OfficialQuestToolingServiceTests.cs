using System.Security.Cryptography;
using System.Text;
using DopeCompanion.Core.Services;

namespace DopeCompanion.Core.Tests;

public sealed class OfficialQuestToolingServiceTests
{
    [Fact]
    public void IntegrityMatchesSha512_accepts_matching_payload()
    {
        var payload = Encoding.UTF8.GetBytes("hzdb payload");
        var integrity = "sha512-" + Convert.ToBase64String(SHA512.HashData(payload));

        Assert.True(OfficialQuestToolingService.IntegrityMatchesSha512(payload, integrity));
    }

    [Fact]
    public void ChecksumMatchesSha1_accepts_matching_payload()
    {
        var payload = Encoding.UTF8.GetBytes("platform-tools payload");
        var checksum = Convert.ToHexString(SHA1.HashData(payload)).ToLowerInvariant();

        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha1(payload, checksum));
    }

    [Fact]
    public void ChecksumMatchesSha256_accepts_matching_payload()
    {
        var payload = Encoding.UTF8.GetBytes("scrcpy payload");
        var checksum = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha256(payload, checksum));
        Assert.True(OfficialQuestToolingService.ChecksumMatchesSha256(payload, $"sha256:{checksum}"));
    }

    [Fact]
    public void ParsePlatformToolsRevision_reads_source_properties_revision()
    {
        var revision = OfficialQuestToolingService.ParsePlatformToolsRevision("""
            Pkg.Desc=Android SDK Platform-Tools
            Pkg.Revision=37.0.0
            """);

        Assert.Equal("37.0.0", revision);
    }

    [Fact]
    public void ParseHzdbReleaseMetadataJson_reads_live_registry_shape()
    {
        var metadata = OfficialQuestToolingService.ParseHzdbReleaseMetadataJson("""
            {
              "name": "@meta-quest/hzdb-win32-x64",
              "version": "1.0.1",
              "license": "SEE LICENSE AT https://developers.meta.com/horizon/licenses/",
              "dist": {
                "tarball": "https://registry.npmjs.org/@meta-quest/hzdb-win32-x64/-/hzdb-win32-x64-1.0.1.tgz",
                "integrity": "sha512-example"
              }
            }
            """);

        Assert.Equal("1.0.1", metadata.Version);
        Assert.Equal("https://registry.npmjs.org/@meta-quest/hzdb-win32-x64/-/hzdb-win32-x64-1.0.1.tgz", metadata.TarballUri);
        Assert.Equal("sha512-example", metadata.Integrity);
        Assert.Equal("SEE LICENSE AT https://developers.meta.com/horizon/licenses/", metadata.License);
    }

    [Fact]
    public void ParseScrcpyReleaseMetadataJson_reads_live_github_release_shape()
    {
        var metadata = OfficialQuestToolingService.ParseScrcpyReleaseMetadataJson("""
            {
              "tag_name": "v3.3.4",
              "html_url": "https://github.com/Genymobile/scrcpy/releases/tag/v3.3.4",
              "assets": [
                {
                  "name": "scrcpy-win64-v3.3.4.zip",
                  "browser_download_url": "https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/scrcpy-win64-v3.3.4.zip",
                  "digest": "sha256:d8a155b7c180b7ca4cdadd40712b8750b63f3aab48cb5b8a2a39ac2d0d4c5d38"
                },
                {
                  "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/SHA256SUMS.txt",
                  "digest": "sha256:4cb7069421050db158d2519ce766f5dd1ee26728a022a61a6724733bde86761a"
                }
              ]
            }
            """);

        Assert.Equal("3.3.4", metadata.Version);
        Assert.Equal("scrcpy-win64-v3.3.4.zip", metadata.AssetName);
        Assert.Equal("https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/scrcpy-win64-v3.3.4.zip", metadata.DownloadUri);
        Assert.Equal("d8a155b7c180b7ca4cdadd40712b8750b63f3aab48cb5b8a2a39ac2d0d4c5d38", metadata.ChecksumSha256);
        Assert.Equal("https://github.com/Genymobile/scrcpy/releases/download/v3.3.4/SHA256SUMS.txt", metadata.Sha256SumsUri);
        Assert.Equal("https://github.com/Genymobile/scrcpy/releases/tag/v3.3.4", metadata.HtmlUri);
    }

    [Fact]
    public void ParseSha256SumsFile_reads_matching_asset_hash()
    {
        var checksum = OfficialQuestToolingService.ParseSha256SumsFile("""
            d8a155b7c180b7ca4cdadd40712b8750b63f3aab48cb5b8a2a39ac2d0d4c5d38  scrcpy-win64-v3.3.4.zip
            393f7d5379dabd8aacc41184755c3d0df975cd2861353cb7a8d50e0835e2eb72  scrcpy-win32-v3.3.4.zip
            """,
            "scrcpy-win64-v3.3.4.zip");

        Assert.Equal("d8a155b7c180b7ca4cdadd40712b8750b63f3aab48cb5b8a2a39ac2d0d4c5d38", checksum);
    }

    [Theory]
    [InlineData(null, "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("", "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.0", "C:\\tools\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.1", "C:\\missing\\hzdb.exe", "1.0.1", true)]
    [InlineData("1.0.1", "C:\\tools\\hzdb.exe", "1.0.1", false)]
    public void NeedsInstall_matches_expected_conditions(string? installedVersion, string targetPath, string availableVersion, bool expected)
    {
        var result = OfficialQuestToolingService.NeedsInstall(
            installedVersion,
            targetPath,
            availableVersion,
            path => !path.Contains("missing", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expected, result);
    }
}
