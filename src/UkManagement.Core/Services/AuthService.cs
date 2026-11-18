using UkManagement.Core.Abstractions;
using UkManagement.Core.Contracts;
using UkManagement.Core.Domain;

namespace UkManagement.Core.Services;

public interface IAuthService
{
    Task<TokenResponse> LoginAsync(LoginRequest request);
    Task<UserDto> RegisterResidentAsync(RegisterResidentRequest request);
    Task<UserDto> MeAsync();
}

/// <summary>Вход сотрудников и жильцов, регистрация личного кабинета.</summary>
public class AuthService(
    IAccountRepository accounts,
    IPasswordHasher hasher,
    ITokenService tokens,
    IUnitOfWork uow,
    ICurrentUser currentUser) : IAuthService
{
    private const string BadCredentials = "Неверный логин или пароль";

    public async Task<TokenResponse> LoginAsync(LoginRequest request)
    {
        var login = request.Login.Trim().ToLowerInvariant();
        var account = await accounts.FindEmployeeAsync(login)
                      ?? await accounts.FindResidentAccountAsync(login);
        if (account is null || !hasher.Verify(request.Password,
                account.PasswordHash))
            throw new UnauthorizedException(BadCredentials);
        if (!account.IsActive)
            throw new ForbiddenException("Учётная запись заблокирована");
        return tokens.Issue(new Identity(account.Id, account.Login,
            account.Name, account.Role));
    }

    public async Task<UserDto> RegisterResidentAsync(
        RegisterResidentRequest request)
    {
        await using var tx = await uow.BeginAsync();
        var residentId = await accounts.FindResidentAsync(
                             request.AccountNumber, request.Phone)
                         ?? throw new NotFoundException(
                             "Жилец с указанным лицевым счётом и телефоном "
                             + "не найден");
        if (await accounts.HasAccountAsync(residentId))
            throw new ConflictException(
                "Личный кабинет для жильца уже зарегистрирован");
        if (await accounts.LoginExistsAsync(request.Login))
            throw new ConflictException("Логин уже занят");
        await accounts.CreateResidentAccountAsync(residentId, request.Login,
            hasher.Hash(request.Password));
        await tx.CommitAsync();
        return (await accounts.GetResidentAsync(residentId))!;
    }

    public async Task<UserDto> MeAsync()
    {
        var user = currentUser.IsInRole(Roles.Resident)
            ? await accounts.GetResidentAsync(currentUser.Id)
            : await accounts.GetEmployeeAsync(currentUser.Id);
        return user ?? throw new NotFoundException(
            "Учётная запись не найдена");
    }
}
