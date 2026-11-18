using UkManagement.Core.Abstractions;
using UkManagement.Core.Domain;

namespace UkManagement.Core;

/// <summary>Формирует текст уведомлений и рассылает их по всем
/// зарегистрированным каналам (OCP: новый канал — новый класс).</summary>
public class Notifier(IEnumerable<INotificationChannel> channels) : INotifier
{
    public Task EmergencyRegisteredAsync(int requestId, string address)
        => SendAsync(Roles.Engineer, null,
            $"Аварийная заявка {requestId}: {address}");

    public Task OrderAssignedAsync(int masterId, int workOrderId)
        => SendAsync(Roles.Master, masterId,
            $"Вам назначен наряд-допуск {workOrderId}");

    public Task OrderFinishedAsync(int workOrderId)
        => SendAsync(Roles.Engineer, null,
            $"Работы по наряду {workOrderId} выполнены, проверьте расход ТМЦ");

    public Task RequestClosedAsync(int requestId, decimal cost)
        => SendAsync(Roles.Accountant, null,
            $"Заявка {requestId} закрыта, материалы {cost:0.00} BYN");

    private Task SendAsync(string role, int? id, string text)
        => Task.WhenAll(channels.Select(c => c.SendAsync(role, id, text)));
}
