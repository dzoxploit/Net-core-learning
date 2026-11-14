using Microsoft.EntityFrameworkCore;
using Parkir.Models;

namespace Parkir.Data;

/// <summary>Maps onto the tables created by Database/ParkirDb.sql (the script is the source of truth).</summary>
public class ParkirDbContext(DbContextOptions<ParkirDbContext> options) : DbContext(options)
{
    public DbSet<Tarif> Tarif => Set<Tarif>();
    public DbSet<Transaksi> Transaksi => Set<Transaksi>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tarif>(e =>
        {
            e.ToTable("TARIF");
            e.HasKey(x => x.TarifId);
            e.Property(x => x.JenisKendaraan).HasMaxLength(30);
            e.Property(x => x.Tarif1).HasPrecision(18, 2);
            e.Property(x => x.Tarif2).HasPrecision(18, 2);
            e.Property(x => x.RowVer).IsRowVersion();
        });

        b.Entity<Transaksi>(e =>
        {
            e.ToTable("TRANSAKSI");
            e.HasKey(x => x.TransaksiId);
            // filled by the DEFAULT constraint (sequence), read back after insert
            e.Property(x => x.NoTransaksi).HasMaxLength(20).IsUnicode(false)
                .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            e.Property(x => x.NoPolisi).HasMaxLength(15);
            e.Property(x => x.Tarif1).HasPrecision(18, 2);
            e.Property(x => x.Tarif2).HasPrecision(18, 2);
            e.Property(x => x.Bayar).HasPrecision(18, 2);
            e.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasOne(x => x.Tarif).WithMany().HasForeignKey(x => x.TarifId);
        });
    }
}
