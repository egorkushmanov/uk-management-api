using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;
using UkManagement.Core.Rules;

namespace UkManagement.Core.Services;

/// <summary>Назначение исполнителя (диспетчер).</summary>
public interface IWorkOrderAssignment
{
    Task<WorkOrderDto> AssignAsync(AssignDto dto);
}

/// <summary>Выполнение работ по наряду (мастер).</summary>
public interface IWorkOrderExecution
{
    Task<IReadOnlyList<WorkOrderDto>> MyOrdersAsync();
    Task<WorkOrderDto> GetAsync(int id);
    Task<WorkOrderDto> StartAsync(int id);
    Task<WorkOrderDto> FinishAsync(int id);
    Task<WorkOrderDto> AddUsageAsync(int id, AddUsageDto dto);
    Task<WorkOrderDto> RemoveUsageAsync(int id, int usageId);
}

public class WorkOrderService(
    IWorkOrderRepository orders,
    IRequestRepository requests,
    IMaterialRepository materials,
    IEnumerable<IAssignmentRule> rules,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    INotifier notifier) : IWorkOrderAssignment, IWorkOrderExecution
{
    public async Task<WorkOrderDto> AssignAsync(AssignDto dto)
    {
        int id;
        await using (var tx = await uow.BeginAsync())
        {
            var request = await requests.LockAsync(dto.RequestId)
                          ?? throw new NotFoundException("Заявка не найдена");
            if (request.Status != Statuses.New)
                throw new ConflictException("Исполнитель уже назначен");
            var master = await orders.GetMasterAsync(dto.MasterId)
                         ?? throw new NotFoundException(
                             "Сотрудник не найден");
            var context = new AssignmentContext(dto, request, master);
            foreach (var rule in rules)
                rule.Check(context);
            id = await orders.InsertAsync(new NewWorkOrder(dto.RequestId,
                dto.MasterId, dto.WorkType.Trim(), dto.Safety?.Trim(),
                dto.PlannedStart, dto.PlannedEnd));
            await requests.SetStatusAsync(dto.RequestId, Statuses.Assigned);
            await tx.CommitAsync();
        }
        await notifier.OrderAssignedAsync(dto.MasterId, id);
        return (await orders.GetAsync(id))!;
    }

    public Task<IReadOnlyList<WorkOrderDto>> MyOrdersAsync()
        => orders.ListByMasterAsync(currentUser.Id);

    public async Task<WorkOrderDto> GetAsync(int id)
    {
        var order = await orders.GetAsync(id)
                    ?? throw new NotFoundException("Наряд-допуск не найден");
        if (currentUser.IsInRole(Roles.Master)
            && order.MasterId != currentUser.Id)
            throw new ForbiddenException("Наряд выдан другому мастеру");
        return order;
    }

    public Task<WorkOrderDto> StartAsync(int id)
        => ChangeAsync(id, Statuses.Assigned, async state =>
        {
            await orders.SetStartedAsync(id);
            await requests.SetStatusAsync(state.RequestId,
                Statuses.InProgress);
        });

    public async Task<WorkOrderDto> FinishAsync(int id)
    {
        var result = await ChangeAsync(id, Statuses.InProgress,
            async state =>
            {
                await orders.SetFinishedAsync(id);
                await requests.SetStatusAsync(state.RequestId,
                    Statuses.Done);
            });
        await notifier.OrderFinishedAsync(id);
        return result;
    }

    public Task<WorkOrderDto> AddUsageAsync(int id, AddUsageDto dto)
        => ChangeAsync(id, Statuses.InProgress, async _ =>
        {
            var material = await materials.GetAsync(dto.MaterialId)
                           ?? throw new NotFoundException("ТМЦ не найдены");
            if (await orders.UsageExistsAsync(id, dto.MaterialId))
                throw new ConflictException(
                    "Эти ТМЦ уже указаны в наряде");
            await orders.AddUsageAsync(id, dto.MaterialId, dto.Quantity,
                material.Price);
        });

    public Task<WorkOrderDto> RemoveUsageAsync(int id, int usageId)
        => ChangeAsync(id, Statuses.InProgress, async _ =>
        {
            var approval = await orders.GetUsageApprovalAsync(id, usageId)
                           ?? throw new NotFoundException(
                               "Позиция расхода не найдена");
            if (approval != Approvals.Pending)
                throw new ConflictException(
                    "Позиция уже рассмотрена главным инженером");
            await orders.DeleteUsageAsync(usageId);
        });

    /// <summary>Общий шаблон изменения наряда мастером (DRY): блокировка,
    /// проверка исполнителя и статуса, действие, фиксация.</summary>
    private async Task<WorkOrderDto> ChangeAsync(int id, string expected,
        Func<WorkOrderState, Task> action)
    {
        await using (var tx = await uow.BeginAsync())
        {
            var state = await orders.LockAsync(id)
                        ?? throw new NotFoundException(
                            "Наряд-допуск не найден");
            if (state.MasterId != currentUser.Id)
                throw new ForbiddenException("Наряд выдан другому мастеру");
            if (state.Status != expected)
                throw new ConflictException(
                    $"Действие недоступно в статусе заявки «{state.Status}»");
            await action(state);
            await tx.CommitAsync();
        }
        return (await orders.GetAsync(id))!;
    }
}
