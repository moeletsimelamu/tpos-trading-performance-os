using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MelamuHoldings.Application.Trading;
using MelamuHoldings.Infrastructure.Ai;

namespace MelamuHoldings.IntegrationTests;

// The HTTP provider against a stub handler: what it sends, and how failures become UpstreamException (no database)
public class OpenAiCompatibleProviderTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static (OpenAiCompatibleProvider Provider, StubHandler Handler) Create(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond, Action<AiReviewOptions>? configure = null)
    {
        var options = new AiReviewOptions { BaseUrl = "http://ai.local/v1/", Model = "local-model", ApiKey = "test-key" };
        configure?.Invoke(options);
        var handler = new StubHandler(respond);
        return (new OpenAiCompatibleProvider(new HttpClient(handler), Options.Create(options)), handler);
    }

    private static Task<HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    [Fact]
    public async Task Sends_one_system_and_one_user_message_with_no_tools_and_returns_the_reply()
    {
        var (provider, handler) = Create(_ => Json("""{"choices":[{"message":{"role":"assistant","content":"{\"facts\":[]}"}}]}"""));

        var reply = await provider.CompleteAsync("system rules", "<journal_data>{}</journal_data>", CancellationToken.None);

        Assert.Equal("{\"facts\":[]}", reply);
        Assert.Equal("http://ai.local/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", handler.Request.Headers.Authorization!.ToString());
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal("local-model", body.GetProperty("model").GetString());
        Assert.Equal(["system", "user"], body.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.False(body.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task Works_without_a_key_and_without_json_mode_for_local_servers()
    {
        var (provider, handler) = Create(_ => Json("""{"choices":[{"message":{"content":"ok"}}]}"""), o => { o.ApiKey = null; o.JsonMode = false; });

        await provider.CompleteAsync("s", "u", CancellationToken.None);

        Assert.Null(handler.Request!.Headers.Authorization);
        Assert.False(JsonDocument.Parse(handler.Body!).RootElement.TryGetProperty("response_format", out _));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"bad key sk-secret"}}""", "HTTP 401")]
    [InlineData(HttpStatusCode.OK, """{"choices":[]}""", "no answer")]
    [InlineData(HttpStatusCode.OK, "<html>gateway</html>", "expected format")]
    public async Task Failures_become_upstream_errors_without_echoing_the_provider_body(HttpStatusCode status, string body, string message)
    {
        var (provider, _) = Create(_ => Json(body, status));

        var ex = await Assert.ThrowsAsync<UpstreamException>(() => provider.CompleteAsync("s", "u", CancellationToken.None));

        Assert.Contains(message, ex.Message);
        Assert.DoesNotContain("sk-secret", ex.Message);
    }

    [Fact]
    public async Task An_unreachable_provider_is_an_upstream_error()
    {
        var (provider, _) = Create(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<UpstreamException>(() => provider.CompleteAsync("s", "u", CancellationToken.None));

        Assert.Contains("could not be reached", ex.Message);
    }
}
