using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PrinterApp;

namespace backend.Tests;

public sealed class BackendCorsTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("https://app.local")]
    [InlineData("null")]
    public async Task Preflight_allows_local_frontend_origins(string origin)
    {
        await using var app = BackendStartup.Build([]);
        var baseUrl = await StartOnDynamicLoopbackPort(app);

        using var client = new HttpClient();
        using var request = Preflight(baseUrl, origin, "POST");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(origin);
    }

    [Fact]
    public async Task Preflight_rejects_non_loopback_web_origins()
    {
        await using var app = BackendStartup.Build([]);
        var baseUrl = await StartOnDynamicLoopbackPort(app);

        using var client = new HttpClient();
        using var request = Preflight(baseUrl, "https://evil.example", "POST");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private static HttpRequestMessage Preflight(string baseUrl, string origin, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, $"{baseUrl}/api/print");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        return request;
    }

    private static async Task<string> StartOnDynamicLoopbackPort(WebApplication app)
    {
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses;
        return addresses.Should().ContainSingle().Subject;
    }
}
