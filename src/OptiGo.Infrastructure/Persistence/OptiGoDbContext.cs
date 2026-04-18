using Microsoft.EntityFrameworkCore;
using OptiGo.Application.Interfaces;
using OptiGo.Domain.Entities;

namespace OptiGo.Infrastructure.Persistence;

public class OptiGoDbContext : DbContext, IUnitOfWork
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<PickupRequest> PickupRequests => Set<PickupRequest>();

    public OptiGoDbContext(DbContextOptions<OptiGoDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OptiGoDbContext).Assembly);
    }
}
