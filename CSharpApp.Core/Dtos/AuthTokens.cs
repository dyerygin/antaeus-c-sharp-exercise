using System.Text.Json.Serialization;

namespace CSharpApp.Core.Dtos;

/// <summary>The upstream login and refresh answer (<c>{"access_token","refresh_token"}</c>).</summary>
public sealed record AuthTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);
