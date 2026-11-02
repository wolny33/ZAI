using Backend.Data;
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


// GET /api/health - check that the app is alive
app.MapGet("/api/health", () => Results.Json(new { status = "ok" }));

app.Run();
