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

        // ── Mapbox Travel Time Service ──
        services.Configure<MapboxOptions>(
            configuration.GetSection(MapboxOptions.SectionName));

        services.AddHttpClient<ITravelTimeService, MapboxTravelTimeService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
