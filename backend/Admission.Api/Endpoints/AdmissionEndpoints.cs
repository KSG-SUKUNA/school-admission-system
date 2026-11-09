using Admission.Api.Contracts;
using Admission.Api.Services;

namespace Admission.Api.Endpoints;

public static class AdmissionEndpoints
{
    public static IEndpointRouteBuilder MapAdmissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/classes", async (
            CreateClassRequest request,
            AdmissionService service,
            CancellationToken cancellationToken) =>
        {
            var created = await service.CreateClassAsync(request, cancellationToken);
            return Results.Created($"/classes/{created.Id}", new
            {
                id = created.Id,
                name = created.Name,
                totalSeats = created.TotalSeats
            });
        });

        endpoints.MapGet("/classes", async (AdmissionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetClassesAsync(cancellationToken)));

        endpoints.MapGet("/classes/{classId}", async (
            string classId,
            AdmissionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetClassAsync(classId, cancellationToken)));

        endpoints.MapPost("/classes/{classId}/holds", async (
            string classId,
            HttpContext context,
            AdmissionService service,
            CancellationToken cancellationToken) =>
        {
            var studentId = context.Request.Headers["X-Student-Id"].FirstOrDefault();
            if (!StudentIdValidator.IsValid(studentId))
                throw new DomainException(400, "Student ID must be supplied in X-Student-Id and contain exactly 17 digits.");
            var created = await service.CreateHoldAsync(classId, studentId!, cancellationToken);
            return Results.Created($"/holds/{created.HoldId}", created);
        });

        endpoints.MapGet("/holds/{holdId}", async (
            string holdId,
            AdmissionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetHoldAsync(holdId, cancellationToken)));

        endpoints.MapPost("/holds/{holdId}/confirm", async (
            string holdId,
            HttpContext context,
            AdmissionService service,
            CancellationToken cancellationToken) =>
        {
            var studentId = context.Request.Headers["X-Student-Id"].FirstOrDefault();
            if (!StudentIdValidator.IsValid(studentId))
                throw new DomainException(400, "Student ID must be supplied in X-Student-Id and contain exactly 17 digits.");
            return Results.Ok(await service.ConfirmHoldAsync(holdId, studentId!, cancellationToken));
        });

        endpoints.MapDelete("/holds/{holdId}", async (
            string holdId,
            HttpContext context,
            AdmissionService service,
            CancellationToken cancellationToken) =>
        {
            var studentId = context.Request.Headers["X-Student-Id"].FirstOrDefault();
            if (!StudentIdValidator.IsValid(studentId))
                throw new DomainException(400, "Student ID must be supplied in X-Student-Id and contain exactly 17 digits.");
            await service.CancelHoldAsync(holdId, studentId!, cancellationToken);
            return Results.NoContent();
        });

        endpoints.MapGet("/me/holds", async (
            HttpContext context,
            AdmissionService service,
            CancellationToken cancellationToken) =>
        {
            var studentId = context.Request.Headers["X-Student-Id"].FirstOrDefault();
            if (!StudentIdValidator.IsValid(studentId))
                throw new DomainException(400, "Student ID must be supplied in X-Student-Id and contain exactly 17 digits.");
            return Results.Ok(await service.GetMyHoldsAsync(studentId!, cancellationToken));
        });

        return endpoints;
    }
}
