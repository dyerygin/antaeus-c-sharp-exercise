namespace CSharpApp.Core.Settings;

/// <summary>Outbound HTTP client settings (section <c>HttpClientSettings</c>).</summary>
public sealed class HttpClientSettings
{
    public int LifeTime { get; set; }
    public int RetryCount { get; set; }
    public int SleepDuration { get; set; }
}
