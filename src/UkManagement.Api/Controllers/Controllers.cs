using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;
using UkManagement.Core.Services;

namespace UkManagement.Api.Controllers;

/// <summary>Вход в систему и регистрация личного кабинета жильца.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>Аутентификация сотрудника или жильца, выдача токена JWT.
    /// </summary>
    [HttpPost("login")]
    public async Task<TokenResponse> Login(LoginRequest request)
        => await auth.LoginAsync(request);

    /// <summary>Регистрация личного кабинета жильца по лицевому счёту.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register(
        RegisterResidentRequest request)
    {
        var user = await auth.RegisterResidentAsync(request);
        return CreatedAtAction(nameof(Me), null, user);
    }

    /// <summary>Сведения о текущем пользователе.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<UserDto> Me() => await auth.MeAsync();
}

/// <summary>Заявки жильцов: регистрация, поиск, история, оценка.</summary>
[ApiController]
[Authorize]
[Route("api/requests")]
public class RequestsController(
    IRequestCommandService commands,
    IRequestQueryService queries) : ControllerBase
{
    /// <summary>Регистрация заявки диспетчером или жильцом.</summary>
    [Authorize(Roles = $"{Roles.Dispatcher},{Roles.Resident}")]
    [HttpPost]
    public async Task<ActionResult<RequestDto>> Create(CreateRequestDto dto)
    {
        var created = await commands.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Поиск заявок; жилец получает только свои заявки.</summary>
    [Authorize(Roles = $"{Roles.Dispatcher},{Roles.Engineer},{Roles.Resident}")]
    [HttpGet]
    public async Task<IReadOnlyList<RequestDto>> Search(
        [FromQuery] RequestFilter filter) => await queries.SearchAsync(filter);

    /// <summary>Карточка заявки.</summary>
    [Authorize(Roles = $"{Roles.Dispatcher},{Roles.Engineer},{Roles.Resident}")]
    [HttpGet("{id:int}")]
    public async Task<RequestDto> Get(int id) => await queries.GetAsync(id);

    /// <summary>История изменения статусов заявки.</summary>
    [Authorize(Roles = $"{Roles.Dispatcher},{Roles.Engineer},{Roles.Resident}")]
    [HttpGet("{id:int}/history")]
    public async Task<IReadOnlyList<HistoryItemDto>> History(int id)
        => await queries.HistoryAsync(id);

    /// <summary>Оценка выполненных работ жильцом.</summary>
    [Authorize(Roles = Roles.Resident)]
    [HttpPost("{id:int}/rating")]
    public async Task<ActionResult<RatingDto>> Rate(int id, RateDto dto)
        => StatusCode(201, await commands.RateAsync(id, dto));
}

/// <summary>Наряды-допуски: назначение, выполнение, расход ТМЦ, закрытие.
/// </summary>
[ApiController]
[Authorize]
[Route("api/work-orders")]
public class WorkOrdersController(
    IWorkOrderAssignment assignment,
    IWorkOrderExecution execution,
    IWorkOrderClosing closing) : ControllerBase
{
    /// <summary>Назначение исполнителя и оформление наряда-допуска.</summary>
    [Authorize(Roles = Roles.Dispatcher)]
    [HttpPost]
    public async Task<ActionResult<WorkOrderDto>> Assign(AssignDto dto)
    {
        var order = await assignment.AssignAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Активные наряды текущего мастера.</summary>
    [Authorize(Roles = Roles.Master)]
    [HttpGet("my")]
    public async Task<IReadOnlyList<WorkOrderDto>> My()
        => await execution.MyOrdersAsync();

    /// <summary>Карточка наряда-допуска с расходом ТМЦ.</summary>
    [Authorize(Roles = $"{Roles.Master},{Roles.Dispatcher},{Roles.Engineer}")]
    [HttpGet("{id:int}")]
    public async Task<WorkOrderDto> Get(int id) => await execution.GetAsync(id);

    /// <summary>Начало работ мастером (меры безопасности выполнены).</summary>
    [Authorize(Roles = Roles.Master)]
    [HttpPost("{id:int}/start")]
    public async Task<WorkOrderDto> Start(int id)
        => await execution.StartAsync(id);

    /// <summary>Отметка о выполнении работ.</summary>
    [Authorize(Roles = Roles.Master)]
    [HttpPost("{id:int}/finish")]
    public async Task<WorkOrderDto> Finish(int id)
        => await execution.FinishAsync(id);

    /// <summary>Добавление израсходованных ТМЦ.</summary>
    [Authorize(Roles = Roles.Master)]
    [HttpPost("{id:int}/materials")]
    public async Task<WorkOrderDto> AddMaterial(int id, AddUsageDto dto)
        => await execution.AddUsageAsync(id, dto);

    /// <summary>Удаление позиции расхода до рассмотрения инженером.</summary>
    [Authorize(Roles = Roles.Master)]
    [HttpDelete("{id:int}/materials/{usageId:int}")]
    public async Task<WorkOrderDto> RemoveMaterial(int id, int usageId)
        => await execution.RemoveUsageAsync(id, usageId);

    /// <summary>Закрытие наряда главным инженером со списанием ТМЦ.</summary>
    [Authorize(Roles = Roles.Engineer)]
    [HttpPost("{id:int}/close")]
    public async Task<CloseResultDto> Close(int id, CloseDto dto)
        => await closing.CloseAsync(id, dto);
}

/// <summary>Справочник ТМЦ склада (синхронизируется с 1С: Бухгалтерия).
/// </summary>
[ApiController]
[Authorize(Roles = $"{Roles.Accountant},{Roles.Engineer},{Roles.Master}")]
[Route("api/materials")]
public class MaterialsController(IMaterialService materials) : ControllerBase
{
    /// <summary>Список ТМЦ с поиском и отбором позиций ниже уровня дозаказа.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<MaterialDto>> List(string? search,
        bool belowReorder = false)
        => await materials.ListAsync(search, belowReorder);

    /// <summary>Карточка ТМЦ.</summary>
    [HttpGet("{id:int}")]
    public async Task<MaterialDto> Get(int id) => await materials.GetAsync(id);

    /// <summary>Добавление ТМЦ из 1С.</summary>
    [Authorize(Roles = Roles.Accountant)]
    [HttpPost]
    public async Task<ActionResult<MaterialDto>> Create(MaterialUpsertDto dto)
    {
        var created = await materials.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Обновление остатка и цены по данным 1С.</summary>
    [Authorize(Roles = Roles.Accountant)]
    [HttpPut("{id:int}")]
    public async Task<MaterialDto> Update(int id, MaterialUpsertDto dto)
        => await materials.UpdateAsync(id, dto);
}

/// <summary>Отчёт о соблюдении нормативных сроков и затратах ТМЦ.</summary>
[ApiController]
[Authorize(Roles = $"{Roles.Engineer},{Roles.Accountant}")]
[Route("api/reports")]
public class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Показатели за период (даты включительно).</summary>
    [HttpGet("sla")]
    public async Task<SlaReportDto> Sla(DateOnly from, DateOnly to)
        => await reports.SlaAsync(from, to);

    /// <summary>Выгрузка отчёта в файл CSV или JSON.</summary>
    [HttpGet("sla/export")]
    public async Task<FileContentResult> Export(DateOnly from, DateOnly to,
        string? format)
    {
        var file = await reports.ExportAsync(from, to, format);
        return File(file.Content, file.ContentType, file.FileName);
    }
}

/// <summary>Учётные записи сотрудников (главный инженер).</summary>
[ApiController]
[Route("api/employees")]
public class EmployeesController(IEmployeeService employees) : ControllerBase
{
    /// <summary>Список сотрудников с отбором по роли.</summary>
    [Authorize(Roles = $"{Roles.Engineer},{Roles.Dispatcher}")]
    [HttpGet]
    public async Task<IReadOnlyList<EmployeeDto>> List(string? role)
        => await employees.ListAsync(role);

    /// <summary>Карточка сотрудника.</summary>
    [Authorize(Roles = $"{Roles.Engineer},{Roles.Dispatcher}")]
    [HttpGet("{id:int}")]
    public async Task<EmployeeDto> Get(int id) => await employees.GetAsync(id);

    /// <summary>Создание учётной записи сотрудника.</summary>
    [Authorize(Roles = Roles.Engineer)]
    [HttpPost]
    public async Task<ActionResult<EmployeeDto>> Create(CreateEmployeeDto dto)
    {
        var created = await employees.CreateAsync(dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Блокировка или разблокировка учётной записи.</summary>
    [Authorize(Roles = Roles.Engineer)]
    [HttpPatch("{id:int}/active")]
    public async Task<EmployeeDto> SetActive(int id, SetActiveDto dto)
        => await employees.SetActiveAsync(id, dto.IsActive);
}

/// <summary>Справочники: дома, помещения, виды работ и нормативы.</summary>
[ApiController]
[Authorize(Roles = $"{Roles.Dispatcher},{Roles.Engineer},{Roles.Master},"
                   + $"{Roles.Accountant}")]
[Route("api")]
public class ReferenceController(IReferenceService reference) : ControllerBase
{
    /// <summary>Дома в управлении компании.</summary>
    [HttpGet("houses")]
    public async Task<IReadOnlyList<HouseDto>> Houses()
        => await reference.HousesAsync();

    /// <summary>Помещения дома.</summary>
    [HttpGet("houses/{id:int}/premises")]
    public async Task<IReadOnlyList<PremiseDto>> Premises(int id)
        => await reference.PremisesAsync(id);

    /// <summary>Виды работ с нормативными сроками, ч.</summary>
    [AllowAnonymous]
    [HttpGet("categories")]
    public async Task<IReadOnlyList<CategoryDto>> Categories()
        => await reference.CategoriesAsync();
}
