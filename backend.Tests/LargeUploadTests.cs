using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PrinterApp;
using PrinterApp.Models;
using PrinterApp.Services;

namespace backend.Tests;

public sealed class LargeUploadTests
{
    [Fact]
    public async Task Upload_accepts_docx_larger_than_kestrel_default_body_limit()
    {
        await using var app = BackendStartup.Build([]);
        var baseUrl = await StartOnDynamicLoopbackPort(app);

        const int fileSize = 35 * 1024 * 1024;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var multipart = new MultipartFormDataContent();
        using var content = new StreamContent(new ZeroStream(fileSize));
        content.Headers.ContentType = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        multipart.Add(content, "file", "large-upload.docx");

        using var response = await client.PostAsync($"{baseUrl}/api/upload", multipart);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var upload = await response.Content.ReadFromJsonAsync<UploadResponse>();
        upload.Should().NotBeNull();
        upload!.Success.Should().BeTrue();
        upload.FileId.Should().NotBeNullOrWhiteSpace();

        var sessions = app.Services.GetRequiredService<FileSessionService>();
        var tempPath = sessions.GetFilePath(upload.FileId!);
        try
        {
            tempPath.Should().NotBeNullOrWhiteSpace();
            File.Exists(tempPath).Should().BeTrue();
            new FileInfo(tempPath!).Length.Should().Be(fileSize);
        }
        finally
        {
            sessions.RemoveFile(upload.FileId!);
            if (!string.IsNullOrWhiteSpace(tempPath))
                TryDelete(tempPath);
        }
    }

    private static async Task<string> StartOnDynamicLoopbackPort(WebApplication app)
    {
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        return app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .Should()
            .ContainSingle()
            .Subject;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class ZeroStream : Stream
    {
        private readonly long _length;
        private long _position;

        public ZeroStream(long length)
        {
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
                return 0;

            var bytesToRead = (int)Math.Min(count, _length - _position);
            Array.Clear(buffer, offset, bytesToRead);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _length)
                return ValueTask.FromResult(0);

            var bytesToRead = (int)Math.Min(buffer.Length, _length - _position);
            buffer.Span[..bytesToRead].Clear();
            _position += bytesToRead;
            return ValueTask.FromResult(bytesToRead);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
