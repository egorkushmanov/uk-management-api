namespace UkManagement.Core.Domain;

/// <summary>Нарушение правила предметной области с кодом состояния HTTP.
/// Обработчик ApiExceptionHandler преобразует его в ответ с ошибкой.</summary>
public abstract class ApiException : Exception
{
    public int Status { get; }

    protected ApiException(int status, string message) : base(message)
        => Status = status;
}

public sealed class ValidationException(string message)
    : ApiException(400, message);

public sealed class UnauthorizedException(string message)
    : ApiException(401, message);

public sealed class ForbiddenException(string message)
    : ApiException(403, message);

public sealed class NotFoundException(string message)
    : ApiException(404, message);

public sealed class ConflictException(string message)
    : ApiException(409, message);

/// <summary>Недостаточно ТМЦ на складе для списания.</summary>
public sealed class InsufficientStockException(
    string name, decimal need, decimal stock)
    : ApiException(409, $"Недостаточно «{name}»: требуется {need:0.###}, "
                        + $"на складе {stock:0.###}");
