using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;

namespace UkManagement.Infrastructure;

/// <summary>Хеш пароля PBKDF2-SHA256 в формате
/// «PBKDF2-SHA256$итерации$соль$хеш».</summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int Iterations = 100_000;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(password, salt, Iterations);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}$"
               + Convert.ToBase64String(hash);
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Derive(password, Convert.FromBase64String(parts[2]),
            int.Parse(parts[1]));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int n)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, n,
            HashAlgorithmName.SHA256, 32);
}

/// <summary>Параметры токена доступа (раздел Jwt файла appsettings.json).
/// </summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "uk-management";
    public string Key { get; set; } = "";
    public int LifetimeHours { get; set; } = 8;
}

public class JwtTokenService(JwtOptions options, IClock clock)
    : ITokenService
{
    /// <summary>Имя claim с ролью пользователя.</summary>
    public const string RoleClaim = "role";

    public TokenResponse Issue(Identity identity)
    {
        var expires = clock.Now.AddHours(options.LifetimeHours);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(options.Issuer, options.Issuer,
            new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, identity.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, identity.Login),
                new Claim(RoleClaim, identity.Role)
            },
            notBefore: clock.Now.UtcDateTime, expires: expires.UtcDateTime,
            signingCredentials: credentials);
        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token),
            expires, identity.Role, identity.Name);
    }
}

/// <summary>Системное время.</summary>
public class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now.ToOffset(
        Data.DbSession.Offset);
}

/// <summary>Время демонстрационного стенда: начинается с заданного
/// момента (данные ЛР7 на 18.11.2026) и идёт быстрее реального в speed раз.
/// </summary>
public class DemoClock(DateTimeOffset start, double speed) : IClock
{
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    public DateTimeOffset Now =>
        (start + (DateTimeOffset.UtcNow - _started) * speed)
        .ToOffset(Data.DbSession.Offset);
}

/// <summary>Журнал событий стенда: уведомления и обмен с 1С.</summary>
public class EventJournal
{
    private readonly List<string> _items = new();

    public void Add(string text)
    {
        lock (_items) _items.Add(text);
    }

    public IReadOnlyList<string> Items
    {
        get { lock (_items) return _items.ToList(); }
    }
}

/// <summary>Канал уведомлений «журнал» (в эксплуатации к нему добавляется
/// канал push-уведомлений).</summary>
public class LogNotificationChannel(ILogger<LogNotificationChannel> log,
    EventJournal journal) : INotificationChannel
{
    public string Name => "log";

    public Task SendAsync(string recipientRole, int? recipientId, string text)
    {
        var to = recipientId is null ? recipientRole
            : $"{recipientRole} {recipientId}";
        log.LogInformation("Уведомление ({To}): {Text}", to, text);
        journal.Add($"{to}: {text}");
        return Task.CompletedTask;
    }
}

/// <summary>Шлюз 1С стенда: заявки на закупку записываются в журнал.</summary>
public class JournalErpGateway(ILogger<JournalErpGateway> log,
    EventJournal journal) : IErpGateway
{
    public Task RequestPurchaseAsync(IReadOnlyList<string> materials)
    {
        var text = "Заявка на закупку — " + string.Join(", ", materials);
        log.LogInformation("Обмен с 1С: {Text}", text);
        journal.Add("1С: " + text);
        return Task.CompletedTask;
    }
}

/// <summary>Отчёт в формате CSV для Excel (разделитель «;», UTF-8 с BOM).
/// </summary>
public class CsvReportExporter : IReportExporter
{
    public string Format => "csv";
    public string ContentType => "text/csv; charset=utf-8";

    public byte[] Export(SlaReportDto r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Отчёт о соблюдении нормативных сроков;{r.From:dd.MM.yyyy}"
                      + $";{r.To:dd.MM.yyyy}");
        sb.AppendLine("Вид работ;Заявок;Закрыто;В срок;С нарушением срока;"
                      + "Материалы, BYN");
        foreach (var row in r.ByCategory)
            sb.AppendLine($"{row.Category};{row.Total};{row.Closed};"
                          + $"{row.OnTime};{row.Overdue};{row.MaterialsCost:0.00}");
        sb.AppendLine($"Итого;{r.Total};{r.Closed};{r.OnTime};{r.Overdue};"
                      + $"{r.MaterialsCost:0.00}");
        sb.AppendLine($"Закрыто в срок, %;{r.OnTimePercent:0.0}");
        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }
}

/// <summary>Отчёт в формате JSON для обмена с другими системами.</summary>
public class JsonReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    public string Format => "json";
    public string ContentType => "application/json; charset=utf-8";

    public byte[] Export(SlaReportDto report)
        => JsonSerializer.SerializeToUtf8Bytes(report, Options);
}
