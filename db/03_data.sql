-- УК-Менеджмент: начальные данные
SET timezone = 'Europe/Minsk';

INSERT INTO role (code, name) VALUES
 ('dispatcher', 'Диспетчер'), ('master', 'Мастер'),
 ('engineer', 'Главный инженер'), ('accountant', 'Бухгалтер');

-- пароль всех демонстрационных учётных записей хранится хешем BCrypt
INSERT INTO employee (role_id, full_name, phone, login,
                      password_hash, specialization) VALUES
 (1, 'Петрова Ольга Николаевна', '+375291234501', 'o.petrova',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.', NULL),
 (2, 'Сидоренко Андрей Викторович', '+375291234502', 'a.sidorenko',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.',
  'Электрик, сантехник'),
 (2, 'Ковалёв Дмитрий Сергеевич', '+375291234503', 'd.kovalev',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.',
  'Сантехник'),
 (2, 'Лукашевич Игорь Петрович', '+375291234504', 'i.lukashevich',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.',
  'Общестроительные работы'),
 (3, 'Гуриневич Виктор Михайлович', '+375291234505', 'v.gurinevich',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.', NULL),
 (4, 'Мельник Татьяна Александровна', '+375291234506', 't.melnik',
  '$2a$10$7EqJtq98hPqEX7fNZaFWoOhi5BWX4Z3pDu/vpSeTWD5a8hTkXzv6.', NULL);

INSERT INTO house (address, floors, entrances, build_year, gis_guid)
VALUES
 ('ул. Притыцкого, 62', 9, 4, 1987,
  '8f2a1c34-5b6d-4e7f-8a9b-0c1d2e3f4a51'),
 ('пр. Победителей, 119', 19, 3, 2009,
  '8f2a1c34-5b6d-4e7f-8a9b-0c1d2e3f4a52'),
 ('ул. Кальварийская, 21', 12, 2, 1998,
  '8f2a1c34-5b6d-4e7f-8a9b-0c1d2e3f4a53');

INSERT INTO premise (house_id, kind, number, entrance, floor, area,
                     account_number) VALUES
 (1, 'apartment', '114', 4, 2, 52.40, '4012-0114'),
 (1, 'apartment', '37', 1, 3, 48.10, '4012-0037'),
 (1, 'apartment', '201', 4, 9, 61.70, '4012-0201'),
 (1, 'entrance', '1', 1, NULL, NULL, NULL),
 (2, 'apartment', '52', 2, 4, 74.30, '4019-0052'),
 (2, 'apartment', '7', 1, 2, 39.80, '4019-0007'),
 (2, 'entrance', '2', 2, NULL, NULL, NULL),
 (3, 'apartment', '8', 1, 2, 56.20, '4021-0008'),
 (3, 'apartment', '64', 2, 11, 63.90, '4021-0064'),
 (3, 'entrance', '1', 1, NULL, NULL, NULL);

INSERT INTO resident (premise_id, full_name, phone, email) VALUES
 (1, 'Новик Елена Сергеевна', '+375447001114', 'novik@example.by'),
 (2, 'Жук Сергей Павлович', '+375336401802', 'zhuk@example.by'),
 (3, 'Климович Артём Юрьевич', '+375297002201', NULL),
 (5, 'Шевчук Ирина Олеговна', '+375447005252', 'shevchuk@example.by'),
 (8, 'Ярошевич Анна Викторовна', '+375297112458',
  'yaroshevich@example.by'),
 (9, 'Мороз Павел Андреевич', '+375296406464', NULL);

INSERT INTO work_category (name) VALUES
 ('Водоснабжение'), ('Отопление'), ('Электроснабжение'), ('Лифт'),
 ('Сантехника'), ('Кровля'), ('Благоустройство');

-- нормативы: аварийные — 2 ч (лифт — 1 ч), срочные — 24 ч,
-- плановые — 72 ч (благоустройство — 168 ч)
INSERT INTO sla_norm (category_id, priority, hours)
SELECT c.id, p.priority,
       CASE p.priority
            WHEN 'emergency' THEN CASE c.name WHEN 'Лифт' THEN 1
                                              ELSE 2 END
            WHEN 'urgent'    THEN 24
            ELSE CASE c.name WHEN 'Благоустройство' THEN 168
                             ELSE 72 END
       END
  FROM work_category c
 CROSS JOIN unnest(enum_range(NULL::request_priority))
            AS p(priority);

INSERT INTO material (erp_code, name, unit, stock_qty, price,
                      synced_at) VALUES
 ('00-00000112', 'Сифон кухонный 40 мм', 'шт', 14, 9.80,
  '2026-11-18 08:00'),
 ('00-00000127', 'Розетка двойная с заземлением', 'шт', 36, 7.45,
  '2026-11-18 08:00'),
 ('00-00000203', 'Мастика битумная, 20 кг', 'ведро', 1, 96.00,
  '2026-11-18 08:00'),
 ('00-00000215', 'Светильник НПП 60 Вт', 'шт', 12, 18.90,
  '2026-11-18 08:00'),
 ('00-00000216', 'Лампа светодиодная 12 Вт E27', 'шт', 60, 3.20,
  '2026-11-18 08:00'),
 ('00-00000301', 'Труба ППР 20 мм', 'м', 40, 2.15,
  '2026-11-18 08:00');

-- заявки: срок рассчитывается триггером, статус — журналом
SELECT set_config('app.employee_id', '1', false);
INSERT INTO request (premise_id, resident_id, category_id, priority,
                     description, source, created_at,
                     registered_by) VALUES
 (10, NULL, 6, 'urgent', 'Протечка с кровли после дождя',
  'phone', '2026-11-17 10:15', 1),
 (3, 3, 3, 'emergency', 'Искрит розетка в спальне',
  'phone', '2026-11-17 11:47', 1),
 (10, NULL, 7, 'planned', 'Замена доводчика входной двери',
  'web', '2026-11-17 14:02', 1),
 (5, 4, 5, 'urgent', 'Засор канализации на кухне',
  'mobile', '2026-11-17 16:20', NULL),
 (8, 5, 2, 'urgent', 'Холодные радиаторы в двух комнатах',
  'phone', '2026-11-18 09:31', 1),
 (4, 2, 3, 'urgent', 'Не работает освещение на лестничной площадке',
  'mobile', '2026-11-18 09:05', NULL),
 (7, NULL, 4, 'emergency', 'Лифт остановился между этажами',
  'phone', '2026-11-18 08:40', 1),
 (1, 1, 1, 'emergency', 'Течь стояка ГВС в ванной комнате',
  'phone', '2026-11-18 08:12', 1);
UPDATE status_history h SET changed_at = r.created_at
  FROM request r WHERE r.id = h.request_id;

-- наряды-допуски
INSERT INTO work_order (request_id, master_id, work_type,
                        safety_measures, planned_start, planned_end,
                        started_at, finished_at) VALUES
 (1041, 4, 'Ремонт кровельного покрытия',
  'Работы на высоте с ограждением', '2026-11-17 12:00',
  '2026-11-17 16:00', '2026-11-17 12:10', '2026-11-17 15:40'),
 (1042, 2, 'Электромонтажные работы до 1000 В',
  'Отключить автомат, проверить отсутствие напряжения',
  '2026-11-17 12:00', '2026-11-17 13:00', '2026-11-17 12:05',
  '2026-11-17 12:50'),
 (1043, 4, 'Замена доводчика', NULL, '2026-11-18 14:00',
  '2026-11-18 15:00', '2026-11-18 14:05', NULL),
 (1044, 3, 'Прочистка канализации', NULL, '2026-11-17 18:00',
  '2026-11-17 19:00', '2026-11-17 18:10', '2026-11-17 18:55'),
 (1046, 2, 'Электромонтажные работы до 1000 В',
  'Отключить автомат щита ЩЭ-3', '2026-11-18 11:00',
  '2026-11-18 13:00', NULL, NULL),
 (1047, 3, 'Аварийная остановка лифта', 'Работы по наряду лифтёра',
  '2026-11-18 08:50', '2026-11-18 09:40', '2026-11-18 08:55', NULL),
 (1048, 2, 'Замена участка стояка ГВС', 'Перекрыть стояк ГВС',
  '2026-11-18 08:30', '2026-11-18 10:30', '2026-11-18 08:45', NULL);

-- изменения статусов в порядке их выполнения
SELECT set_config('app.now', '2026-11-17 12:05', false);
UPDATE request SET status = 'in_progress' WHERE id IN (1041, 1042);
SELECT set_config('app.now', '2026-11-17 12:50', false);
UPDATE request SET status = 'done' WHERE id = 1042;
SELECT set_config('app.now', '2026-11-17 15:40', false);
UPDATE request SET status = 'done' WHERE id = 1041;
SELECT set_config('app.now', '2026-11-17 16:00', false);
UPDATE request SET status = 'closed' WHERE id IN (1041, 1042);
SELECT set_config('app.now', '2026-11-17 19:00', false);
UPDATE request SET status = 'done' WHERE id = 1044;
SELECT set_config('app.now', '2026-11-18 09:14', false);
UPDATE request SET status = 'assigned' WHERE id IN (1043, 1046);
UPDATE request SET status = 'in_progress' WHERE id IN (1047, 1048);
SELECT set_config('app.now', '2026-11-18 14:05', false);
UPDATE request SET status = 'in_progress' WHERE id = 1043;

-- расход ТМЦ по нарядам (цена фиксируется на момент использования)
INSERT INTO material_usage (work_order_id, material_id, quantity,
                            unit_price) VALUES
 (1, 3, 2, 96.00),
 (2, 2, 2, 7.45),
 (4, 1, 1, 9.80),
 (7, 6, 3, 2.15);

-- оценки жильцов
INSERT INTO rating (request_id, score, comment, created_at) VALUES
 (1042, 5, 'Быстро и аккуратно', '2026-11-17 18:30'),
 (1044, 4, NULL, '2026-11-17 20:10');

-- заявки из мобильного приложения регистрируются без участия диспетчера
UPDATE status_history h SET employee_id = NULL
  FROM request r
 WHERE r.id = h.request_id AND r.registered_by IS NULL
   AND h.status = 'new';
