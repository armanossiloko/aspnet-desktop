using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AspNetDesktop.Hosting.Internal;

/// <summary>
/// Buffers the response in memory. The adapters need the whole response before handing it to the WebView,
/// so "starting" the response only freezes the status line and headers and runs the OnStarting callbacks.
/// </summary>
internal sealed class DesktopResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
{
    private readonly MemoryStream _buffer = new();
    private readonly ResponseBodyStream _stream;
    private List<(Func<object, Task> Callback, object State)>? _onStarting;
    private List<(Func<object, Task> Callback, object State)>? _onCompleted;
    private PipeWriter? _writer;
    private int _statusCode = StatusCodes.Status200OK;
    private string? _reasonPhrase;
    private bool _starting;
    private bool _completed;

    public DesktopResponseFeature()
    {
        _stream = new ResponseBodyStream(this, _buffer);
    }

    public int StatusCode
    {
        get => _statusCode;
        set
        {
            ThrowIfStarted(nameof(StatusCode));
            _statusCode = value;
        }
    }

    public string? ReasonPhrase
    {
        get => _reasonPhrase;
        set
        {
            ThrowIfStarted(nameof(ReasonPhrase));
            _reasonPhrase = value;
        }
    }

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    [Obsolete("Use IHttpResponseBodyFeature.Stream instead.")]
    public Stream Body
    {
        get => _stream;
        set => throw new NotSupportedException("Replace IHttpResponseBodyFeature to wrap the response body.");
    }

    public bool HasStarted { get; private set; }

    public Stream Stream => _stream;

    public PipeWriter Writer => _writer ??= PipeWriter.Create(_stream, new StreamPipeWriterOptions(leaveOpen: true));

    public void OnStarting(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ThrowIfStarted(nameof(OnStarting));
        (_onStarting ??= []).Add((callback, state));
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        (_onCompleted ??= []).Add((callback, state));
    }

    public void DisableBuffering()
    {
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (HasStarted || _starting)
        {
            return;
        }

        _starting = true;
        if (_onStarting is not null)
        {
            // Registration order is reversed, matching Kestrel.
            for (int i = _onStarting.Count - 1; i >= 0; i--)
            {
                (Func<object, Task> callback, object state) = _onStarting[i];
                await callback(state);
            }
        }

        HasStarted = true;
        if (Headers is HeaderDictionary headers)
        {
            headers.IsReadOnly = true;
        }
    }

    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
        if (_writer is not null)
        {
            await _writer.FlushAsync(cancellationToken);
        }

        await SendFileFallback.SendFileAsync(_stream, path, offset, count, cancellationToken);
    }

    public async Task CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        await StartAsync();
        if (_writer is not null)
        {
            await _writer.CompleteAsync();
        }

        _completed = true;
    }

    public async Task FireOnCompletedAsync(Action<Exception> onError)
    {
        if (_onCompleted is null)
        {
            return;
        }

        for (int i = _onCompleted.Count - 1; i >= 0; i--)
        {
            (Func<object, Task> callback, object state) = _onCompleted[i];
            try
            {
                await callback(state);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }
    }

    public ArraySegment<byte> GetBody() =>
        _buffer.TryGetBuffer(out ArraySegment<byte> segment) ? segment : new ArraySegment<byte>(_buffer.ToArray());

    private void ThrowIfStarted(string member)
    {
        if (HasStarted)
        {
            throw new InvalidOperationException($"{member} cannot be set because the response has already started.");
        }
    }

    private sealed class ResponseBodyStream(DesktopResponseFeature feature, MemoryStream buffer) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => feature.StartAsync().GetAwaiter().GetResult();

        public override Task FlushAsync(CancellationToken cancellationToken) => feature.StartAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> source)
        {
            feature.StartAsync().GetAwaiter().GetResult();
            buffer.Write(source);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await feature.StartAsync(cancellationToken);
            buffer.Write(source.Span);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
