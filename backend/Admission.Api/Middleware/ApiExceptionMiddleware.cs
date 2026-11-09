using System.Text.Json;
using Admission.Api.Contracts;
using Admission.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Admission.Api.Middleware;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException exception)
        {
            await WriteErrorAsync(context, exception.StatusCode, exception.Message);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            logger.LogInformation(exception, "A database uniqueness rule rejected the request.");
            await WriteErrorAsync(context, StatusCodes.Status409Conflict,
                "This student already has an active application or confirmed admission for this class.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled API error.");
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError,
                "Something went wrong on the server. Please try again later.");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, new ErrorResponse(message),
            new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted);
    }
}
