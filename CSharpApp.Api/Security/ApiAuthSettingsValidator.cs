using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Security;

/// <summary>Checks every key on start (DataAnnotations do not look inside list items); messages name the setting, never the key.</summary>
internal sealed class ApiAuthSettingsValidator : IValidateOptions<ApiAuthSettings>
{
    internal const int MinKeyLength = 32;

    public ValidateOptionsResult Validate(string? name, ApiAuthSettings options)
    {
        if (options.Keys.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                "ApiAuth:Keys is empty: set API_KEY in .env for Docker, or ApiAuth:Keys in user-secrets/appsettings.Development.json locally (see .env.example).");
        }

        var failures = new List<string>();
        for (var i = 0; i < options.Keys.Count; i++)
        {
            var client = options.Keys[i];
            if (string.IsNullOrWhiteSpace(client.ClientId))
            {
                failures.Add($"ApiAuth:Keys:{i}:ClientId is empty.");
            }

            if (client.Key.Length < MinKeyLength)
            {
                failures.Add($"ApiAuth:Keys:{i}:Key must be at least {MinKeyLength} characters.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
