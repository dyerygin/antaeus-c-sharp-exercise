using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CSharpApp.IntegrationTests;

/// <summary>Canned upstream answers in the Platzi contract's shape (ASM-01, ASM-10).</summary>
public static class UpstreamStubs
{
    extension(WireMockServer upstream)
    {
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
