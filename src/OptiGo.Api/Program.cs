using OptiGo.Infrastructure;
using DotNetEnv;
using Scalar.AspNetCore;
using OptiGo.Api.Hubs;
using OptiGo.Api.Services;
using OptiGo.Api.Validators;
using OptiGo.Application.Interfaces;
using FluentValidation;
using FluentValidation.AspNetCore;
using MediatR;

Env.Load("../../.env");

Env.Load(".env");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddSignalR();
builder.Services.AddScoped<ISessionNotifier, SignalRSessionNotifier>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(OptiGo.Application.Interfaces.IUnitOfWork).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(OptiGo.Api.Behaviors.ValidationBehavior<,>));
});

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateSessionCommandValidator>();

builder.Services.AddControllers();

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

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (FluentValidation.ValidationException ex)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "Validation Failed",
            Details = ex.Errors.Select(e => e.ErrorMessage)
        });
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.MapControllers();

app.MapHub<SessionHub>("/hubs/session");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0-alpha"
})).WithName("HealthCheck");

app.Run();
