using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.OfficeIMO;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class OfficeImoReadToolTests {
    [Fact]
    public async Task OfficeImoRead_WhenExtensionsOmitted_DefaultsIncludePdf() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-officeimo-read-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);

        try {
            var pdfPath = Path.Combine(tempRoot, "sample.pdf");
            var txtPath = Path.Combine(tempRoot, "ignored.txt");
            File.WriteAllText(pdfPath, "not-a-real-pdf");
            File.WriteAllText(txtPath, "plain text");

            var options = new OfficeImoToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new OfficeImoReadTool(options);

            var json = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("path", tempRoot),
                cancellationToken: CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());

            var files = root.GetProperty("files").EnumerateArray()
                .Select(static x => x.GetString())
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => Path.GetFileName(x!))
                .ToArray();

            Assert.Contains("sample.pdf", files, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("ignored.txt", files, StringComparer.OrdinalIgnoreCase);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }
}
