namespace CSharpApp.Core.Interfaces;

/// <summary>A valid upstream access token for outgoing calls.</summary>
/// <remarks>Cached until shortly before it expires; concurrent callers share one login (ADR-0014).</remarks>
public interface IAccessTokenProvider
{
    /// <summary>The cached token, or a new one from refresh (first) or login.</summary>
    ValueTask<string> GetTokenAsync(CancellationToken ct);

    /// <summary>Marks <paramref name="token"/> as rejected; ignored when a newer token is already cached.</summary>
    void Invalidate(string token);
}
