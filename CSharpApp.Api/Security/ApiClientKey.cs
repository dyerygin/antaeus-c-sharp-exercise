namespace CSharpApp.Api.Security;

/// <summary>One client's API key.</summary>
/// <param name="ClientId">Who the key belongs to; becomes the <c>client_id</c> claim (rate-limit partition, ADR-0016).</param>
/// <param name="Key">The secret the client sends in <c>X-Api-Key</c>; at least 32 characters.</param>
public sealed record ApiClientKey(string ClientId = "", string Key = "");
