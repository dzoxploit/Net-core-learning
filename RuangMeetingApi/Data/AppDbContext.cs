using Microsoft.EntityFrameworkCore;
using RuangMeetingApi.Models;

namespace RuangMeetingApi.Data;

/// <summary>Maps onto the tables created by Database/RuangMeetingDb.sql (the script is the source of truth).</summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ruangan> Ruangan => Set<Ruangan>();
    public DbSet<Reservasi> Reservasi => Set<Reservasi>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Ruangan>(e =>
        {
            e.ToTable("RUANGAN");
            e.HasKey(x => x.RuanganId);
            e.Property(x => x.KodeRuangan).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.NamaRuangan).HasMaxLength(100);
            e.Property(x => x.Lokasi).HasMaxLength(100);
            e.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
            e.Property(x => x.RowVer).IsRowVersion();
        });

        b.Entity<Reservasi>(e =>
        {
            // the table has a trigger: tell EF so it doesn't use a plain OUTPUT clause
            e.ToTable("RESERVASI", t => t.HasTrigger("TR_RESERVASI_NoOverlap"));
            e.HasKey(x => x.ReservasiId);
            e.Property(x => x.KodeReservasi).HasMaxLength(20).IsUnicode(false)
                .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            e.Property(x => x.NamaPemesan).HasMaxLength(100);
            e.Property(x => x.JudulMeeting).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.CreatedAt).ValueGeneratedOnAdd();
            e.Property(x => x.RowVer).IsRowVersion();
            e.HasOne(x => x.Ruangan).WithMany().HasForeignKey(x => x.RuanganId);
        });
    }
}
