using Admission.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Admission.Api.Data;

public sealed class AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : DbContext(options)
{
    public DbSet<AdmissionClass> AdmissionClasses => Set<AdmissionClass>();
    public DbSet<AdmissionHold> AdmissionHolds => Set<AdmissionHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdmissionClass>(entity =>
        {
            entity.ToTable("AdmissionClasses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.TotalSeats).IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<AdmissionHold>(entity =>
        {
            entity.ToTable("AdmissionHolds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(40);
            entity.Property(x => x.ClassId).HasMaxLength(40).IsRequired();
            entity.Property(x => x.StudentId).HasMaxLength(17).IsRequired();
            entity.Property(x => x.Status)
                .HasConversion(
                    value => value.ToString().ToUpperInvariant(),
                    value => Enum.Parse<HoldStatus>(value, true))
                .HasMaxLength(16)
                .IsRequired();
            entity.Property(x => x.CreatedAt).IsRequired();
            entity.Property(x => x.ExpiresAt).IsRequired();
            entity.HasOne(x => x.Class)
                .WithMany()
                .HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });
            entity.HasIndex(x => new { x.StudentId, x.ClassId })
                .IsUnique()
                .HasDatabaseName("IX_AdmissionHolds_StudentId_ClassId_Active")
                .HasFilter("\"Status\" IN ('HELD', 'CONFIRMED')");
        });
    }
}
