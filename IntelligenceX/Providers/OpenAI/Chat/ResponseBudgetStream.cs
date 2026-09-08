using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>Bounds wire bytes before parsers can buffer an untrusted response.</summary>
internal sealed class ResponseBudgetStream : Stream {
    private readonly Stream _inner;
    private readonly long _maximum;
    private long _read;

    internal ResponseBudgetStream(Stream inner, long? maximum) {
        if (maximum.HasValue && maximum.Value < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        _inner = inner;
        _maximum = maximum ?? long.MaxValue;
    }

    internal static Task<string> ReadTextAsync(HttpContent content, long? maximum, CancellationToken cancellationToken) =>
        TaskCancellation.WaitAsync(ReadTextCoreAsync(content, maximum, cancellationToken), cancellationToken);

    private static async Task<string> ReadTextCoreAsync(HttpContent content, long? maximum, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new ResponseBudgetStream(await TaskCancellation.WaitAsync(content.ReadAsStreamAsync(),
            cancellationToken, abandoned => abandoned.Dispose()).ConfigureAwait(false), maximum);
        using var registration = cancellationToken.Register(stream.Dispose);
        using var buffer = new MemoryStream();
        try {
            // Keep copy buffers owned until a non-cooperative read finishes, even after the caller stops waiting.
            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        } catch (Exception) when (cancellationToken.IsCancellationRequested) {
            throw new OperationCanceledException(cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private int RequestCount(int count) => (int)Math.Min(count, Math.Min(int.MaxValue, _maximum - _read + (_maximum == long.MaxValue ? 0 : 1)));
    private int Record(int count) {
        _read = checked(_read + count);
        if (_read > _maximum) throw new InvalidDataException("The provider response exceeded its configured wire byte limit.");
        return count;
    }
    public override int Read(byte[] buffer, int offset, int count) => Record(_inner.Read(buffer, offset, RequestCount(count)));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Record(await _inner.ReadAsync(buffer, offset, RequestCount(count), cancellationToken).ConfigureAwait(false));
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _read; set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
