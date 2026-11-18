using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using UkManagement.Api.Infrastructure;
using UkManagement.Core;
using UkManagement.Core.Abstractions;
using UkManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// внедрение зависимостей: бизнес-логика, реализации абстракций, веб-слой
services.AddUkCore();
services.AddUkInfrastructure(builder.Configuration);
services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser, HttpCurrentUser>();
services.AddExceptionHandler<ApiExceptionHandler>();

services.AddControllers()
    .ConfigureApiBehaviorOptions(o =>
        o.InvalidModelStateResponseFactory = ErrorResponses.InvalidModel);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Issuer,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt.Key)),
            RoleClaimType = JwtTokenService.RoleClaim,
            NameClaimType = JwtRegisteredClaimNames.UniqueName
        };
        // ответы 401 и 403 в едином формате ошибок
        o.Events = new JwtBearerEvents
        {
            OnChallenge = c =>
            {
                c.HandleResponse();
                return ErrorResponses.WriteAsync(c.HttpContext, 401,
                    "Требуется авторизация: токен отсутствует, "
                    + "недействителен или просрочен");
            },
            OnForbidden = c => ErrorResponses.WriteAsync(c.HttpContext, 403,
                "Недостаточно прав для выполнения операции")
        };
    });
// срок действия токена проверяется по часам приложения (IClock)
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IClock>((o, clock) =>
        o.TokenValidationParameters.LifetimeValidator =
            (notBefore, expires, _, _) =>
                notBefore <= clock.Now.UtcDateTime
                && clock.Now.UtcDateTime < expires);
services.AddAuthorization();

services.AddEndpointsApiExplorer();
services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "УК-Менеджмент REST API",
        Version = "v1",
        Description = "Серверная часть ИС управляющей компании "
                      + "ООО «Этажи-Сервис Плюс»"
    });
    o.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"));
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
        { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();
app.UseExceptionHandler(_ => { });
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

/// <summary>Точка входа (открыта для тестов).</summary>
public partial class Program;
