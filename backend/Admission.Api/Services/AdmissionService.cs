using System.Data;
using Admission.Api.Contracts;
using Admission.Api.Data;
using Admission.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Admission.Api.Services;

public sealed class AdmissionService(
    AdmissionDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private readonly int _holdDurationSeconds = Math.Clamp(
        int.TryParse(configuration["HOLD_DURATION_SECONDS"], out var configured) ? configured : 600,
        1,
        86400);

    public async Task<ClassResponse> CreateClassAsync(CreateClassRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new DomainException(400, "Class name is required and must be at most 120 characters.");
        if (request.TotalSeats < 1 || request.TotalSeats > 100000)
            throw new DomainException(400, "Total seats must be between 1 and 100000.");

        var entity = new AdmissionClass
        {
            Id = $"c_{Guid.NewGuid():N}",
            Name = name,
            TotalSeats = request.TotalSeats,
            CreatedAt = timeProvider.GetUtcNow()
        };

        db.AdmissionClasses.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new ClassResponse(entity.Id, entity.Name, entity.TotalSeats, 0, 0, entity.TotalSeats);
    }

    public async Task<IReadOnlyList<ClassResponse>> GetClassesAsync(CancellationToken cancellationToken)
    {
        await ExpireDueHoldsAsync(cancellationToken);
        var classes = await db.AdmissionClasses.AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var counts = await GetCountsAsync(classes.Select(x => x.Id).ToArray(), cancellationToken);
        return classes.Select(item => BuildClassResponse(item, counts)).ToList();
    }

    public async Task<ClassResponse> GetClassAsync(string classId, CancellationToken cancellationToken)
    {
        await ExpireDueHoldsAsync(cancellationToken);
        var entity = await db.AdmissionClasses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == classId, cancellationToken)
            ?? throw new DomainException(404, "Class not found.");
        var counts = await GetCountsAsync([classId], cancellationToken);
        return BuildClassResponse(entity, counts);
    }

    public async Task<CreateHoldResponse> CreateHoldAsync(
        string classId,
        string studentId,
        CancellationToken cancellationToken)
    {
        ValidateStudentId(studentId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // The class-row lock is held until commit. It coordinates every API process
        // connected to this PostgreSQL database, not only requests in this process.
        var lockedClasses = await db.AdmissionClasses
            .FromSqlInterpolated($"SELECT * FROM \"AdmissionClasses\" WHERE \"Id\" = {classId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var admissionClass = lockedClasses.SingleOrDefault()
            ?? throw new DomainException(404, "Class not found.");

        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AdmissionHolds"
            SET "Status" = 'EXPIRED'
            WHERE "ClassId" = {classId}
              AND "Status" = 'HELD'
              AND "ExpiresAt" <= {now}
            """, cancellationToken);

        var alreadyApplied = await db.AdmissionHolds.AnyAsync(x =>
            x.ClassId == classId && x.StudentId == studentId &&
            (x.Status == HoldStatus.Held || x.Status == HoldStatus.Confirmed), cancellationToken);
        if (alreadyApplied)
        {
            await transaction.CommitAsync(cancellationToken);
            throw new DomainException(409, "This student already has an active application or confirmed admission for this class.");
        }

        var heldSeats = await db.AdmissionHolds.CountAsync(x =>
            x.ClassId == classId && x.Status == HoldStatus.Held && x.ExpiresAt > now, cancellationToken);
        var confirmedSeats = await db.AdmissionHolds.CountAsync(x =>
            x.ClassId == classId && x.Status == HoldStatus.Confirmed, cancellationToken);
        if (heldSeats + confirmedSeats >= admissionClass.TotalSeats)
        {
            await transaction.CommitAsync(cancellationToken);
            throw new DomainException(409, "No seats are currently available in this class.");
        }

        var hold = new AdmissionHold
        {
            Id = $"h_{Guid.NewGuid():N}",
            ClassId = classId,
            StudentId = studentId,
            Status = HoldStatus.Held,
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_holdDurationSeconds)
        };
        db.AdmissionHolds.Add(hold);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CreateHoldResponse(hold.Id, hold.ClassId, hold.StudentId, "HELD", hold.ExpiresAt);
    }

    public async Task<HoldResponse> GetHoldAsync(string holdId, CancellationToken cancellationToken)
    {
        await ExpireDueHoldsAsync(cancellationToken);
        var hold = await db.AdmissionHolds.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == holdId, cancellationToken)
            ?? throw new DomainException(404, "Application not found.");
        return new HoldResponse(hold.Id, hold.ClassId, ToApiStatus(hold.Status), hold.ExpiresAt);
    }

    public async Task<ConfirmHoldResponse> ConfirmHoldAsync(
        string holdId,
        string studentId,
        CancellationToken cancellationToken)
    {
        ValidateStudentId(studentId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = await db.AdmissionHolds
            .FromSqlInterpolated($"SELECT * FROM \"AdmissionHolds\" WHERE \"Id\" = {holdId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var hold = rows.SingleOrDefault() ?? throw new DomainException(404, "Application not found.");
        if (hold.StudentId != studentId)
            throw new DomainException(404, "Application not found.");

        if (hold.Status == HoldStatus.Confirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ConfirmHoldResponse(hold.Id, "CONFIRMED");
        }

        if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= timeProvider.GetUtcNow())
        {
            hold.Status = HoldStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DomainException(409, "This application has expired. Please apply again if seats are available.");
        }

        if (hold.Status != HoldStatus.Held)
            throw new DomainException(409, "Only an active, unexpired application can be confirmed.");

        hold.Status = HoldStatus.Confirmed;
        hold.ConfirmedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ConfirmHoldResponse(hold.Id, "CONFIRMED");
    }

    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken cancellationToken)
    {
        ValidateStudentId(studentId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var rows = await db.AdmissionHolds
            .FromSqlInterpolated($"SELECT * FROM \"AdmissionHolds\" WHERE \"Id\" = {holdId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var hold = rows.SingleOrDefault() ?? throw new DomainException(404, "Application not found.");
        if (hold.StudentId != studentId)
            throw new DomainException(404, "Application not found.");

        if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= timeProvider.GetUtcNow())
            hold.Status = HoldStatus.Expired;
        else if (hold.Status == HoldStatus.Held)
            hold.Status = HoldStatus.Cancelled;
        else if (hold.Status == HoldStatus.Confirmed)
            throw new DomainException(409, "A confirmed admission cannot be cancelled through this endpoint.");

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MyHoldResponse>> GetMyHoldsAsync(string studentId, CancellationToken cancellationToken)
    {
        ValidateStudentId(studentId);
        await ExpireDueHoldsAsync(cancellationToken);
        var holds = await db.AdmissionHolds.AsNoTracking()
            .Include(x => x.Class)
            .Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return holds.Select(x => new MyHoldResponse(
            x.Id,
            x.ClassId,
            x.Class!.Name,
            ToApiStatus(x.Status),
            x.ExpiresAt)).ToList();
    }

    public async Task ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "AdmissionHolds"
            SET "Status" = 'EXPIRED'
            WHERE "Status" = 'HELD' AND "ExpiresAt" <= {now}
            """, cancellationToken);
    }

    private async Task<Dictionary<(string ClassId, HoldStatus Status), int>> GetCountsAsync(
        string[] classIds,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await db.AdmissionHolds.AsNoTracking()
            .Where(x => classIds.Contains(x.ClassId) &&
                (x.Status == HoldStatus.Confirmed || (x.Status == HoldStatus.Held && x.ExpiresAt > now)))
            .GroupBy(x => new { x.ClassId, x.Status })
            .Select(group => new { group.Key.ClassId, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(x => (x.ClassId, x.Status), x => x.Count);
    }

    private static ClassResponse BuildClassResponse(
        AdmissionClass entity,
        Dictionary<(string ClassId, HoldStatus Status), int> counts)
    {
        counts.TryGetValue((entity.Id, HoldStatus.Held), out var held);
        counts.TryGetValue((entity.Id, HoldStatus.Confirmed), out var confirmed);
        return new ClassResponse(entity.Id, entity.Name, entity.TotalSeats, held, confirmed,
            Math.Max(0, entity.TotalSeats - held - confirmed));
    }

    private static string ToApiStatus(HoldStatus status) => status.ToString().ToUpperInvariant();

    private static void ValidateStudentId(string studentId)
    {
        if (!StudentIdValidator.IsValid(studentId))
            throw new DomainException(400, "Student ID must be exactly 17 digits.");
    }
}
