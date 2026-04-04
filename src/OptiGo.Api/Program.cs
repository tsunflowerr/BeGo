using OptiGo.Infrastructure;
using DotNetEnv;
using Scalar.AspNetCore;

// Đọc từ thư mục root của solution trước, nếu chạy qua dotnet run từ folder src/OptiGo.Api
Env.Load("../../.env");
// Đọc từ current directory nếu publish/docker
Env.Load(".env");

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
    app.MapScalarApiReference();
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

