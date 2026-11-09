using Admission.Api.Background;
using Admission.Api.Data;
using Admission.Api.Endpoints;
using Admission.Api.Middleware;
using Admission.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<AdmissionDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AdmissionService>();
builder.Services.AddHostedService<ExpiredHoldWorker>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

app.UseMiddleware<ApiExceptionMiddleware>();
app.UseCors("frontend");

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    // Both API containers may start together. A PostgreSQL advisory lock keeps
    // schema migration single-writer across processes sharing this database.
    await db.Database.OpenConnectionAsync();
    var connection = db.Database.GetDbConnection();
    var migrationLockAcquired = false;
    try
    {
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandText = "SELECT pg_advisory_lock(718234987123)";
            await lockCommand.ExecuteScalarAsync();
            migrationLockAcquired = true;
        }
        await db.Database.MigrateAsync();
    }
    finally
    {
        if (migrationLockAcquired)
        {
            await using var unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = "SELECT pg_advisory_unlock(718234987123)";
            await unlockCommand.ExecuteScalarAsync();
        }
        await db.Database.CloseConnectionAsync();
    }
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAdmissionEndpoints();

app.Run();

public partial class Program { }
