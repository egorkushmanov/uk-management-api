using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Core.Services;

/// <summary>Закрытие наряда-допуска главным инженером.</summary>
public interface IWorkOrderClosing
{
    Task<CloseResultDto> CloseAsync(int id, CloseDto dto);
}

public class ClosingService(
    IWorkOrderRepository orders,
    IRequestRepository requests,
    MaterialWriteOff writeOff,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IErpGateway erp,
    INotifier notifier) : IWorkOrderClosing
{
    public async Task<CloseResultDto> CloseAsync(int id, CloseDto dto)
    {
        WriteOffResult result;
        int requestId;
        await using (var tx = await uow.BeginAsync())
        {
            var order = await orders.LockAsync(id)
                        ?? throw new NotFoundException(
                            "Наряд-допуск не найден");
            if (order.Status != Statuses.Done)
                throw new ConflictException("Работы по наряду не завершены");
            var usages = await orders.PendingUsagesAsync(id);
            result = await writeOff.ApplyAsync(usages,
                dto.Rejected ?? Array.Empty<int>(), currentUser.Id);
            requestId = order.RequestId;
            await requests.SetStatusAsync(requestId, Statuses.Closed);
            await tx.CommitAsync();
        }
        if (result.Reorder.Count > 0)
            await erp.RequestPurchaseAsync(result.Reorder);
        await notifier.RequestClosedAsync(requestId, result.Cost);
        return new CloseResultDto(id, requestId, result.Cost,
            result.Reorder);
    }
}

public record WriteOffResult(decimal Cost, IReadOnlyList<string> Reorder);

/// <summary>Списание ТМЦ и расчёт стоимости материалов (SRP: правила
/// списания сосредоточены в одном классе). Исключение откатывает
/// транзакцию закрытия наряда.</summary>
public class MaterialWriteOff(
    IWorkOrderRepository orders,
    IMaterialRepository materials)
{
    public virtual async Task<WriteOffResult> ApplyAsync(
        IReadOnlyList<PendingUsage> usages, IReadOnlyCollection<int> rejected,
        int engineerId)
    {
        decimal cost = 0;
        var reorder = new List<string>();
        foreach (var usage in usages)
        {
            if (rejected.Contains(usage.Id))
            {
                await orders.SetApprovalAsync(usage.Id, Approvals.Rejected,
                    engineerId);
                continue;
            }
            var item = (await materials.LockAsync(usage.MaterialId))!;
            if (usage.Quantity > item.StockQty)
                throw new InsufficientStockException(item.Name,
                    usage.Quantity, item.StockQty);
            // остаток уменьшает триггер tr_usage_approve
            await orders.SetApprovalAsync(usage.Id, Approvals.Approved,
                engineerId);
            cost += usage.Quantity * usage.UnitPrice;
            if (item.StockQty - usage.Quantity < BusinessRules.ReorderLevel)
                reorder.Add(item.Name);
        }
        return new WriteOffResult(cost, reorder);
    }
}
