using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using UkManagement.Core.Abstractions;
using UkManagement.Infrastructure.Data;

namespace UkManagement.Infrastructure;

/// <summary>Регистрация реализаций абстракций ядра: база данных,
/// безопасность, уведомления, выгрузка отчётов.</summary>
public static class InfrastructureModule
{
    public static IServiceCollection AddUkInfrastructure(
        this IServiceCollection s, IConfiguration config)
    {
        s.AddSingleton(NpgsqlDataSource.Create(
            config.GetConnectionString("Uk")
            ?? throw new InvalidOperationException(
                "Не задана строка подключения ConnectionStrings:Uk")));
        s.AddScoped<DbSession>();
        s.AddScoped<IUnitOfWork>(p => p.GetRequiredService<DbSession>());
        s.AddScoped<IAccountRepository, AccountRepository>();
        s.AddScoped<IRequestRepository, RequestRepository>();
        s.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        s.AddScoped<IMaterialRepository, MaterialRepository>();
        s.AddScoped<IEmployeeRepository, EmployeeRepository>();
        s.AddScoped<IReferenceRepository, ReferenceRepository>();
        s.AddScoped<IReportRepository, ReportRepository>();

        var demoNow = config["App:DemoNow"];
        s.AddSingleton<IClock>(string.IsNullOrEmpty(demoNow)
            ? new SystemClock()
            : new DemoClock(DateTimeOffset.Parse(demoNow,
                CultureInfo.InvariantCulture),
                config.GetValue("App:DemoSpeed", 1.0)));

        var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        s.AddSingleton(jwt);
        s.AddSingleton<ITokenService, JwtTokenService>();
        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        s.AddSingleton<EventJournal>();
        s.AddSingleton<INotificationChannel, LogNotificationChannel>();
        s.AddSingleton<IErpGateway, JournalErpGateway>();
        s.AddSingleton<IReportExporter, CsvReportExporter>();
        s.AddSingleton<IReportExporter, JsonReportExporter>();
        return s;
    }
}
