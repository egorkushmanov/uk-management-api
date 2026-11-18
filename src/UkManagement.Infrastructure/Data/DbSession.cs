using Npgsql;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Domain;

namespace UkManagement.Infrastructure.Data;

/// <summary>Соединение с базой данных на время обработки запроса HTTP
/// (единица работы). Передаёт триггерам сотрудника и текущее время.</summary>
public sealed class DbSession(
    NpgsqlDataSource source,
    ICurrentUser user,
    IClock clock) : IUnitOfWork, IAsyncDisposable
{
    /// <summary>Часовой пояс ответов API (Europe/Minsk, UTC+3).</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public async Task<ITransaction> BeginAsync()
    {
        var connection = await ConnectionAsync();
        _transaction = await connection.BeginTransactionAsync();
        return new Transaction(this);
    }

    private async Task<NpgsqlConnection> ConnectionAsync()
    {
        if (_connection is not null)
            return _connection;
        _connection = await source.OpenConnectionAsync();
        var staff = user.IsAuthenticated && user.Role != Roles.Resident;
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config('app.now', @n, false), " +
            "set_config('app.employee_id', @e, false)", _connection);
        cmd.Parameters.AddWithValue("n",
            clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fffzzz"));
        cmd.Parameters.AddWithValue("e", staff ? user.Id.ToString() : "");
        await cmd.ExecuteNonQueryAsync();
        return _connection;
    }

    private async Task<NpgsqlCommand> CommandAsync(string sql,
        (string Name, object? Value)[] args)
    {
        var cmd = new NpgsqlCommand(sql, await ConnectionAsync(),
            _transaction);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value switch
            {
                null => DBNull.Value,
                // timestamptz в Npgsql передаётся только в UTC
                DateTimeOffset t => t.ToUniversalTime(),
                _ => value
            });
        return cmd;
    }

    public async Task<int> ExecAsync(string sql,
        params (string, object?)[] args)
    {
        await using var cmd = await CommandAsync(sql, args);
        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAsync<T>(string sql,
        params (string, object?)[] args)
    {
        await using var cmd = await CommandAsync(sql, args);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql,
        Func<Row, T> map, params (string, object?)[] args)
    {
        await using var cmd = await CommandAsync(sql, args);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await reader.ReadAsync())
            list.Add(map(new Row(reader)));
        return list;
    }

    public async Task<T?> SingleAsync<T>(string sql, Func<Row, T> map,
        params (string, object?)[] args) where T : class
        => (await QueryAsync(sql, map, args)).FirstOrDefault();

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }

    private sealed class Transaction(DbSession session) : ITransaction
    {
        private bool _done;

        public async Task CommitAsync()
        {
            await session._transaction!.CommitAsync();
            await ReleaseAsync();
        }

        /// <summary>Незафиксированная транзакция откатывается.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_done)
                return;
            await session._transaction!.RollbackAsync();
            await ReleaseAsync();
        }

        private async Task ReleaseAsync()
        {
            await session._transaction!.DisposeAsync();
            session._transaction = null;
            _done = true;
        }
    }
}

/// <summary>Строка результата запроса с типизированным чтением столбцов
/// (DRY: преобразование NULL и времени выполняется в одном месте).</summary>
public readonly struct Row(NpgsqlDataReader reader)
{
    public int Int(int i) => reader.GetInt32(i);
    public short Short(int i) => reader.GetInt16(i);
    public long Long(int i) => reader.GetInt64(i);
    public bool Bool(int i) => reader.GetBoolean(i);
    public decimal Dec(int i) => reader.GetDecimal(i);
    public string Str(int i) => reader.GetString(i);

    public string? StrN(int i) => reader.IsDBNull(i) ? null
        : reader.GetString(i);

    public int? IntN(int i) => reader.IsDBNull(i) ? null
        : reader.GetInt32(i);

    public short? ShortN(int i) => reader.IsDBNull(i) ? null
        : reader.GetInt16(i);

    public DateTimeOffset Time(int i)
        => reader.GetFieldValue<DateTimeOffset>(i).ToOffset(DbSession.Offset);

    public DateTimeOffset? TimeN(int i) => reader.IsDBNull(i) ? null
        : Time(i);
}
