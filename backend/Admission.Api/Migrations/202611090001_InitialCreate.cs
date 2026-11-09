using Admission.Api.Data;
using Admission.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Admission.Api.Migrations;

[DbContext(typeof(AdmissionDbContext))]
[Migration("202611090001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "AdmissionClasses" (
                "Id" character varying(40) NOT NULL PRIMARY KEY,
                "Name" character varying(120) NOT NULL,
                "TotalSeats" integer NOT NULL CHECK ("TotalSeats" > 0),
                "CreatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE "AdmissionHolds" (
                "Id" character varying(40) NOT NULL PRIMARY KEY,
                "ClassId" character varying(40) NOT NULL REFERENCES "AdmissionClasses" ("Id") ON DELETE CASCADE,
                "StudentId" character varying(17) NOT NULL,
                "Status" character varying(16) NOT NULL CHECK ("Status" IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')),
                "CreatedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "ConfirmedAt" timestamp with time zone NULL
            );

            CREATE INDEX "IX_AdmissionHolds_ClassId_Status_ExpiresAt"
                ON "AdmissionHolds" ("ClassId", "Status", "ExpiresAt");

            CREATE UNIQUE INDEX "IX_AdmissionHolds_StudentId_ClassId_Active"
                ON "AdmissionHolds" ("StudentId", "ClassId")
                WHERE "Status" IN ('HELD', 'CONFIRMED');
            """);
    }


    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.11");
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
            entity.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });
            entity.HasIndex(x => new { x.StudentId, x.ClassId }).IsUnique()
                .HasDatabaseName("IX_AdmissionHolds_StudentId_ClassId_Active")
                .HasFilter("\"Status\" IN ('HELD', 'CONFIRMED')");
        });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"AdmissionHolds\"; DROP TABLE IF EXISTS \"AdmissionClasses\";");
    }
}
