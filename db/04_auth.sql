-- УК-Менеджмент: учётные записи для REST API (лабораторная работа 9)
-- Пароли хранятся хешем PBKDF2-SHA256 (100 000 итераций, соль 16 байт).
-- Демонстрационный пароль всех учётных записей: Uk2026!pass
SET timezone = 'Europe/Minsk';

ALTER TABLE employee ALTER COLUMN password_hash TYPE varchar(200);

UPDATE employee SET password_hash =
    'PBKDF2-SHA256$100000$VWtNYW5hZ2VtZW50MjAyNg==$'
    || 'FZCH9fo1xhF/cfqcEz7DclrMWGQvDXHEiyeYUfq9DxY=';

-- личный кабинет жильца: одна учётная запись на жильца
CREATE TABLE resident_account (
    resident_id   integer PRIMARY KEY
                  REFERENCES resident (id) ON DELETE CASCADE,
    login         varchar(30) NOT NULL UNIQUE,
    password_hash varchar(200) NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT app_now()
);

INSERT INTO resident_account (resident_id, login, password_hash,
                              created_at) VALUES
 (1, 'novik', 'PBKDF2-SHA256$100000$VWtNYW5hZ2VtZW50MjAyNg==$'
  || 'FZCH9fo1xhF/cfqcEz7DclrMWGQvDXHEiyeYUfq9DxY=', '2026-11-10 12:00'),
 (4, 'shevchuk', 'PBKDF2-SHA256$100000$VWtNYW5hZ2VtZW50MjAyNg==$'
  || 'FZCH9fo1xhF/cfqcEz7DclrMWGQvDXHEiyeYUfq9DxY=', '2026-11-12 18:30');
