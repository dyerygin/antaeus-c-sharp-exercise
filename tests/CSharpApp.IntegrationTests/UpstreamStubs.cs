using System.Text.Json;
using CSharpApp.TestData.Builders;
using WireMock.Logging;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpApp.IntegrationTests;

/// <summary>Canned upstream answers in the Platzi contract's shape (ASM-01, ASM-10).</summary>
public static class UpstreamStubs
{
    extension(WireMockServer upstream)
    {
        /// <summary>Requests to resources only: login and refresh calls (ADR-0014) left out.</summary>
        public IReadOnlyList<ILogEntry> ResourceEntries =>
            [.. upstream.LogEntries.Where(e => !e.RequestMessage!.Path.StartsWith("/api/v1/auth/", StringComparison.Ordinal))];

        /// <summary>Requests to <c>auth/login</c>.</summary>
        public IReadOnlyList<ILogEntry> LoginEntries =>
            [.. upstream.LogEntries.Where(e => string.Equals(e.RequestMessage!.Path, "/api/v1/auth/login", StringComparison.Ordinal))];

        /// <summary>Answers login with a 20-day access token (ASM-04) and returns that token.</summary>
        public string StubLogin()
        {
            var token = TestJwt.ExpiringAt(DateTimeOffset.UtcNow.AddDays(20), Guid.NewGuid().ToString("N"));
            upstream.StubJson("POST", "auth/login", 201, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["access_token"] = token,
                ["refresh_token"] = "refresh-" + token,
            });
            return token;
        }

        /// <summary>Answers <paramref name="method"/> on <c>/api/v1/{path}</c> with JSON (camelCase, like the upstream).</summary>
        public void StubJson(string method, string path, int status, object body) =>
            upstream.Given(Request.Create().WithPath($"/api/v1/{path}").UsingMethod(method))
                .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json")
                    .WithBody(JsonSerializer.Serialize(body, JsonSerializerOptions.Web)));

        /// <summary>Answers with a status and a raw body.</summary>
        public void StubRaw(string method, string path, int status, string body = "") =>
            upstream.Given(Request.Create().WithPath($"/api/v1/{path}").UsingMethod(method))
                .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

        /// <summary>The upstream's "not found": 400 with <c>EntityNotFoundError</c> (ASM-10).</summary>
        public void StubEntityNotFound(string method, string path) =>
            upstream.StubRaw(method, path, 400,
                """{"path":"/api/v1/x","timestamp":"2026-11-23T10:00:00.000Z","name":"EntityNotFoundError","message":"Could not find any entity"}""");
    }
}
