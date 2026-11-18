using Microsoft.Extensions.DependencyInjection;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Rules;
using UkManagement.Core.Services;

namespace UkManagement.Core;

/// <summary>Регистрация сервисов бизнес-логики в контейнере внедрения
/// зависимостей.</summary>
public static class CoreModule
{
    public static IServiceCollection AddUkCore(this IServiceCollection s)
    {
        s.AddScoped<IAuthService, AuthService>();

        // один объект RequestService реализует два узких интерфейса
        s.AddScoped<RequestService>();
        s.AddScoped<IRequestCommandService>(
            p => p.GetRequiredService<RequestService>());
        s.AddScoped<IRequestQueryService>(
            p => p.GetRequiredService<RequestService>());

        s.AddScoped<WorkOrderService>();
        s.AddScoped<IWorkOrderAssignment>(
            p => p.GetRequiredService<WorkOrderService>());
        s.AddScoped<IWorkOrderExecution>(
            p => p.GetRequiredService<WorkOrderService>());
        s.AddScoped<MaterialWriteOff>();
        s.AddScoped<IWorkOrderClosing, ClosingService>();

        // правила назначения проверяются в порядке регистрации
        s.AddSingleton<IAssignmentRule, ActiveMasterRule>();
        s.AddSingleton<IAssignmentRule, MasterLoadRule>();
        s.AddSingleton<IAssignmentRule, PlanWithinDeadlineRule>();
        s.AddSingleton<IAssignmentRule, SafetyMeasuresRule>();

        s.AddScoped<IMaterialService, MaterialService>();
        s.AddScoped<IReportService, ReportService>();
        s.AddScoped<IEmployeeService, EmployeeService>();
        s.AddScoped<IReferenceService, ReferenceService>();
        s.AddScoped<INotifier, Notifier>();
        return s;
    }
}
