using OptiGo.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (DB, Redis, Mapbox, Repositories) ──
builder.Services.AddInfrastructure(builder.Configuration);

// ── MediatR (Application layer CQRS) ──
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(OptiGo.Application.Interfaces.IUnitOfWork).Assembly));

// ── Controllers ──
builder.Services.AddControllers();

// ── OpenAPI / Swagger ──
builder.Services.AddOpenApi();

// ── CORS (cho Next.js frontend) ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:3000"];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // Cho SignalR
    });
});

var app = builder.Build();

// ── Middleware Pipeline ──
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.MapControllers();

// ── Health check endpoint ──
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0-alpha"
})).WithName("HealthCheck");

app.Run();

