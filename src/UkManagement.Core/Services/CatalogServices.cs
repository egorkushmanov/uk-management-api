using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Core.Services;

// ---------- склад ТМЦ ----------

public interface IMaterialService
{
    Task<IReadOnlyList<MaterialDto>> ListAsync(string? search,
        bool belowReorder);
    Task<MaterialDto> GetAsync(int id);
    Task<MaterialDto> CreateAsync(MaterialUpsertDto dto);
    Task<MaterialDto> UpdateAsync(int id, MaterialUpsertDto dto);
}

public class MaterialService(IMaterialRepository materials, IUnitOfWork uow)
    : IMaterialService
{
    public Task<IReadOnlyList<MaterialDto>> ListAsync(string? search,
        bool belowReorder) => materials.ListAsync(search, belowReorder);

    public async Task<MaterialDto> GetAsync(int id)
        => await materials.GetAsync(id)
           ?? throw new NotFoundException("ТМЦ не найдены");

    public async Task<MaterialDto> CreateAsync(MaterialUpsertDto dto)
    {
        int id;
        await using (var tx = await uow.BeginAsync())
        {
            await EnsureUniqueAsync(dto.ErpCode, null);
            id = await materials.InsertAsync(dto);
            await tx.CommitAsync();
        }
        return await GetAsync(id);
    }

    public async Task<MaterialDto> UpdateAsync(int id, MaterialUpsertDto dto)
    {
        await using (var tx = await uow.BeginAsync())
        {
            await EnsureUniqueAsync(dto.ErpCode, id);
            if (!await materials.UpdateAsync(id, dto))
                throw new NotFoundException("ТМЦ не найдены");
            await tx.CommitAsync();
        }
        return await GetAsync(id);
    }

    private async Task EnsureUniqueAsync(string erpCode, int? exceptId)
    {
        if (await materials.ErpCodeExistsAsync(erpCode, exceptId))
            throw new ConflictException($"Код 1С {erpCode} уже используется");
    }
}

// ---------- отчёт о соблюдении нормативных сроков ----------

public interface IReportService
{
    Task<SlaReportDto> SlaAsync(DateOnly from, DateOnly to);
    Task<ReportFile> ExportAsync(DateOnly from, DateOnly to, string? format);
}

public class ReportService(
    IReportRepository reports,
    IEnumerable<IReportExporter> exporters) : IReportService
{
    /// <summary>Наибольшая длина периода отчёта, дней.</summary>
    public const int MaxDays = 366;

    private readonly Dictionary<string, IReportExporter> _exporters =
        exporters.ToDictionary(e => e.Format);

    public async Task<SlaReportDto> SlaAsync(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ValidationException(
                "Дата окончания периода раньше даты начала");
        if (to.DayNumber - from.DayNumber >= MaxDays)
            throw new ValidationException("Период отчёта не больше года");
        var rows = await reports.SlaRowsAsync(from, to);
        var total = rows.Sum(r => r.Total);
        var closed = rows.Sum(r => r.Closed);
        var onTime = rows.Sum(r => r.OnTime);
        return new SlaReportDto(from, to, total, closed, onTime,
            rows.Sum(r => r.Overdue),
            closed == 0 ? 0 : Math.Round(100m * onTime / closed, 1),
            rows.Sum(r => r.MaterialsCost), rows);
    }

    public async Task<ReportFile> ExportAsync(DateOnly from, DateOnly to,
        string? format)
    {
        var key = (format ?? "csv").ToLowerInvariant();
        if (!_exporters.TryGetValue(key, out var exporter))
            throw new ValidationException(
                $"Формат отчёта не поддерживается: {format}");
        var report = await SlaAsync(from, to);
        if (report.Total == 0)
            throw new NotFoundException("Нет заявок за выбранный период");
        return new ReportFile($"sla_{from:yyyyMMdd}_{to:yyyyMMdd}.{key}",
            exporter.ContentType, exporter.Export(report));
    }
}

// ---------- сотрудники ----------

public interface IEmployeeService
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(string? role);
    Task<EmployeeDto> GetAsync(int id);
    Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto);
    Task<EmployeeDto> SetActiveAsync(int id, bool isActive);
}

public class EmployeeService(
    IEmployeeRepository employees,
    IAccountRepository accounts,
    IPasswordHasher hasher,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IEmployeeService
{
    public Task<IReadOnlyList<EmployeeDto>> ListAsync(string? role)
    {
        if (role is not null && !Roles.Staff.Contains(role))
            throw new ValidationException($"Неизвестная роль: {role}");
        return employees.ListAsync(role);
    }

    public async Task<EmployeeDto> GetAsync(int id)
        => await employees.GetAsync(id)
           ?? throw new NotFoundException("Сотрудник не найден");

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto)
    {
        int id;
        await using (var tx = await uow.BeginAsync())
        {
            if (await accounts.LoginExistsAsync(dto.Login))
                throw new ConflictException("Логин уже занят");
            id = await employees.InsertAsync(dto, hasher.Hash(dto.Password));
            await tx.CommitAsync();
        }
        return await GetAsync(id);
    }

    public async Task<EmployeeDto> SetActiveAsync(int id, bool isActive)
    {
        await using (var tx = await uow.BeginAsync())
        {
            var employee = await GetAsync(id);
            if (id == currentUser.Id)
                throw new ForbiddenException(
                    "Нельзя заблокировать собственную учётную запись");
            if (!isActive && employee.ActiveOrders > 0)
                throw new ConflictException(
                    $"У сотрудника {employee.ActiveOrders} активных нарядов");
            await employees.SetActiveAsync(id, isActive);
            await tx.CommitAsync();
        }
        return await GetAsync(id);
    }
}

// ---------- справочники ----------

public interface IReferenceService
{
    Task<IReadOnlyList<HouseDto>> HousesAsync();
    Task<IReadOnlyList<PremiseDto>> PremisesAsync(int houseId);
    Task<IReadOnlyList<CategoryDto>> CategoriesAsync();
}

public class ReferenceService(IReferenceRepository reference)
    : IReferenceService
{
    public Task<IReadOnlyList<HouseDto>> HousesAsync()
        => reference.HousesAsync();

    public async Task<IReadOnlyList<PremiseDto>> PremisesAsync(int houseId)
    {
        if (!await reference.HouseExistsAsync(houseId))
            throw new NotFoundException("Дом не найден");
        return await reference.PremisesAsync(houseId);
    }

    public Task<IReadOnlyList<CategoryDto>> CategoriesAsync()
        => reference.CategoriesAsync();
}
