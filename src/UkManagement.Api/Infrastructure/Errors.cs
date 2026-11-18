using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Domain;

namespace UkManagement.Api.Infrastructure;

/// <summary>Единый формат ответа об ошибке.</summary>
public record ApiError(bool Error, int Status, string Message,
    DateTimeOffset Timestamp);

/// <summary>Формирование ответов об ошибках в одном месте (DRY): исключения
/// сервисов, ошибки проверки данных, ответы 401 и 403 аутентификации.
/// </summary>
public static class ErrorResponses
{
    public static ApiError Create(int status, string message, IClock clock)
        => new(true, status, message, Truncate(clock.Now));

    /// <summary>Время ошибки с точностью до миллисекунд.</summary>
    private static DateTimeOffset Truncate(DateTimeOffset t)
        => t.AddTicks(-(t.Ticks % TimeSpan.TicksPerMillisecond));

    public static Task WriteAsync(HttpContext http, int status,
        string message)
    {
        var clock = http.RequestServices.GetRequiredService<IClock>();
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(Create(status, message, clock));
    }

    /// <summary>Ответ 400 при нарушении атрибутов проверки DTO.</summary>
    public static IActionResult InvalidModel(ActionContext context)
    {
        var messages = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrEmpty(e.ErrorMessage)
                ? "Некорректный формат данных запроса" : e.ErrorMessage)
            .Distinct();
        var clock = context.HttpContext.RequestServices
            .GetRequiredService<IClock>();
        return new BadRequestObjectResult(
            Create(400, string.Join("; ", messages), clock));
    }
}

/// <summary>Преобразует исключения в ответы с кодом состояния HTTP.</summary>
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> log)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http,
        Exception exception, CancellationToken cancellationToken)
    {
        var (status, message) = exception switch
        {
            ApiException e => (e.Status, e.Message),
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
                => (409, "Запись с такими данными уже существует"),
            BadHttpRequestException => (400,
                "Некорректный формат данных запроса"),
            _ => (500, "Внутренняя ошибка сервера")
        };
        if (status == 500)
            log.LogError(exception, "Необработанная ошибка");
        await ErrorResponses.WriteAsync(http, status, message);
        return true;
    }
}
