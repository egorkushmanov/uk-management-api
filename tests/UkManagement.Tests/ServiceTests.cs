using Moq;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;
using UkManagement.Core.Rules;
using UkManagement.Core.Services;
using UkManagement.Infrastructure;
using Xunit;

namespace UkManagement.Tests;

/// <summary>Общие заглушки: единица работы без базы данных и пользователь.
/// </summary>
internal static class Fakes
{
    public static Mock<IUnitOfWork> Uow(Action? onCommit = null)
    {
        var tx = new Mock<ITransaction>();
        tx.Setup(t => t.CommitAsync()).Callback(() => onCommit?.Invoke())
            .Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.BeginAsync()).ReturnsAsync(tx.Object);
        return uow;
    }

    public static ICurrentUser User(int id, string role)
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.Id).Returns(id);
        user.Setup(u => u.Role).Returns(role);
        user.Setup(u => u.IsAuthenticated).Returns(true);
        user.Setup(u => u.IsInRole(It.IsAny<string[]>()))
            .Returns((string[] roles) => roles.Contains(role));
        return user.Object;
    }

    public static readonly DateTimeOffset Now =
        new(2026, 11, 18, 11, 0, 0, TimeSpan.FromHours(3));

    public static RequestDto Request(int id, string priority = "urgent") =>
        new(id, "ул. Кальварийская, 21, кв. 64", "Сантехника", priority,
            Statuses.New, "Течёт смеситель на кухне", "phone", null, null,
            Now, Now.AddHours(24), null, false);
}

public class RequestServiceTests
{
    private readonly Mock<IRequestRepository> _repo = new();
    private readonly Mock<INotifier> _notifier = new();

    private RequestService Service(string role, int userId = 1) => new(
        _repo.Object, Fakes.Uow().Object, Fakes.User(userId, role),
        _notifier.Object);

    [Fact]
    public async Task ShortDescription_IsRejectedWithoutDatabase()
    {
        var dto = new CreateRequestDto(9, null, 5, "urgent", "Нет воды");
        var e = await Assert.ThrowsAsync<ValidationException>(
            () => Service(Roles.Dispatcher).CreateAsync(dto));
        Assert.Equal(400, e.Status);
        _repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Duplicate_IsConflict()
    {
        _repo.Setup(r => r.PremiseExistsAsync(8)).ReturnsAsync(true);
        _repo.Setup(r => r.CategoryExistsAsync(2)).ReturnsAsync(true);
        _repo.Setup(r => r.FindOpenDuplicateAsync(8, 2)).ReturnsAsync(1045);
        var dto = new CreateRequestDto(8, null, 2, "urgent",
            "Холодные радиаторы в спальне");
        var e = await Assert.ThrowsAsync<ConflictException>(
            () => Service(Roles.Dispatcher).CreateAsync(dto));
        Assert.Contains("1045", e.Message);
        _repo.Verify(r => r.InsertAsync(It.IsAny<NewRequest>()), Times.Never);
    }

    [Fact]
    public async Task Resident_CreatesRequestForOwnPremise()
    {
        NewRequest? saved = null;
        _repo.Setup(r => r.FindPremiseOfResidentAsync(1)).ReturnsAsync(1);
        _repo.Setup(r => r.CategoryExistsAsync(5)).ReturnsAsync(true);
        _repo.Setup(r => r.InsertAsync(It.IsAny<NewRequest>()))
            .Callback<NewRequest>(n => saved = n).ReturnsAsync(1051);
        _repo.Setup(r => r.GetAsync(1051)).ReturnsAsync(Fakes.Request(1051));
        await Service(Roles.Resident).CreateAsync(new CreateRequestDto(
            99, null, 5, "urgent", "Подтекает сифон под раковиной"));
        Assert.Equal(1, saved!.PremiseId);   // помещение из запроса игнорируется
        Assert.Equal(1, saved.ResidentId);
        Assert.Equal("mobile", saved.Source);
    }

    [Fact]
    public async Task Emergency_NotifiesEngineer()
    {
        _repo.Setup(r => r.PremiseExistsAsync(7)).ReturnsAsync(true);
        _repo.Setup(r => r.CategoryExistsAsync(3)).ReturnsAsync(true);
        _repo.Setup(r => r.InsertAsync(It.IsAny<NewRequest>()))
            .ReturnsAsync(1050);
        _repo.Setup(r => r.GetAsync(1050))
            .ReturnsAsync(Fakes.Request(1050, "emergency"));
        await Service(Roles.Dispatcher).CreateAsync(new CreateRequestDto(7,
            null, 3, "emergency", "Нет света в подъезде на всех этажах"));
        _notifier.Verify(n => n.EmergencyRegisteredAsync(1050,
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Resident_CannotSeeForeignRequest()
    {
        _repo.Setup(r => r.GetAsync(1045)).ReturnsAsync(Fakes.Request(1045));
        _repo.Setup(r => r.GetOwnerAsync(1045)).ReturnsAsync(5);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service(Roles.Resident, userId: 1).GetAsync(1045));
    }
}

/// <summary>Правила назначения взаимозаменяемы (LSP): каждое либо
/// пропускает корректное назначение, либо выбрасывает ApiException.</summary>
public class AssignmentRuleTests
{
    private static AssignmentContext Context(int load = 1,
        string category = "Сантехника", string? safety = null,
        int endHour = 13, string role = Roles.Master, bool active = true)
        => new(new AssignDto(1049, 2, "Замена смесителя", safety,
                Fakes.Now, Fakes.Now.Date.AddHours(endHour)),
            new RequestState(1049, 9, 6, Statuses.New, category,
                Fakes.Now.AddHours(24), "ул. Кальварийская, 21, кв. 64"),
            new MasterInfo(2, role, active, load));

    public static TheoryData<IAssignmentRule> AllRules => new()
    {
        new ActiveMasterRule(), new MasterLoadRule(),
        new PlanWithinDeadlineRule(), new SafetyMeasuresRule()
    };

    [Theory]
    [MemberData(nameof(AllRules))]
    public void EveryRule_AcceptsValidAssignment(IAssignmentRule rule)
        => rule.Check(Context());

    [Fact]
    public void LoadRule_RejectsFourthOrder()
    {
        var e = Assert.Throws<ConflictException>(
            () => new MasterLoadRule().Check(Context(load: 3)));
        Assert.Equal(409, e.Status);
    }

    [Fact]
    public void SafetyRule_RequiresMeasuresForElectricity()
        => Assert.Throws<ValidationException>(() => new SafetyMeasuresRule()
            .Check(Context(category: BusinessRules.ElectricCategory)));

    [Fact]
    public void ActiveMasterRule_RejectsDispatcher()
        => Assert.Throws<ValidationException>(() => new ActiveMasterRule()
            .Check(Context(role: Roles.Dispatcher)));

    [Fact]
    public void PlanRule_RejectsEndAfterDeadline()
        => Assert.Throws<ValidationException>(() =>
            new PlanWithinDeadlineRule().Check(Context(endHour: 36)));
}

public class MaterialWriteOffTests
{
    [Fact]
    public async Task Shortage_ThrowsAndDoesNotApproveFurther()
    {
        var orders = new Mock<IWorkOrderRepository>();
        var materials = new Mock<IMaterialRepository>();
        materials.Setup(m => m.LockAsync(3)).ReturnsAsync(
            new StockItem(3, "Мастика битумная, 20 кг", 1, 96));
        var writeOff = new MaterialWriteOff(orders.Object, materials.Object);
        var e = await Assert.ThrowsAsync<InsufficientStockException>(() =>
            writeOff.ApplyAsync(new[] { new PendingUsage(6, 3, 2, 96) },
                Array.Empty<int>(), 5));
        Assert.Contains("требуется 2, на складе 1", e.Message);
        orders.Verify(o => o.SetApprovalAsync(It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CostAndReorder_AreCalculated()
    {
        var orders = new Mock<IWorkOrderRepository>();
        var materials = new Mock<IMaterialRepository>();
        materials.Setup(m => m.LockAsync(5)).ReturnsAsync(
            new StockItem(5, "Лампа светодиодная 12 Вт E27", 60, 3.2m));
        var writeOff = new MaterialWriteOff(orders.Object, materials.Object);
        var result = await writeOff.ApplyAsync(new[]
        {
            new PendingUsage(7, 5, 57, 3.2m), new PendingUsage(8, 1, 1, 9.8m)
        }, new[] { 8 }, 5);
        Assert.Equal(182.4m, result.Cost);
        Assert.Equal(new[] { "Лампа светодиодная 12 Вт E27" }, result.Reorder);
        orders.Verify(o => o.SetApprovalAsync(8, Approvals.Rejected, 5));
        orders.Verify(o => o.SetApprovalAsync(7, Approvals.Approved, 5));
    }
}

/// <summary>Все форматы выгрузки соблюдают общий контракт (LSP).</summary>
public class ReportExporterTests
{
    private static readonly SlaReportDto Report = new(
        new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18), 2, 1, 1, 0,
        100, 9.8m, new[] { new SlaRow("Сантехника", 2, 1, 1, 0, 9.8m) });

    public static TheoryData<IReportExporter> All => new()
        { new CsvReportExporter(), new JsonReportExporter() };

    [Theory]
    [MemberData(nameof(All))]
    public void Exporter_ProducesNonEmptyUtf8File(IReportExporter exporter)
    {
        var bytes = exporter.Export(Report);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("Сантехника", text);
        Assert.Contains("charset=utf-8", exporter.ContentType);
        Assert.Matches("^[a-z]+$", exporter.Format);
    }

    [Fact]
    public async Task UnknownFormat_IsRejected()
    {
        var service = new ReportService(Mock.Of<IReportRepository>(),
            new IReportExporter[] { new CsvReportExporter() });
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ExportAsync(Report.From, Report.To, "pdf"));
    }
}

public class PasswordHasherTests
{
    [Fact]
    public void Hash_IsSaltedAndVerifiable()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var a = hasher.Hash("Uk2026!pass");
        var b = hasher.Hash("Uk2026!pass");
        Assert.NotEqual(a, b);
        Assert.True(hasher.Verify("Uk2026!pass", a));
        Assert.False(hasher.Verify("uk2026!pass", a));
    }

    [Fact]
    public void SeedHash_MatchesDemoPassword()
        => Assert.True(new Pbkdf2PasswordHasher().Verify("Uk2026!pass",
            "PBKDF2-SHA256$100000$VWtNYW5hZ2VtZW50MjAyNg==$"
            + "FZCH9fo1xhF/cfqcEz7DclrMWGQvDXHEiyeYUfq9DxY="));
}
