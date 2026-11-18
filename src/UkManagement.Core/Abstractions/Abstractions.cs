using UkManagement.Core.Contracts;

namespace UkManagement.Core.Abstractions;

/// <summary>Пользователь, выполняющий запрос (DIP: сервисы не зависят от
/// HttpContext и способа аутентификации).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int Id { get; }
    string Login { get; }
    string Role { get; }

    bool IsInRole(params string[] roles) => roles.Contains(Role);
}

/// <summary>Текущее время (в контрольных примерах фиксируется).</summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

/// <summary>Единица работы: операции сервиса выполняются в одной
/// транзакции базы данных.</summary>
public interface IUnitOfWork
{
    Task<ITransaction> BeginAsync();
}

public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Учётная запись, для которой выдаётся токен доступа.</summary>
public record Identity(int Id, string Login, string Name, string Role);

public interface ITokenService
{
    TokenResponse Issue(Identity identity);
}

/// <summary>Канал доставки уведомлений (push, e-mail, журнал).</summary>
public interface INotificationChannel
{
    string Name { get; }
    Task SendAsync(string recipientRole, int? recipientId, string text);
}

/// <summary>События процесса, о которых уведомляются участники.</summary>
public interface INotifier
{
    Task EmergencyRegisteredAsync(int requestId, string address);
    Task OrderAssignedAsync(int masterId, int workOrderId);
    Task OrderFinishedAsync(int workOrderId);
    Task RequestClosedAsync(int requestId, decimal cost);
}

/// <summary>Шлюз к 1С: Бухгалтерия.</summary>
public interface IErpGateway
{
    Task RequestPurchaseAsync(IReadOnlyList<string> materials);
}

/// <summary>Формат выгрузки отчёта (OCP: новый формат — новый класс).</summary>
public interface IReportExporter
{
    string Format { get; }
    string ContentType { get; }
    byte[] Export(SlaReportDto report);
}
