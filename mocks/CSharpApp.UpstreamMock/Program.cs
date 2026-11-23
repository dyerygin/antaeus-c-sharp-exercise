using CSharpApp.UpstreamMock;
using Microsoft.Extensions.Options;
using static CSharpApp.UpstreamMock.MockContracts;

// A fake of the upstream contract subset (ADR-0003, ASM-01): the original service is offline (ASM-02).
// The container health check runs this process with --health-probe; it probes the running mock and exits.
if (args.Contains(HealthProbe.Argument, StringComparer.Ordinal))
{
    Environment.ExitCode = await HealthProbe.RunAsync(
        HealthProbe.PortFrom(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")), TimeSpan.FromSeconds(2));
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<MockAuthSettings>().BindConfiguration(MockAuthSettings.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MockStore>();
builder.Services.AddSingleton<MockTokens>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");

api.MapPost("auth/login", (LoginRequest body, MockStore store, MockTokens tokens) =>
    store.FindUser(body.Email, body.Password) is { } user
        ? Results.Json(tokens.Issue(user.Id), statusCode: StatusCodes.Status201Created)
        : Results.Json(new { message = "Unauthorized", statusCode = 401 }, statusCode: StatusCodes.Status401Unauthorized));

api.MapPost("auth/refresh-token", (RefreshRequest body, MockTokens tokens) =>
    body.RefreshToken is { } token && tokens.Validate(token, MockTokens.Refresh) is { } userId
        ? Results.Json(tokens.Issue(userId), statusCode: StatusCodes.Status201Created)
        : Results.Json(new { message = "Invalid", error = "Unauthorized", statusCode = 401 }, statusCode: StatusCodes.Status401Unauthorized));

// ASM-06: only the profile is guarded upstream.
api.MapGet("auth/profile", (HttpContext context, MockStore store, MockTokens tokens) =>
    BearerUserId(context, tokens) is { } userId && store.FindUser(userId) is { } user
        ? Results.Ok(new { user.Id, user.Email, user.Name, user.Role, user.Avatar })
        : Unauthorized());

var resources = api.MapGroup(string.Empty).AddEndpointFilter(async (context, next) =>
{
    var settings = context.HttpContext.RequestServices.GetRequiredService<IOptions<MockAuthSettings>>().Value;
    var tokens = context.HttpContext.RequestServices.GetRequiredService<MockTokens>();
    return settings.RequireTokenOnResources && BearerUserId(context.HttpContext, tokens) is null ? Unauthorized() : await next(context);
});

resources.MapGet("products", (int? offset, int? limit, MockStore store) => Results.Ok(store.GetProducts(offset ?? 0, limit)));

resources.MapGet("products/{id}", (string id, HttpContext context, MockStore store) =>
    FindById(id, store.GetProduct, context, "Product"));

resources.MapPost("products", (CreateProductRequest body, HttpContext context, MockStore store) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(body.Title)) { errors.Add("title should not be empty"); }
    if (body.Price is not > 0) { errors.Add("price must be a positive number"); }
    if (string.IsNullOrWhiteSpace(body.Description)) { errors.Add("description should not be empty"); }
    if (body.CategoryId is null) { errors.Add("categoryId must be a number conforming to the specified constraints"); }
    if (body.Images is not { Count: > 0 }) { errors.Add("images must contain at least 1 elements"); }
    else if (!body.Images.All(IsHttpUrl)) { errors.Add("each value in images must be a URL address"); }
    if (errors.Count > 0) { return ValidationFailed(errors); }

    // ASM-10: an unknown categoryId is reported as EntityNotFoundError, like an unknown id.
    return store.GetCategory(body.CategoryId!.Value) is { } category
        ? Results.Json(store.AddProduct(body.Title!, body.Price!.Value, body.Description!, category, body.Images!), statusCode: StatusCodes.Status201Created)
        : EntityNotFound(context, "Category", body.CategoryId.Value);
});

resources.MapGet("categories", (int? limit, MockStore store) => Results.Ok(store.GetCategories(limit)));

resources.MapGet("categories/{id}", (string id, HttpContext context, MockStore store) =>
    FindById(id, store.GetCategory, context, "Category"));

resources.MapPost("categories", (CreateCategoryRequest body, MockStore store) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(body.Name)) { errors.Add("name should not be empty"); }
    if (!IsHttpUrl(body.Image)) { errors.Add("image must be a URL address"); }
    return errors.Count > 0
        ? ValidationFailed(errors)
        : Results.Json(store.AddCategory(body.Name!, body.Image!), statusCode: StatusCodes.Status201Created);
});

await app.RunAsync();

static int? BearerUserId(HttpContext context, MockTokens tokens)
{
    const string scheme = "Bearer ";
    var header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith(scheme, StringComparison.Ordinal) ? tokens.Validate(header[scheme.Length..], MockTokens.Access) : null;
}

static bool IsHttpUrl(string? value) =>
    Uri.TryCreate(value, UriKind.Absolute, out var uri) && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal));

static IResult Unauthorized() =>
    Results.Json(new { message = "Unauthorized", statusCode = 401 }, statusCode: StatusCodes.Status401Unauthorized);

// The original parses ids with a pipe: a non-numeric id is a 400 validation error, not a 404.
static IResult FindById<T>(string id, Func<int, T?> find, HttpContext context, string entity)
    where T : class
{
    if (!int.TryParse(id, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var key))
    {
        return Results.BadRequest(new { message = "Validation failed (numeric string is expected)", error = "Bad Request", statusCode = 400 });
    }

    return find(key) is { } found ? Results.Ok(found) : EntityNotFound(context, entity, key);
}

static IResult ValidationFailed(IReadOnlyList<string> messages) =>
    Results.BadRequest(new { message = messages, error = "Bad Request", statusCode = 400 });

// ASM-10: the original reports a missing entity as 400 EntityNotFoundError (a TypeORM error), not 404.
static IResult EntityNotFound(HttpContext context, string entity, int id) => Results.BadRequest(new
{
    path = context.Request.Path.Value,
    timestamp = TimeProvider.System.GetUtcNow(),
    name = "EntityNotFoundError",
    message = $$"""Could not find any entity of type "{{entity}}" matching: { "id": {{id}} }""",
});
