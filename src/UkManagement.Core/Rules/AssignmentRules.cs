using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Core.Rules;

/// <summary>Данные, по которым проверяется назначение исполнителя.</summary>
public record AssignmentContext(AssignDto Order, RequestState Request,
    MasterInfo Master);

/// <summary>Правило назначения исполнителя. Контракт (LSP): правило не
/// изменяет данные и при нарушении выбрасывает ApiException.</summary>
public interface IAssignmentRule
{
    void Check(AssignmentContext context);
}

/// <summary>Исполнителем может быть только действующий мастер.</summary>
public sealed class ActiveMasterRule : IAssignmentRule
{
    public void Check(AssignmentContext c)
    {
        if (c.Master.Role != Roles.Master || !c.Master.IsActive)
            throw new ValidationException(
                "Сотрудник не является действующим мастером");
    }
}

/// <summary>У мастера не более трёх активных нарядов.</summary>
public sealed class MasterLoadRule : IAssignmentRule
{
    public void Check(AssignmentContext c)
    {
        if (c.Master.ActiveOrders >= BusinessRules.MaxActiveOrders)
            throw new ConflictException(
                $"У мастера {c.Master.ActiveOrders} активных нарядов");
    }
}

/// <summary>Плановые сроки: окончание позже начала и не позже
/// нормативного срока заявки.</summary>
public sealed class PlanWithinDeadlineRule : IAssignmentRule
{
    public void Check(AssignmentContext c)
    {
        if (c.Order.PlannedEnd <= c.Order.PlannedStart)
            throw new ValidationException(
                "Плановое окончание должно быть позже начала");
        if (c.Order.PlannedEnd > c.Request.DeadlineAt)
            throw new ValidationException(
                "Плановое окончание позже нормативного срока заявки");
    }
}

/// <summary>Для электромонтажных работ обязательны меры безопасности.</summary>
public sealed class SafetyMeasuresRule : IAssignmentRule
{
    public void Check(AssignmentContext c)
    {
        if (c.Request.Category == BusinessRules.ElectricCategory
            && string.IsNullOrWhiteSpace(c.Order.Safety))
            throw new ValidationException(
                "Для электромонтажных работ нужны меры безопасности");
    }
}
