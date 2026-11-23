namespace CSharpApp.Application.Common;

/// <summary>A request that is well-formed but invalid against upstream data, for example an unknown <c>categoryId</c> on create.</summary>
/// <remarks>The API answers 400 ValidationProblem with the field name, like any other validation error (ADR-0005).</remarks>
public sealed class RequestValidationException(string field, string error) : Exception(error)
{
    /// <summary>Field name → error messages, in the shape of a ValidationProblem.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [error] };
}
