using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Core.Services;

/// <summary>Изменение заявок (ISP: отделено от чтения).</summary>
public interface IRequestCommandService
{
    Task<RequestDto> CreateAsync(CreateRequestDto dto);
    Task<RatingDto> RateAsync(int requestId, RateDto dto);
}

/// <summary>Просмотр и поиск заявок.</summary>
public interface IRequestQueryService
{
    Task<IReadOnlyList<RequestDto>> SearchAsync(RequestFilter filter);
    Task<RequestDto> GetAsync(int id);
    Task<IReadOnlyList<HistoryItemDto>> HistoryAsync(int id);
}

public class RequestService(
    IRequestRepository requests,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    INotifier notifier) : IRequestCommandService, IRequestQueryService
{
    public async Task<RequestDto> CreateAsync(CreateRequestDto dto)
    {
        var text = dto.Description.Trim();
        if (text.Length < BusinessRules.MinDescription)
            throw new ValidationException(
                $"Описание проблемы не короче {BusinessRules.MinDescription}"
                + " символов");
        int id;
        await using (var tx = await uow.BeginAsync())
        {
            var request = currentUser.IsInRole(Roles.Resident)
                ? await FromResidentAsync(dto, text)
                : await FromDispatcherAsync(dto, text);
            if (!await requests.CategoryExistsAsync(dto.CategoryId))
                throw new NotFoundException("Вид работ не найден");
            var duplicate = await requests.FindOpenDuplicateAsync(
                request.PremiseId, dto.CategoryId);
            if (duplicate is not null)
                throw new ConflictException(
                    $"По помещению уже открыта заявка {duplicate}");
            id = await requests.InsertAsync(request);
            await tx.CommitAsync();
        }
        var created = (await requests.GetAsync(id))!;
        if (dto.Priority == Priorities.Emergency)
            await notifier.EmergencyRegisteredAsync(id, created.Address);
        return created;
    }

    private async Task<NewRequest> FromResidentAsync(CreateRequestDto dto,
        string text)
    {
        var premise = await requests.FindPremiseOfResidentAsync(
                          currentUser.Id)
                      ?? throw new NotFoundException("Помещение не найдено");
        return new NewRequest(premise, currentUser.Id, dto.CategoryId,
            dto.Priority, text, "mobile", null);
    }

    private async Task<NewRequest> FromDispatcherAsync(CreateRequestDto dto,
        string text)
    {
        if (dto.PremiseId is not { } premise)
            throw new ValidationException("Укажите помещение");
        if (!await requests.PremiseExistsAsync(premise))
            throw new NotFoundException("Помещение не найдено");
        var resident = string.IsNullOrWhiteSpace(dto.Phone) ? null
            : await requests.FindResidentByPhoneAsync(dto.Phone.Trim());
        return new NewRequest(premise, resident, dto.CategoryId,
            dto.Priority, text, "phone", currentUser.Id);
    }

    public async Task<RatingDto> RateAsync(int requestId, RateDto dto)
    {
        await using var tx = await uow.BeginAsync();
        var request = await requests.LockAsync(requestId)
                      ?? throw new NotFoundException("Заявка не найдена");
        EnsureOwner(request.ResidentId);
        if (request.Status != Statuses.Closed)
            throw new ConflictException(
                "Оценить можно только закрытую заявку");
        if (await requests.RatingExistsAsync(requestId))
            throw new ConflictException("Заявка уже оценена");
        var rating = await requests.InsertRatingAsync(requestId, dto.Score,
            string.IsNullOrWhiteSpace(dto.Comment) ? null
                : dto.Comment.Trim());
        await tx.CommitAsync();
        return rating;
    }

    public Task<IReadOnlyList<RequestDto>> SearchAsync(RequestFilter filter)
    {
        if (filter.Status is { } s && !Statuses.All.Contains(s))
            throw new ValidationException($"Неизвестный статус: {s}");
        if (filter.Priority is { } p && !Priorities.All.Contains(p))
            throw new ValidationException($"Неизвестный приоритет: {p}");
        int? owner = currentUser.IsInRole(Roles.Resident)
            ? currentUser.Id : null;
        return requests.SearchAsync(filter, owner);
    }

    public async Task<RequestDto> GetAsync(int id)
    {
        await EnsureVisibleAsync(id);
        return (await requests.GetAsync(id))!;
    }

    public async Task<IReadOnlyList<HistoryItemDto>> HistoryAsync(int id)
    {
        await EnsureVisibleAsync(id);
        return await requests.HistoryAsync(id);
    }

    /// <summary>Жилец видит только свои заявки (DRY: одна проверка для
    /// карточки и истории).</summary>
    private async Task EnsureVisibleAsync(int id)
    {
        var request = await requests.GetAsync(id)
                      ?? throw new NotFoundException("Заявка не найдена");
        if (currentUser.IsInRole(Roles.Resident))
            EnsureOwner(await requests.GetOwnerAsync(request.Id));
    }

    private void EnsureOwner(int? residentId)
    {
        if (residentId != currentUser.Id)
            throw new ForbiddenException("Заявка подана другим жильцом");
    }
}
