# УК-Менеджмент — REST API

Серверная часть информационной системы управляющей компании ООО «Этажи-Сервис Плюс»:
регистрация заявок жильцов, назначение мастеров, наряды-допуски, расход и списание
товарно-материальных ценностей (ТМЦ), отчёт о соблюдении нормативных сроков.

Лабораторная работа 9 по дисциплине «Технологии проектирования сложных информационных
систем» (БГУИР, гр. 314301).

## Технологии

- .NET 8, ASP.NET Core Web API, аутентификация JWT Bearer
- PostgreSQL 17, драйвер Npgsql
- Swagger UI (Swashbuckle) — `/swagger`
- модульные тесты: xUnit, Moq

## Структура

```
db/                              скрипты базы данных
  01_schema.sql 02_logic.sql     таблицы, триггеры, представление
  03_data.sql 04_auth.sql        демонстрационные данные, учётные записи
src/UkManagement.Core            бизнес-логика: сервисы, правила, абстракции, DTO
src/UkManagement.Infrastructure  PostgreSQL, JWT, PBKDF2, уведомления, выгрузка отчётов
src/UkManagement.Api             контроллеры REST API, обработка ошибок, Program.cs
tests/UkManagement.Tests         модульные тесты сервисов и правил
```

## Запуск

1. Создать базу данных и выполнить скрипты:

   ```bash
   createdb -U postgres uk_management
   for f in db/0*.sql; do psql -U postgres -d uk_management -f "$f"; done
   ```

2. Указать строку подключения `ConnectionStrings:Uk` и ключ `Jwt:Key`
   (в `src/UkManagement.Api/appsettings.json` или переменными окружения
   `ConnectionStrings__Uk`, `Jwt__Key`).

3. Запустить сервер и тесты:

   ```bash
   dotnet run --project src/UkManagement.Api
   dotnet test
   ```

   API доступен по адресу `http://localhost:5080`, документация — `http://localhost:5080/swagger`.

Параметр `App:DemoNow` задаёт время демонстрационного стенда (данные на 18.11.2026),
`App:DemoSpeed` — ускорение времени стенда. Для работы с реальным временем параметр
`App:DemoNow` нужно удалить.

## Учётные записи стенда

Пароль всех учётных записей — `Uk2026!pass`.

| Логин | Роль |
|---|---|
| o.petrova | диспетчер |
| a.sidorenko, d.kovalev, i.lukashevich | мастер |
| v.gurinevich | главный инженер |
| t.melnik | бухгалтер |
| novik, shevchuk | жилец |

## Основные конечные точки

| Метод | URL | Назначение |
|---|---|---|
| POST | /api/auth/login | вход, получение токена |
| POST | /api/auth/register | регистрация личного кабинета жильца |
| POST | /api/requests | регистрация заявки |
| GET | /api/requests | поиск заявок |
| POST | /api/work-orders | назначение исполнителя |
| POST | /api/work-orders/{id}/start, /finish, /materials | выполнение работ мастером |
| POST | /api/work-orders/{id}/close | закрытие наряда со списанием ТМЦ |
| POST | /api/requests/{id}/rating | оценка работ жильцом |
| GET, POST, PUT | /api/materials | справочник ТМЦ |
| GET | /api/reports/sla, /api/reports/sla/export | отчёт о нормативных сроках |
| GET, POST, PATCH | /api/employees | учётные записи сотрудников |
| GET | /api/houses, /api/categories | справочники |

Ошибки возвращаются в едином формате:

```json
{ "error": true, "status": 409, "message": "Заявка уже оценена", "timestamp": "2026-11-18T16:30:06.2+03:00" }
```
