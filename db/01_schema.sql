-- УК-Менеджмент: схема базы данных (PostgreSQL 17)
-- текущий момент: значение app.now, заданное сеансом (загрузка
-- исторических данных, тесты), иначе системное время
CREATE FUNCTION app_now() RETURNS timestamptz AS $$
    SELECT coalesce(nullif(current_setting('app.now', true), '')
                    ::timestamptz, now())
$$ LANGUAGE sql STABLE;

-- перечислимые типы
CREATE TYPE request_priority AS ENUM ('emergency', 'urgent', 'planned');
CREATE TYPE request_status AS ENUM
    ('new', 'assigned', 'in_progress', 'done', 'closed');

-- сотрудники и роли
CREATE TABLE role (
    id   smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code varchar(20) NOT NULL UNIQUE,
    name varchar(50) NOT NULL
);
CREATE TABLE employee (
    id             integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    role_id        smallint NOT NULL REFERENCES role (id),
    full_name      varchar(100) NOT NULL,
    phone          varchar(20),
    login          varchar(30) NOT NULL UNIQUE,
    password_hash  char(60) NOT NULL,
    specialization varchar(100),
    is_active      boolean NOT NULL DEFAULT true
);

-- жилой фонд и жильцы
CREATE TABLE house (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    address    varchar(150) NOT NULL UNIQUE,
    floors     smallint CHECK (floors BETWEEN 1 AND 50),
    entrances  smallint CHECK (entrances > 0),
    build_year smallint CHECK (build_year BETWEEN 1900 AND 2100),
    gis_guid   uuid UNIQUE
);
CREATE TABLE premise (
    id             integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    house_id       integer NOT NULL REFERENCES house (id),
    kind           varchar(10) NOT NULL
                   CHECK (kind IN ('apartment', 'entrance', 'technical')),
    number         varchar(10) NOT NULL,
    entrance       smallint,
    floor          smallint,
    area           numeric(6, 2) CHECK (area > 0),
    account_number char(9) UNIQUE,
    UNIQUE (house_id, kind, number)
);
CREATE TABLE resident (
    id         integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    premise_id integer NOT NULL REFERENCES premise (id),
    full_name  varchar(100) NOT NULL,
    phone      varchar(20) UNIQUE,
    email      varchar(100)
);

-- категории работ и нормативные сроки
CREATE TABLE work_category (
    id   smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(50) NOT NULL UNIQUE
);
CREATE TABLE sla_norm (
    category_id smallint NOT NULL REFERENCES work_category (id),
    priority    request_priority NOT NULL,
    hours       smallint NOT NULL CHECK (hours > 0),
    PRIMARY KEY (category_id, priority)
);

-- заявки и история статусов
CREATE TABLE request (
    id              integer GENERATED ALWAYS AS IDENTITY
                    (START WITH 1041) PRIMARY KEY,
    premise_id      integer NOT NULL REFERENCES premise (id),
    resident_id     integer REFERENCES resident (id),
    category_id     smallint NOT NULL REFERENCES work_category (id),
    priority        request_priority NOT NULL,
    description     varchar(1000) NOT NULL
                    CHECK (char_length(description) >= 15),
    source          varchar(10) NOT NULL
                    CHECK (source IN ('phone', 'mobile', 'web')),
    status          request_status NOT NULL DEFAULT 'new',
    created_at      timestamptz NOT NULL DEFAULT app_now(),
    deadline_at     timestamptz NOT NULL,
    closed_at       timestamptz,
    registered_by   integer REFERENCES employee (id),
    gis_exported_at timestamptz,
    CHECK (closed_at IS NULL OR closed_at >= created_at)
);
CREATE TABLE status_history (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    request_id  integer NOT NULL
                REFERENCES request (id) ON DELETE CASCADE,
    status      request_status NOT NULL,
    changed_at  timestamptz NOT NULL DEFAULT app_now(),
    employee_id integer REFERENCES employee (id)
);

-- наряды-допуски, материалы и оценки
CREATE TABLE work_order (
    id              integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    request_id      integer NOT NULL UNIQUE
                    REFERENCES request (id) ON DELETE CASCADE,
    master_id       integer NOT NULL REFERENCES employee (id),
    work_type       varchar(100) NOT NULL,
    safety_measures varchar(500),
    planned_start   timestamptz,
    planned_end     timestamptz,
    started_at      timestamptz,
    finished_at     timestamptz,
    CHECK (planned_end > planned_start),
    CHECK (finished_at >= started_at)
);
CREATE TABLE material (
    id        integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    erp_code  varchar(11) NOT NULL UNIQUE,
    name      varchar(100) NOT NULL,
    unit      varchar(10) NOT NULL,
    stock_qty numeric(10, 3) NOT NULL DEFAULT 0
              CHECK (stock_qty >= 0),
    price     numeric(10, 2) NOT NULL CHECK (price >= 0),
    synced_at timestamptz
);
CREATE TABLE material_usage (
    id            integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    work_order_id integer NOT NULL
                  REFERENCES work_order (id) ON DELETE CASCADE,
    material_id   integer NOT NULL REFERENCES material (id),
    quantity      numeric(10, 3) NOT NULL CHECK (quantity > 0),
    unit_price    numeric(10, 2) NOT NULL CHECK (unit_price >= 0),
    approval      varchar(10) NOT NULL DEFAULT 'pending'
                  CHECK (approval IN
                         ('pending', 'approved', 'rejected')),
    approved_by   integer REFERENCES employee (id),
    approved_at   timestamptz,
    UNIQUE (work_order_id, material_id)
);
CREATE TABLE rating (
    request_id integer PRIMARY KEY
               REFERENCES request (id) ON DELETE CASCADE,
    score      smallint NOT NULL CHECK (score BETWEEN 1 AND 5),
    comment    varchar(500),
    created_at timestamptz NOT NULL DEFAULT app_now()
);

-- индексы по внешним ключам и частым условиям отбора
CREATE INDEX ix_employee_role     ON employee (role_id);
CREATE INDEX ix_premise_house     ON premise (house_id);
CREATE INDEX ix_resident_premise  ON resident (premise_id);
CREATE INDEX ix_request_premise   ON request (premise_id);
CREATE INDEX ix_request_status    ON request (status, deadline_at);
CREATE INDEX ix_history_request   ON status_history (request_id);
CREATE INDEX ix_work_order_master ON work_order (master_id);
CREATE INDEX ix_usage_material    ON material_usage (material_id);
