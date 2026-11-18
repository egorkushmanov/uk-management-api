using UkManagement.Core.Contracts;

namespace UkManagement.Core.Abstractions;

/// <summary>Учётные данные для проверки пароля.</summary>
public record Account(int Id, string Login, string Name, string Role,
    string PasswordHash, bool IsActive);

public record NewRequest(int PremiseId, int? ResidentId, short CategoryId,
    string Priority, string Description, string Source, int? RegisteredBy);

/// <summary>Состояние заявки, заблокированной для изменения.</summary>
public record RequestState(int Id, int PremiseId, int? ResidentId,
    string Status, string Category, DateTimeOffset DeadlineAt,
    string Address);

public record MasterInfo(int Id, string Role, bool IsActive,
    int ActiveOrders);

public record NewWorkOrder(int RequestId, int MasterId, string WorkType,
    string? Safety, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd);

/// <summary>Состояние наряда, заблокированного для изменения.</summary>
public record WorkOrderState(int Id, int RequestId, int MasterId,
    string Status);

public record PendingUsage(int Id, int MaterialId, decimal Quantity,
    decimal UnitPrice);

public record StockItem(int Id, string Name, decimal StockQty,
    decimal Price);

public interface IAccountRepository
{
    Task<Account?> FindEmployeeAsync(string login);
    Task<Account?> FindResidentAccountAsync(string login);
    Task<bool> LoginExistsAsync(string login);
    Task<int?> FindResidentAsync(string accountNumber, string phone);
    Task<bool> HasAccountAsync(int residentId);
    Task CreateResidentAccountAsync(int residentId, string login,
        string passwordHash);
    Task<UserDto?> GetResidentAsync(int residentId);
    Task<UserDto?> GetEmployeeAsync(int employeeId);
}

public interface IRequestRepository
{
    Task<bool> PremiseExistsAsync(int premiseId);
    Task<bool> CategoryExistsAsync(short categoryId);
    Task<int?> FindPremiseOfResidentAsync(int residentId);
    Task<int?> FindResidentByPhoneAsync(string phone);
    Task<int?> FindOpenDuplicateAsync(int premiseId, short categoryId);
    Task<int> InsertAsync(NewRequest request);
    Task<RequestState?> LockAsync(int id);
    Task SetStatusAsync(int id, string status);
    Task<RequestDto?> GetAsync(int id);
    Task<int?> GetOwnerAsync(int id);
    Task<IReadOnlyList<RequestDto>> SearchAsync(RequestFilter filter,
        int? residentId);
    Task<IReadOnlyList<HistoryItemDto>> HistoryAsync(int id);
    Task<bool> RatingExistsAsync(int requestId);
    Task<RatingDto> InsertRatingAsync(int requestId, short score,
        string? comment);
}

public interface IWorkOrderRepository
{
    Task<MasterInfo?> GetMasterAsync(int employeeId);
    Task<int> InsertAsync(NewWorkOrder order);
    Task<WorkOrderState?> LockAsync(int id);
    Task SetStartedAsync(int id);
    Task SetFinishedAsync(int id);
    Task<WorkOrderDto?> GetAsync(int id);
    Task<IReadOnlyList<WorkOrderDto>> ListByMasterAsync(int masterId);
    Task<bool> UsageExistsAsync(int workOrderId, int materialId);
    Task<int> AddUsageAsync(int workOrderId, int materialId,
        decimal quantity, decimal unitPrice);
    Task<string?> GetUsageApprovalAsync(int workOrderId, int usageId);
    Task DeleteUsageAsync(int usageId);
    Task<IReadOnlyList<PendingUsage>> PendingUsagesAsync(int workOrderId);
    Task SetApprovalAsync(int usageId, string approval, int engineerId);
}

public interface IMaterialRepository
{
    Task<IReadOnlyList<MaterialDto>> ListAsync(string? search,
        bool belowReorderOnly);
    Task<MaterialDto?> GetAsync(int id);
    Task<StockItem?> LockAsync(int id);
    Task<bool> ErpCodeExistsAsync(string erpCode, int? exceptId);
    Task<int> InsertAsync(MaterialUpsertDto material);
    Task<bool> UpdateAsync(int id, MaterialUpsertDto material);
}

public interface IEmployeeRepository
{
    Task<IReadOnlyList<EmployeeDto>> ListAsync(string? role);
    Task<EmployeeDto?> GetAsync(int id);
    Task<int> InsertAsync(CreateEmployeeDto employee, string passwordHash);
    Task SetActiveAsync(int id, bool isActive);
}

public interface IReferenceRepository
{
    Task<IReadOnlyList<HouseDto>> HousesAsync();
    Task<bool> HouseExistsAsync(int id);
    Task<IReadOnlyList<PremiseDto>> PremisesAsync(int houseId);
    Task<IReadOnlyList<CategoryDto>> CategoriesAsync();
}

public interface IReportRepository
{
    Task<IReadOnlyList<SlaRow>> SlaRowsAsync(DateOnly from, DateOnly to);
}
