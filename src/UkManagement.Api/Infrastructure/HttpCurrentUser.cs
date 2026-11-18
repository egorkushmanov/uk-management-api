using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using UkManagement.Core.Abstractions;
using UkManagement.Core.Domain;
using UkManagement.Infrastructure;

namespace UkManagement.Api.Infrastructure;

/// <summary>Пользователь из токена JWT текущего запроса.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated =>
        Principal.Identity?.IsAuthenticated == true;

    public int Id => int.Parse(Claim(JwtRegisteredClaimNames.Sub));
    public string Login => Claim(JwtRegisteredClaimNames.UniqueName);
    public string Role => Claim(JwtTokenService.RoleClaim);

    private string Claim(string type) =>
        IsAuthenticated
            ? Principal.FindFirstValue(type) ?? ""
            : throw new UnauthorizedException("Требуется авторизация");
}
