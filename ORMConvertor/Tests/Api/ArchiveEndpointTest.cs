using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;

namespace Tests.Api;

/// <summary>
/// The complete-output download of S7 (decision 033), over HTTP (decision 043). The endpoint
/// translates nothing, so everything worth asserting about it is on the wire: that the answer
/// really is a ZIP, that the browser is told to save it, and that the entries come back under
/// the names the client gave them with the content it sent.
/// </summary>
[Collection(ApiCollection.Name)]
public class ArchiveEndpointTest(ApiTestHost host)
{
    private readonly HttpClient client = host.CreateClient();

    [Fact]
    public async Task TheNamedFilesComeBackAsAZipToDownload()
    {
        var files = new Dictionary<string, string>
        {
            ["Customer.cs"] = "namespace Sales;\r\n\r\npublic class Customer\r\n{\r\n}\r\n",
            ["Customer.hbm.xml"] = "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\r\n<hibernate-mapping />\r\n",
        };

        var response = await client.PostAsJsonAsync(
            ApiTestHost.BasePath + "/archive",
            new { files = files.Select(file => new { name = file.Key, content = file.Value }) },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("conversion.zip", response.Content.Headers.ContentDisposition?.FileName);

        using var archive = new ZipArchive(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            ZipArchiveMode.Read);

        Assert.Equal(files.Keys.ToHashSet(), archive.Entries.Select(entry => entry.FullName).ToHashSet());

        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            // Line endings included: what the download hands over has to be what the
            // conversion produced, and generated code carries CRLF.
            Assert.Equal(files[entry.FullName], reader.ReadToEnd(), ignoreLineEndingDifferences: false);
        }
    }

    /// <summary>
    /// An empty list is a valid request - the client decides what to pack - and answers with an
    /// empty archive rather than an error.
    /// </summary>
    [Fact]
    public async Task AnEmptyRequestPacksAnEmptyArchive()
    {
        var response = await client.PostAsJsonAsync(
            ApiTestHost.BasePath + "/archive",
            new { files = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var archive = new ZipArchive(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            ZipArchiveMode.Read);

        Assert.Empty(archive.Entries);
    }
}
