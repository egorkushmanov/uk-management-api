namespace UkManagement.Core.Domain;

/// <summary>Роли пользователей (значение claim role в токене JWT).</summary>
public static class Roles
{
    public const string Dispatcher = "dispatcher";
    public const string Master = "master";
    public const string Engineer = "engineer";
    public const string Accountant = "accountant";
    public const string Resident = "resident";

    public static readonly string[] Staff =
        { Dispatcher, Master, Engineer, Accountant };
}

/// <summary>Статусы заявки (тип request_status в базе данных).</summary>
public static class Statuses
{
    public const string New = "new";
    public const string Assigned = "assigned";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Closed = "closed";

    public static readonly string[] All =
        { New, Assigned, InProgress, Done, Closed };
}

/// <summary>Приоритеты заявки (тип request_priority).</summary>
public static class Priorities
{
    public const string Emergency = "emergency";
    public const string Urgent = "urgent";
    public const string Planned = "planned";

    public static readonly string[] All = { Emergency, Urgent, Planned };
}

/// <summary>Состояния позиции расхода ТМЦ.</summary>
public static class Approvals
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

/// <summary>Правила предметной области, общие для нескольких сервисов.</summary>
public static class BusinessRules
{
    /// <summary>Минимальная длина описания проблемы.</summary>
    public const int MinDescription = 15;

    /// <summary>Наибольшее число активных нарядов у мастера.</summary>
    public const int MaxActiveOrders = 3;

    /// <summary>Остаток ТМЦ, ниже которого формируется закупка в 1С.</summary>
    public const decimal ReorderLevel = 5;

    /// <summary>Вид работ, для которого обязательны меры безопасности.</summary>
    public const string ElectricCategory = "Электроснабжение";
}
