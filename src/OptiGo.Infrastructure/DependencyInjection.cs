using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptiGo.Application.Interfaces;
using OptiGo.Infrastructure.ExternalServices.Mapbox;
using OptiGo.Infrastructure.Persistence;
using OptiGo.Infrastructure.Persistence.Repositories;

namespace OptiGo.Infrastructure;

/// <summary>
/// Extension method để đăng ký tất cả Infrastructure services vào DI container.
/// Giữ cho Program.cs sạch sẽ và dễ đọc.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── PostgreSQL + EF Core ──
        services.AddDbContext<OptiGoDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(OptiGoDbContext).Assembly.FullName);
                });
        });

        // ── Unit of Work ──
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OptiGoDbContext>());

        // ── Repositories ──
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();

        // ── External APIs (Hybrid Network) ──
        // 1. Google cho Places API (Mạnh về POI Việt Nam)
        services.Configure<OptiGo.Infrastructure.ExternalServices.Google.GoogleOptions>(
            configuration.GetSection("Google"));
        
        services.AddHttpClient<IPlacesProvider, OptiGo.Infrastructure.ExternalServices.Google.GooglePlacesProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 2. Mapbox cho Matrix API (Miễn phí 100k elements/tháng, rẻ hơn Google rất nhiều)
        services.Configure<OptiGo.Infrastructure.ExternalServices.Mapbox.MapboxOptions>(
            configuration.GetSection("Mapbox"));

        services.AddHttpClient<ITravelTimeService, OptiGo.Infrastructure.ExternalServices.Mapbox.MapboxTravelTimeService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
