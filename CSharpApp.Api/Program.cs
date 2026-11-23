using CSharpApp.Api;
using CSharpApp.Api.Endpoints;
using CSharpApp.Api.Middleware;
using Microsoft.AspNetCore.Routing;
using Serilog.Events;

// F-13: a bootstrap logger reports start-up failures (for example invalid settings) before configuration is read.
// It is a plain logger, not Serilog's reloadable one: that one can be frozen once per process, which breaks a second
// host in the same process (every WebApplicationFactory in the tests). Each host builds its own logger instead.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services),
        preserveStaticLogger: true);

    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApi(builder.Configuration);
    builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());

    var app = builder.Build();

    // T8: one line per request with method, route template, status and time; no query string, headers, body or client IP.
    app.UseSerilogRequestLogging(options =>
    {
        // The static logger is the bootstrap one (above), so request events go to this host's configured logger.
        options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        options.MessageTemplate = "HTTP {RequestMethod} {RoutePattern} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetMessageTemplateProperties = (context, _, elapsed, statusCode) =>
        [
            new("RequestMethod", new ScalarValue(context.Request.Method)),
            new("RoutePattern", new ScalarValue((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)")),
            new("StatusCode", new ScalarValue(statusCode)),
            new("Elapsed", new ScalarValue(elapsed)),
        ];
    });

    // R21 order: errors become ProblemDetails (ADR-0005); bare 4xx/5xx without a body get one as well.
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    if (!app.Environment.IsDevelopment())
    {
        // ASM-09: TLS ends at a reverse proxy; HSTS is sent only on HTTPS requests, so it is inert on plain HTTP.
        app.UseHsts();
    }

    // A1: the OpenAPI document (JSON and YAML) only where configured: Development and the Docker demo.
    if (app.Configuration.GetValue<bool>("OpenApi:Enabled"))
    {
        app.MapOpenApi();
        app.MapOpenApi("/openapi/{documentName}.yaml");
    }

    app.MapProductsEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "The API failed to start");
    Environment.ExitCode = 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
