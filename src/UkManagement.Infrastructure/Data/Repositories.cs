using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Infrastructure.Data;

/// <summary>Общие фрагменты SQL (DRY).</summary>
internal static class Sql
{
    /// <summary>Адрес помещения: «ул. Кальварийская, 21, кв. 64».</summary>
    public const string Place =
        "h.address || ', ' || CASE p.kind WHEN 'apartment' THEN 'кв. ' "
        + "WHEN 'entrance' THEN 'подъезд ' ELSE 'помещение ' END || p.number";

    /// <summary>Число активных нарядов мастера e.</summary>
    public const string ActiveOrders =
        "(SELECT count(*)::int FROM work_order w JOIN request r "
        + "ON r.id = w.request_id WHERE w.master_id = e.id "
        + "AND r.status IN ('assigned', 'in_progress'))";
}

public class AccountRepository(DbSession db) : IAccountRepository
{
    private static Account Map(Row r) => new(r.Int(0), r.Str(1), r.Str(2),
        r.Str(3), r.Str(4), r.Bool(5));

    public Task<Account?> FindEmployeeAsync(string login) => db.SingleAsync(
        "SELECT e.id, e.login, e.full_name, ro.code, e.password_hash, " +
        "e.is_active FROM employee e JOIN role ro ON ro.id = e.role_id " +
        "WHERE e.login = @l", Map, ("l", login));

    public Task<Account?> FindResidentAccountAsync(string login) =>
        db.SingleAsync(
            "SELECT a.resident_id, a.login, r.full_name, 'resident', " +
            "a.password_hash, true FROM resident_account a " +
            "JOIN resident r ON r.id = a.resident_id WHERE a.login = @l",
            Map, ("l", login));

    public async Task<bool> LoginExistsAsync(string login)
        => await db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM employee WHERE login = @l) " +
            "OR EXISTS (SELECT 1 FROM resident_account WHERE login = @l)",
            ("l", login));

    public Task<int?> FindResidentAsync(string accountNumber, string phone)
        => db.ScalarAsync<int?>(
            "SELECT r.id FROM resident r JOIN premise p " +
            "ON p.id = r.premise_id WHERE p.account_number = @a " +
            "AND r.phone = @p", ("a", accountNumber), ("p", phone));

    public Task<bool> HasAccountAsync(int residentId) => db.ScalarAsync<bool>(
        "SELECT EXISTS (SELECT 1 FROM resident_account " +
        "WHERE resident_id = @r)", ("r", residentId));

    public Task CreateResidentAccountAsync(int residentId, string login,
        string passwordHash) => db.ExecAsync(
        "INSERT INTO resident_account (resident_id, login, password_hash) " +
        "VALUES (@r, @l, @h)", ("r", residentId), ("l", login),
        ("h", passwordHash));

    public Task<UserDto?> GetResidentAsync(int residentId) => db.SingleAsync(
        $"SELECT r.id, a.login, r.full_name, 'resident', {Sql.Place} " +
        "FROM resident r JOIN resident_account a ON a.resident_id = r.id " +
        "JOIN premise p ON p.id = r.premise_id " +
        "JOIN house h ON h.id = p.house_id WHERE r.id = @r",
        r => new UserDto(r.Int(0), r.Str(1), r.Str(2), r.Str(3), r.Str(4)),
        ("r", residentId));

    public Task<UserDto?> GetEmployeeAsync(int employeeId) => db.SingleAsync(
        "SELECT e.id, e.login, e.full_name, ro.code FROM employee e " +
        "JOIN role ro ON ro.id = e.role_id WHERE e.id = @e",
        r => new UserDto(r.Int(0), r.Str(1), r.Str(2), r.Str(3), null),
        ("e", employeeId));
}

public class RequestRepository(DbSession db) : IRequestRepository
{
    private const string Select =
        $"SELECT r.id, {Sql.Place}, c.name, r.priority::text, " +
        "r.status::text, r.description, r.source, res.full_name, " +
        "e.full_name, r.created_at, r.deadline_at, r.closed_at, " +
        "r.status NOT IN ('done', 'closed') AND r.deadline_at < app_now() " +
        "FROM request r JOIN premise p ON p.id = r.premise_id " +
        "JOIN house h ON h.id = p.house_id " +
        "JOIN work_category c ON c.id = r.category_id " +
        "LEFT JOIN resident res ON res.id = r.resident_id " +
        "LEFT JOIN work_order w ON w.request_id = r.id " +
        "LEFT JOIN employee e ON e.id = w.master_id ";

    private static RequestDto Map(Row r) => new(r.Int(0), r.Str(1),
        r.Str(2), r.Str(3), r.Str(4), r.Str(5), r.Str(6), r.StrN(7),
        r.StrN(8), r.Time(9), r.Time(10), r.TimeN(11), r.Bool(12));

    public Task<bool> PremiseExistsAsync(int premiseId) =>
        db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM premise WHERE id = @p)",
            ("p", premiseId));

    public Task<bool> CategoryExistsAsync(short categoryId) =>
        db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM work_category WHERE id = @c)",
            ("c", categoryId));

    public Task<int?> FindPremiseOfResidentAsync(int residentId) =>
        db.ScalarAsync<int?>("SELECT premise_id FROM resident WHERE id = @r",
            ("r", residentId));

    public Task<int?> FindResidentByPhoneAsync(string phone) =>
        db.ScalarAsync<int?>("SELECT id FROM resident WHERE phone = @t",
            ("t", phone));

    public Task<int?> FindOpenDuplicateAsync(int premiseId, short categoryId)
        => db.ScalarAsync<int?>(
            "SELECT id FROM request WHERE premise_id = @p " +
            "AND category_id = @c AND status <> 'closed' LIMIT 1",
            ("p", premiseId), ("c", categoryId));

    public async Task<int> InsertAsync(NewRequest n) =>
        (await db.ScalarAsync<int>(
            "INSERT INTO request (premise_id, resident_id, category_id, " +
            "priority, description, source, registered_by) VALUES " +
            "(@p, @r, @c, @pr::request_priority, @d, @s, @e) RETURNING id",
            ("p", n.PremiseId), ("r", n.ResidentId), ("c", n.CategoryId),
            ("pr", n.Priority), ("d", n.Description), ("s", n.Source),
            ("e", n.RegisteredBy)));

    public Task<RequestState?> LockAsync(int id) => db.SingleAsync(
        $"SELECT r.id, r.premise_id, r.resident_id, r.status::text, " +
        $"c.name, r.deadline_at, {Sql.Place} FROM request r " +
        "JOIN work_category c ON c.id = r.category_id " +
        "JOIN premise p ON p.id = r.premise_id " +
        "JOIN house h ON h.id = p.house_id WHERE r.id = @id FOR UPDATE OF r",
        r => new RequestState(r.Int(0), r.Int(1), r.IntN(2), r.Str(3),
            r.Str(4), r.Time(5), r.Str(6)), ("id", id));

    public Task SetStatusAsync(int id, string status) => db.ExecAsync(
        "UPDATE request SET status = @s::request_status WHERE id = @id",
        ("s", status), ("id", id));

    public Task<RequestDto?> GetAsync(int id) =>
        db.SingleAsync(Select + "WHERE r.id = @id", Map, ("id", id));

    public Task<int?> GetOwnerAsync(int id) => db.ScalarAsync<int?>(
        "SELECT resident_id FROM request WHERE id = @id", ("id", id));

    public Task<IReadOnlyList<RequestDto>> SearchAsync(RequestFilter f,
        int? residentId)
    {
        var where = new List<string>();
        var args = new List<(string, object?)>();
        void Add(string condition, string name, object value)
        {
            where.Add(condition);
            args.Add((name, value));
        }
        if (f.Status is not null)
            Add("r.status = @st::request_status", "st", f.Status);
        if (f.Priority is not null)
            Add("r.priority = @pr::request_priority", "pr", f.Priority);
        if (f.HouseId is not null)
            Add("p.house_id = @h", "h", f.HouseId);
        if (residentId is not null)
            Add("r.resident_id = @res", "res", residentId);
        if (!string.IsNullOrWhiteSpace(f.Search))
            Add($"(r.description ILIKE @q OR {Sql.Place} ILIKE @q)", "q",
                $"%{f.Search.Trim()}%");
        if (f.Overdue is { } overdue)
            where.Add((overdue ? "" : "NOT ") +
                      "(r.status NOT IN ('done', 'closed') " +
                      "AND r.deadline_at < app_now())");
        var sql = Select + (where.Count > 0
            ? "WHERE " + string.Join(" AND ", where) : "") +
                  " ORDER BY r.created_at DESC";
        return db.QueryAsync(sql, Map, args.ToArray());
    }

    public Task<IReadOnlyList<HistoryItemDto>> HistoryAsync(int id) =>
        db.QueryAsync(
            "SELECT s.status::text, s.changed_at, e.full_name " +
            "FROM status_history s LEFT JOIN employee e " +
            "ON e.id = s.employee_id WHERE s.request_id = @id ORDER BY s.id",
            r => new HistoryItemDto(r.Str(0), r.Time(1), r.StrN(2)),
            ("id", id));

    public Task<bool> RatingExistsAsync(int requestId) =>
        db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM rating WHERE request_id = @r)",
            ("r", requestId));

    public async Task<RatingDto> InsertRatingAsync(int requestId,
        short score, string? comment) => (await db.SingleAsync(
        "INSERT INTO rating (request_id, score, comment) " +
        "VALUES (@r, @s, @c) RETURNING request_id, score, comment, " +
        "created_at",
        r => new RatingDto(r.Int(0), r.Short(1), r.StrN(2), r.Time(3)),
        ("r", requestId), ("s", score), ("c", comment)))!;
}

public class WorkOrderRepository(DbSession db) : IWorkOrderRepository
{
    private const string Select =
        $"SELECT w.id, w.request_id, {Sql.Place}, r.description, " +
        "r.status::text, w.master_id, e.full_name, w.work_type, " +
        "w.safety_measures, w.planned_start, w.planned_end, w.started_at, " +
        "w.finished_at FROM work_order w " +
        "JOIN request r ON r.id = w.request_id " +
        "JOIN premise p ON p.id = r.premise_id " +
        "JOIN house h ON h.id = p.house_id " +
        "JOIN employee e ON e.id = w.master_id ";

    public Task<MasterInfo?> GetMasterAsync(int employeeId) => db.SingleAsync(
        $"SELECT e.id, ro.code, e.is_active, {Sql.ActiveOrders} " +
        "FROM employee e JOIN role ro ON ro.id = e.role_id WHERE e.id = @e",
        r => new MasterInfo(r.Int(0), r.Str(1), r.Bool(2), r.Int(3)),
        ("e", employeeId));

    public Task<int> InsertAsync(NewWorkOrder o) => db.ScalarAsync<int>(
        "INSERT INTO work_order (request_id, master_id, work_type, " +
        "safety_measures, planned_start, planned_end) VALUES " +
        "(@r, @m, @w, @s, @ps, @pe) RETURNING id",
        ("r", o.RequestId), ("m", o.MasterId), ("w", o.WorkType),
        ("s", o.Safety), ("ps", o.PlannedStart), ("pe", o.PlannedEnd));

    public Task<WorkOrderState?> LockAsync(int id) => db.SingleAsync(
        "SELECT w.id, w.request_id, w.master_id, r.status::text " +
        "FROM work_order w JOIN request r ON r.id = w.request_id " +
        "WHERE w.id = @id FOR UPDATE OF w, r",
        r => new WorkOrderState(r.Int(0), r.Int(1), r.Int(2), r.Str(3)),
        ("id", id));

    public Task SetStartedAsync(int id) => db.ExecAsync(
        "UPDATE work_order SET started_at = app_now() WHERE id = @id",
        ("id", id));

    public Task SetFinishedAsync(int id) => db.ExecAsync(
        "UPDATE work_order SET finished_at = app_now() WHERE id = @id",
        ("id", id));

    public async Task<WorkOrderDto?> GetAsync(int id) =>
        (await LoadAsync("WHERE w.id = @id", ("id", id))).FirstOrDefault();

    public Task<IReadOnlyList<WorkOrderDto>> ListByMasterAsync(int masterId)
        => LoadAsync("WHERE w.master_id = @m AND r.status <> 'closed' " +
                     "ORDER BY w.planned_start", ("m", masterId));

    private async Task<IReadOnlyList<WorkOrderDto>> LoadAsync(string where,
        params (string, object?)[] args)
    {
        var heads = await db.QueryAsync(Select + where, r => (Id: r.Int(0),
            Row: new WorkOrderDto(r.Int(0), r.Int(1), r.Str(2), r.Str(3),
                r.Str(4), r.Int(5), r.Str(6), r.Str(7), r.StrN(8),
                r.TimeN(9), r.TimeN(10), r.TimeN(11), r.TimeN(12),
                Array.Empty<UsageDto>())), args);
        var result = new List<WorkOrderDto>();
        foreach (var (orderId, row) in heads)
            result.Add(row with { Materials = await UsagesAsync(orderId) });
        return result;
    }

    private Task<IReadOnlyList<UsageDto>> UsagesAsync(int orderId) =>
        db.QueryAsync(
            "SELECT u.id, u.material_id, m.name, m.unit, u.quantity, " +
            "u.unit_price, u.approval FROM material_usage u " +
            "JOIN material m ON m.id = u.material_id " +
            "WHERE u.work_order_id = @w ORDER BY u.id",
            r => new UsageDto(r.Int(0), r.Int(1), r.Str(2), r.Str(3),
                r.Dec(4), r.Dec(5), r.Str(6)), ("w", orderId));

    public Task<bool> UsageExistsAsync(int workOrderId, int materialId) =>
        db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM material_usage " +
            "WHERE work_order_id = @w AND material_id = @m)",
            ("w", workOrderId), ("m", materialId));

    public Task<int> AddUsageAsync(int workOrderId, int materialId,
        decimal quantity, decimal unitPrice) => db.ScalarAsync<int>(
        "INSERT INTO material_usage (work_order_id, material_id, quantity, " +
        "unit_price) VALUES (@w, @m, @q, @p) RETURNING id",
        ("w", workOrderId), ("m", materialId), ("q", quantity),
        ("p", unitPrice));

    public Task<string?> GetUsageApprovalAsync(int workOrderId, int usageId)
        => db.ScalarAsync<string>(
            "SELECT approval FROM material_usage WHERE id = @u " +
            "AND work_order_id = @w", ("u", usageId), ("w", workOrderId));

    public Task DeleteUsageAsync(int usageId) => db.ExecAsync(
        "DELETE FROM material_usage WHERE id = @u", ("u", usageId));

    public Task<IReadOnlyList<PendingUsage>> PendingUsagesAsync(
        int workOrderId) => db.QueryAsync(
        "SELECT id, material_id, quantity, unit_price FROM material_usage " +
        "WHERE work_order_id = @w AND approval = 'pending' ORDER BY id",
        r => new PendingUsage(r.Int(0), r.Int(1), r.Dec(2), r.Dec(3)),
        ("w", workOrderId));

    public Task SetApprovalAsync(int usageId, string approval,
        int engineerId) => db.ExecAsync(
        "UPDATE material_usage SET approval = @a, approved_by = @e " +
        "WHERE id = @u", ("a", approval), ("e", engineerId), ("u", usageId));
}

public class MaterialRepository(DbSession db) : IMaterialRepository
{
    private const string Select =
        "SELECT id, erp_code, name, unit, stock_qty, price, synced_at, " +
        "stock_qty < @lvl FROM material ";

    private static MaterialDto Map(Row r) => new(r.Int(0), r.Str(1),
        r.Str(2), r.Str(3), r.Dec(4), r.Dec(5), r.TimeN(6), r.Bool(7));

    public Task<IReadOnlyList<MaterialDto>> ListAsync(string? search,
        bool belowReorderOnly)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            where.Add("(name ILIKE @q OR erp_code LIKE @q)");
        if (belowReorderOnly)
            where.Add("stock_qty < @lvl");
        var sql = Select + (where.Count > 0
            ? "WHERE " + string.Join(" AND ", where) : "") + " ORDER BY name";
        return db.QueryAsync(sql, Map, ("lvl", BusinessRules.ReorderLevel),
            ("q", $"%{search?.Trim()}%"));
    }

    public Task<MaterialDto?> GetAsync(int id) => db.SingleAsync(
        Select + "WHERE id = @id", Map, ("lvl", BusinessRules.ReorderLevel),
        ("id", id));

    public Task<StockItem?> LockAsync(int id) => db.SingleAsync(
        "SELECT id, name, stock_qty, price FROM material WHERE id = @id " +
        "FOR UPDATE", r => new StockItem(r.Int(0), r.Str(1), r.Dec(2),
            r.Dec(3)), ("id", id));

    public Task<bool> ErpCodeExistsAsync(string erpCode, int? exceptId) =>
        db.ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM material WHERE erp_code = @c " +
            "AND id <> coalesce(@id::int, 0))", ("c", erpCode), ("id", exceptId));

    public Task<int> InsertAsync(MaterialUpsertDto m) => db.ScalarAsync<int>(
        "INSERT INTO material (erp_code, name, unit, stock_qty, price, " +
        "synced_at) VALUES (@c, @n, @u, @q, @p, app_now()) RETURNING id",
        ("c", m.ErpCode), ("n", m.Name.Trim()), ("u", m.Unit.Trim()),
        ("q", m.StockQty), ("p", m.Price));

    public async Task<bool> UpdateAsync(int id, MaterialUpsertDto m) =>
        await db.ExecAsync(
            "UPDATE material SET erp_code = @c, name = @n, unit = @u, " +
            "stock_qty = @q, price = @p, synced_at = app_now() " +
            "WHERE id = @id", ("c", m.ErpCode), ("n", m.Name.Trim()),
            ("u", m.Unit.Trim()), ("q", m.StockQty), ("p", m.Price),
            ("id", id)) == 1;
}

public class EmployeeRepository(DbSession db) : IEmployeeRepository
{
    private const string Select =
        "SELECT e.id, e.full_name, e.login, ro.code, e.phone, " +
        $"e.specialization, e.is_active, {Sql.ActiveOrders} " +
        "FROM employee e JOIN role ro ON ro.id = e.role_id ";

    private static EmployeeDto Map(Row r) => new(r.Int(0), r.Str(1),
        r.Str(2), r.Str(3), r.StrN(4), r.StrN(5), r.Bool(6), r.Int(7));

    public Task<IReadOnlyList<EmployeeDto>> ListAsync(string? role) =>
        role is null
            ? db.QueryAsync(Select + "ORDER BY e.id", Map)
            : db.QueryAsync(Select + "WHERE ro.code = @r ORDER BY e.id", Map,
                ("r", role));

    public Task<EmployeeDto?> GetAsync(int id) =>
        db.SingleAsync(Select + "WHERE e.id = @id", Map, ("id", id));

    public Task<int> InsertAsync(CreateEmployeeDto e, string passwordHash) =>
        db.ScalarAsync<int>(
            "INSERT INTO employee (role_id, full_name, phone, login, " +
            "password_hash, specialization) SELECT id, @n, @t, @l, @h, @s " +
            "FROM role WHERE code = @r RETURNING id",
            ("n", e.FullName.Trim()), ("t", e.Phone), ("l", e.Login),
            ("h", passwordHash), ("s", e.Specialization), ("r", e.Role));

    public Task SetActiveAsync(int id, bool isActive) => db.ExecAsync(
        "UPDATE employee SET is_active = @a WHERE id = @id",
        ("a", isActive), ("id", id));
}

public class ReferenceRepository(DbSession db) : IReferenceRepository
{
    public Task<IReadOnlyList<HouseDto>> HousesAsync() => db.QueryAsync(
        "SELECT h.id, h.address, h.floors, h.entrances, " +
        "(SELECT count(*)::int FROM premise p WHERE p.house_id = h.id) " +
        "FROM house h ORDER BY h.id",
        r => new HouseDto(r.Int(0), r.Str(1), r.ShortN(2), r.ShortN(3),
            r.Int(4)));

    public Task<bool> HouseExistsAsync(int id) => db.ScalarAsync<bool>(
        "SELECT EXISTS (SELECT 1 FROM house WHERE id = @id)", ("id", id));

    public Task<IReadOnlyList<PremiseDto>> PremisesAsync(int houseId) =>
        db.QueryAsync(
            "SELECT id, kind, number, entrance, floor, account_number " +
            "FROM premise WHERE house_id = @h ORDER BY kind, id",
            r => new PremiseDto(r.Int(0), r.Str(1), r.Str(2), r.ShortN(3),
                r.ShortN(4), r.StrN(5)), ("h", houseId));

    public Task<IReadOnlyList<CategoryDto>> CategoriesAsync() => db.QueryAsync(
        "SELECT c.id, c.name, " +
        "max(n.hours) FILTER (WHERE n.priority = 'emergency'), " +
        "max(n.hours) FILTER (WHERE n.priority = 'urgent'), " +
        "max(n.hours) FILTER (WHERE n.priority = 'planned') " +
        "FROM work_category c JOIN sla_norm n ON n.category_id = c.id " +
        "GROUP BY c.id, c.name ORDER BY c.id",
        r => new CategoryDto(r.Short(0), r.Str(1), r.Short(2), r.Short(3),
            r.Short(4)));
}

public class ReportRepository(DbSession db) : IReportRepository
{
    public Task<IReadOnlyList<SlaRow>> SlaRowsAsync(DateOnly from,
        DateOnly to) => db.QueryAsync(
        "SELECT c.name, count(*)::int, " +
        "count(*) FILTER (WHERE r.status = 'closed')::int, " +
        "count(*) FILTER (WHERE r.status = 'closed' " +
        "  AND r.closed_at <= r.deadline_at)::int, " +
        "count(*) FILTER (WHERE r.closed_at > r.deadline_at " +
        "  OR (r.closed_at IS NULL AND r.deadline_at < app_now()))::int, " +
        "coalesce(sum(m.cost), 0) " +
        "FROM request r JOIN work_category c ON c.id = r.category_id " +
        "LEFT JOIN (SELECT w.request_id, sum(u.quantity * u.unit_price) " +
        "  AS cost FROM work_order w JOIN material_usage u " +
        "  ON u.work_order_id = w.id WHERE u.approval = 'approved' " +
        "  GROUP BY w.request_id) m ON m.request_id = r.id " +
        "WHERE r.created_at >= @f AND r.created_at < @t + 1 " +
        "GROUP BY c.name ORDER BY c.name",
        r => new SlaRow(r.Str(0), r.Int(1), r.Int(2), r.Int(3), r.Int(4),
            r.Dec(5)), ("f", from), ("t", to));
}
