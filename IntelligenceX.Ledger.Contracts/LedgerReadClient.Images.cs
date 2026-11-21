using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    /// <summary>Uploads one image (PNG, JPEG, WebP or GIF, at most <see cref="CoordinationImage.MaximumBytes"/>) and returns
    /// its id. Uploading the same bytes again returns the same id.</summary>
    public async Task<CoordinationImage> UploadImageAsync(byte[] bytes, CancellationToken token = default) {
        if (bytes.Length is 0 or > CoordinationImage.MaximumBytes) throw new InvalidDataException("The image is empty or larger than 8 MB.");
        var mediaType = CoordinationImage.Sniff(bytes) ?? throw new InvalidDataException("Only PNG, JPEG, WebP and GIF images can be sent.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/coordination/v1/images") { Content = content };
        using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            await ThrowCommandRejectionAsync(response, deadline.Token).ConfigureAwait(false);
        var image = await ReadResponseAsync(response, LedgerReadJson.Default.CoordinationImage, deadline.Token).ConfigureAwait(false);
        if (image.Id != Convert.ToHexStringLower(SHA256.HashData(bytes))) throw new InvalidDataException("The Ledger returned a different image id.");
        return image;
    }

    /// <summary>Downloads one image into <paramref name="directory"/> as <c>&lt;prefix&gt;&lt;id&gt;.&lt;ext&gt;</c> and returns the
    /// file path. The bytes are checked against the id, and an existing file with the right content is reused.</summary>
    public async Task<string> DownloadImageAsync(string id, string directory, CancellationToken token = default, string prefix = "") {
        if (!CoordinationImage.ValidId(id)) throw new ArgumentException("Not a Ledger image id.", nameof(id));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await _client.GetAsync("api/coordination/v1/images/" + id, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new FileNotFoundException("The Ledger has no image " + id + ".");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > CoordinationImage.MaximumBytes) throw new InvalidDataException("The image exceeds its bound.");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) > 0) {
            if (buffer.Length + read > CoordinationImage.MaximumBytes) throw new InvalidDataException("The image exceeds its bound.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != id) throw new InvalidDataException("The image does not match its id.");
        var mediaType = CoordinationImage.Sniff(bytes) ?? throw new InvalidDataException("The Ledger returned something that is not an image.");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, prefix + id + CoordinationImage.Extension(mediaType));
        if (!File.Exists(file) || Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, deadline.Token).ConfigureAwait(false))) != id)
            await File.WriteAllBytesAsync(file, bytes, deadline.Token).ConfigureAwait(false);
        return file;
    }
}
