namespace Admission.Api.Contracts;

public sealed record CreateClassRequest(string? Name, int TotalSeats);
public sealed record ClassResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);
public sealed record CreateHoldResponse(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    DateTimeOffset ExpiresAt);
public sealed record HoldResponse(
    string HoldId,
    string ClassId,
    string Status,
    DateTimeOffset ExpiresAt);
public sealed record MyHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    DateTimeOffset ExpiresAt);
public sealed record ConfirmHoldResponse(string HoldId, string Status);
public sealed record ErrorResponse(string Message);
