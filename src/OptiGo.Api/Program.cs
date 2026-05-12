using OptiGo.Infrastructure;
using DotNetEnv;
using Scalar.AspNetCore;
using OptiGo.Api.Hubs;
using OptiGo.Api.Middleware;
using OptiGo.Api.Services;
using OptiGo.Api.Validators;
using OptiGo.Application.Interfaces;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

Env.Load("../../.env");

Env.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddHostedService<ExpiredSessionCleanupService>();

builder.Services
    .AddAuthentication(GoogleBearerAuthenticationHandler.SchemeName)
    .AddScheme<GoogleBearerAuthenticationOptions, GoogleBearerAuthenticationHandler>(
        GoogleBearerAuthenticationHandler.SchemeName,
        options =>
        {
            var configuredClientIds = builder.Configuration
                .GetSection("Authentication:GoogleClientIds")
                .Get<string[]>() ?? [];
            var nextAuthGoogleClientId = builder.Configuration["AUTH_GOOGLE_ID"];
            options.ClientIds = !string.IsNullOrWhiteSpace(nextAuthGoogleClientId)
                ? configuredClientIds.Append(nextAuthGoogleClientId).Distinct(StringComparer.Ordinal).ToArray()
                : configuredClientIds;
        });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("standard", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        }));
    options.AddPolicy("expensive", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        GetRateLimitPartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 40,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddScoped<ISessionNotifier, SignalRSessionNotifier>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(OptiGo.Application.Interfaces.IUnitOfWork).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(OptiGo.Api.Behaviors.ValidationBehavior<,>));
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateSessionCommandValidator>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

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
            .AllowCredentials();
    });
});

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers().RequireRateLimiting("standard");

app.MapHub<SessionHub>("/hubs/session").RequireAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0-alpha"
})).WithName("HealthCheck").AllowAnonymous();

app.Run();

static string GetRateLimitPartitionKey(HttpContext context) =>
    context.User.Identity?.IsAuthenticated == true
        ? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "authenticated"
        : context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
