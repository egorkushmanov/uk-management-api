using System.ComponentModel.DataAnnotations;

namespace UkManagement.Core.Contracts;

// ---------- аутентификация и личный кабинет ----------

public record LoginRequest(
    [Required(ErrorMessage = "Логин не может быть пустым")]
    string Login,
    [Required(ErrorMessage = "Пароль не может быть пустым")]
    string Password);

public record TokenResponse(string Token, DateTimeOffset ExpiresAt,
    string Role, string Name);

public record RegisterResidentRequest(
    [Required(ErrorMessage = "Укажите лицевой счёт")]
    [RegularExpression(@"^\d{4}-\d{4}$",
        ErrorMessage = "Лицевой счёт имеет формат 0000-0000")]
    string AccountNumber,
    [Required(ErrorMessage = "Укажите номер телефона")]
    [RegularExpression(@"^\+375\d{9}$",
        ErrorMessage = "Телефон имеет формат +375XXXXXXXXX")]
    string Phone,
    [Required(ErrorMessage = "Логин не может быть пустым")]
    [RegularExpression(@"^[a-z0-9._]{3,30}$",
        ErrorMessage = "Логин: 3–30 строчных латинских букв, цифр, . или _")]
    string Login,
    [Required(ErrorMessage = "Пароль не может быть пустым")]
    [MinLength(8,
        ErrorMessage = "Пароль должен содержать не менее 8 символов")]
    string Password);

public record UserDto(int Id, string Login, string Name, string Role,
    string? Address);

// ---------- заявки ----------

public record CreateRequestDto(
    int? PremiseId,
    string? Phone,
    [Range(1, short.MaxValue, ErrorMessage = "Укажите вид работ")]
    short CategoryId,
    [Required(ErrorMessage = "Укажите приоритет")]
    [RegularExpression("^(emergency|urgent|planned)$",
        ErrorMessage = "Приоритет: emergency, urgent или planned")]
    string Priority,
    [Required(ErrorMessage = "Опишите проблему")]
    string Description);

public record RequestDto(int Id, string Address, string Category,
    string Priority, string Status, string Description, string Source,
    string? Resident, string? Master, DateTimeOffset CreatedAt,
    DateTimeOffset DeadlineAt, DateTimeOffset? ClosedAt, bool Overdue);

public record RequestFilter(string? Status, string? Priority,
    int? HouseId, bool? Overdue, string? Search);

public record HistoryItemDto(string Status, DateTimeOffset ChangedAt,
    string? Employee);

public record RateDto(
    [Range(1, 5, ErrorMessage = "Оценка — целое число от 1 до 5")]
    short Score,
    [MaxLength(500,
        ErrorMessage = "Комментарий не длиннее 500 символов")]
    string? Comment);

public record RatingDto(int RequestId, short Score, string? Comment,
    DateTimeOffset CreatedAt);

// ---------- наряды-допуски ----------

public record AssignDto(
    [Range(1, int.MaxValue, ErrorMessage = "Укажите заявку")]
    int RequestId,
    [Range(1, int.MaxValue, ErrorMessage = "Укажите мастера")]
    int MasterId,
    [Required(ErrorMessage = "Укажите вид работ")]
    string WorkType,
    string? Safety,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd);

public record UsageDto(int Id, int MaterialId, string Name, string Unit,
    decimal Quantity, decimal UnitPrice, string Approval);

public record WorkOrderDto(int Id, int RequestId, string Address,
    string Description, string Status, int MasterId, string Master,
    string WorkType,
    string? Safety, DateTimeOffset? PlannedStart,
    DateTimeOffset? PlannedEnd, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, IReadOnlyList<UsageDto> Materials);

public record AddUsageDto(
    [Range(1, int.MaxValue, ErrorMessage = "Укажите ТМЦ")]
    int MaterialId,
    [Range(typeof(decimal), "0.001", "100000",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Количество должно быть больше нуля")]
    decimal Quantity);

public record CloseDto(IReadOnlyList<int>? Rejected);

public record CloseResultDto(int WorkOrderId, int RequestId,
    decimal MaterialsCost, IReadOnlyList<string> Reorder);

// ---------- склад ТМЦ ----------

public record MaterialDto(int Id, string ErpCode, string Name, string Unit,
    decimal StockQty, decimal Price, DateTimeOffset? SyncedAt,
    bool BelowReorder);

public record MaterialUpsertDto(
    [Required(ErrorMessage = "Укажите код 1С")]
    [RegularExpression(@"^\d{2}-\d{8}$",
        ErrorMessage = "Код 1С имеет формат 00-00000000")]
    string ErpCode,
    [Required(ErrorMessage = "Укажите наименование")]
    string Name,
    [Required(ErrorMessage = "Укажите единицу измерения")]
    string Unit,
    [Range(typeof(decimal), "0", "1000000",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Остаток не может быть отрицательным")]
    decimal StockQty,
    [Range(typeof(decimal), "0", "1000000",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Цена не может быть отрицательной")]
    decimal Price);

// ---------- отчёты ----------

public record SlaRow(string Category, int Total, int Closed, int OnTime,
    int Overdue, decimal MaterialsCost);

public record SlaReportDto(DateOnly From, DateOnly To, int Total,
    int Closed, int OnTime, int Overdue, decimal OnTimePercent,
    decimal MaterialsCost, IReadOnlyList<SlaRow> ByCategory);

public record ReportFile(string FileName, string ContentType,
    byte[] Content);

// ---------- сотрудники и справочники ----------

public record EmployeeDto(int Id, string FullName, string Login,
    string Role, string? Phone, string? Specialization, bool IsActive,
    int ActiveOrders);

public record CreateEmployeeDto(
    [Required(ErrorMessage = "Укажите ФИО")]
    string FullName,
    [Required(ErrorMessage = "Логин не может быть пустым")]
    [RegularExpression(@"^[a-z0-9._]{3,30}$",
        ErrorMessage = "Логин: 3–30 строчных латинских букв, цифр, . или _")]
    string Login,
    [Required(ErrorMessage = "Пароль не может быть пустым")]
    [MinLength(8,
        ErrorMessage = "Пароль должен содержать не менее 8 символов")]
    string Password,
    [Required(ErrorMessage = "Укажите роль")]
    [RegularExpression("^(dispatcher|master|engineer|accountant)$",
        ErrorMessage = "Роль: dispatcher, master, engineer или accountant")]
    string Role,
    string? Phone,
    string? Specialization);

public record SetActiveDto(bool IsActive);

public record HouseDto(int Id, string Address, short? Floors,
    short? Entrances, int Premises);

public record PremiseDto(int Id, string Kind, string Number,
    short? Entrance, short? Floor, string? AccountNumber);

public record CategoryDto(short Id, string Name, short EmergencyHours,
    short UrgentHours, short PlannedHours);
