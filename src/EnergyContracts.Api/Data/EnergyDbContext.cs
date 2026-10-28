using EnergyContracts.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnergyContracts.Api.Data;

public sealed class EnergyDbContext(DbContextOptions<EnergyDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ConsumptionReading> ConsumptionReadings => Set<ConsumptionReading>();
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Property(c => c.CompanyName).HasMaxLength(200);
        modelBuilder.Entity<Customer>().Property(c => c.ContactEmail).HasMaxLength(254);
        modelBuilder.Entity<Contract>().HasOne<Customer>().WithMany()
            .HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Contract>().HasIndex(c => new { c.CustomerId, c.StartDate, c.EndDate });
        modelBuilder.Entity<Contract>().Property(c => c.Status).IsConcurrencyToken();
        modelBuilder.Entity<ConsumptionReading>().HasKey(r => new { r.ContractId, r.TimestampUtc });
        modelBuilder.Entity<ConsumptionReading>().HasOne<Contract>().WithMany()
            .HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PricePoint>().HasKey(p => new { p.ContractId, p.TimestampUtc });
        modelBuilder.Entity<PricePoint>().HasOne<Contract>().WithMany()
            .HasForeignKey(p => p.ContractId).OnDelete(DeleteBehavior.Restrict);

        // SQLite stores decimal values as text. Do all decimal arithmetic in the calculator.
        modelBuilder.Entity<Contract>().Property(c => c.AnnualVolumeKwh).HasPrecision(18, 4);
        modelBuilder.Entity<Contract>().Property(c => c.FixedPriceCtPerKwh).HasPrecision(18, 4);
        modelBuilder.Entity<ConsumptionReading>().Property(r => r.Kwh).HasPrecision(18, 4);
        modelBuilder.Entity<PricePoint>().Property(p => p.PriceCtPerKwh).HasPrecision(18, 4);
    }
}
