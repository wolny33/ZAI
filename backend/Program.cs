using Backend.Common;
using Backend.Data;
using Backend.Endpoints;
using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// The connection string is read from configuration
var connString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Default'.");

builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connString));

var app = builder.Build();

// Create the database tables and the administrator account on startup
var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();

await Db.ApplySchema(dataSource, AppContext.BaseDirectory, app.Logger);
await Db.SeedAdmin(dataSource, app.Configuration, app.Logger);

// Turns exceptions into error responses: ApiException keeps the status and details it carries,
// anything unexpected becomes a generic 500, details go to the log.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    IResult response;
    if (exception is ApiException api)
    {
        response = HttpHelpers.Problem(ctx, api.Status, api.Title, api.Detail, api.Errors, api.WwwAuthenticate);
    }
    else if (exception is BadHttpRequestException bad)
    {
        response = HttpHelpers.Problem(ctx, bad.StatusCode, "Bad Request", "The request could not be processed.");
    }
    else
    {
        var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled");
        log.LogError(exception, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        response = HttpHelpers.Problem(ctx, 500, "Internal Server Error", "An unexpected error occurred.");
    }
    await response.ExecuteAsync(ctx);
}));

// Respond with 406 when the client does not accept JSON
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !ContentNegotiation.AcceptsJson(ctx.Request))
    {
        throw new ApiException(406, "Not Acceptable", "Only application/json is available.");
    }
    await next();
});


// GET /api/health - check that the app is alive
app.MapGet("/api/health", () => Results.Json(new { status = "ok" }));

AuthEndpoints.Map(app);
SeriesEndpoints.Map(app);

// Unknown /api/* addresses return 404 in RFC 9457 format
app.MapMethods("/api/{**rest}", ["GET", "POST", "PUT", "PATCH", "DELETE"],
    (HttpContext ctx) => HttpHelpers.Problem(ctx, 404, "Not Found", "No such resource or operation."));


app.Run();
