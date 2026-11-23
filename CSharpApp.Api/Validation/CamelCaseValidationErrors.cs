using System.Text.Json;

namespace CSharpApp.Api.Validation;

/// <summary>Names the members in validation problems as the wire contract does: camelCase (ADR-0008).</summary>
/// <remarks>
/// .NET 10 validation reports body members by their CLR names (<c>Title</c>) but query and route parameters by their
/// parameter names (<c>limit</c>); the JSON contract is camelCase throughout, so a client must find <c>errors.title</c>.
/// Keys that are not member paths (for example the <c>Idempotency-Key</c> header) are left as they are.
/// </remarks>
public static class CamelCaseValidationErrors
{
    /// <summary>A <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/> callback.</summary>
    public static void Apply(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is not HttpValidationProblemDetails { Errors.Count: > 0 } validation)
        {
            return;
        }

        foreach (var (key, messages) in validation.Errors.ToList())
        {
            if (!IsMemberPath(key))
            {
                continue;
            }

            var camel = string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
            if (!string.Equals(camel, key, StringComparison.Ordinal))
            {
                validation.Errors.Remove(key);
                validation.Errors[camel] = validation.Errors.TryGetValue(camel, out var existing) ? [.. existing, .. messages] : messages;
            }
        }
    }

    private static bool IsMemberPath(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '[' or ']' or '_');
}
