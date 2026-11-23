using CSharpApp.Api;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
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

    builder.Services.AddOpenApi();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddOptions<PagingSettings>()
        .BindConfiguration(PagingSettings.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddApi();
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

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    var versionedEndpointRouteBuilder = app.NewVersionedApi();

    // The base commit's route, kept until the products endpoints replace it.
    versionedEndpointRouteBuilder.MapGet(
            "api/v{version:apiVersion}/getproducts",
            (IProductsClient products, IOptions<PagingSettings> paging, CancellationToken ct) =>
                products.GetAllAsync(0, paging.Value.DefaultLimit, ct))
        .WithName("GetProducts")
        .HasApiVersion(1.0);

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
