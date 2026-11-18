-- УК-Менеджмент: функции, триггеры и представление

-- 1 нормативный срок заявки по категории работ и приоритету
CREATE FUNCTION fn_request_deadline() RETURNS trigger AS $$
DECLARE
    h smallint;
BEGIN
    SELECT hours INTO h FROM sla_norm
     WHERE category_id = NEW.category_id
       AND priority = NEW.priority;
    IF h IS NULL THEN
        RAISE EXCEPTION 'Не задан норматив для категории % (%)',
            NEW.category_id, NEW.priority;
    END IF;
    NEW.deadline_at := NEW.created_at + make_interval(hours => h);
    RETURN NEW;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER tr_request_deadline
    BEFORE INSERT ON request
    FOR EACH ROW EXECUTE FUNCTION fn_request_deadline();

-- 2 журнал изменения статусов заявки
CREATE FUNCTION fn_request_history() RETURNS trigger AS $$
BEGIN
    INSERT INTO status_history (request_id, status, employee_id)
    VALUES (NEW.id, NEW.status,
            nullif(current_setting('app.employee_id', true),
                   '')::integer);
    IF NEW.status = 'closed' AND NEW.closed_at IS NULL THEN
        UPDATE request SET closed_at = app_now() WHERE id = NEW.id;
    END IF;
    RETURN NULL;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER tr_request_history
    AFTER INSERT OR UPDATE OF status ON request
    FOR EACH ROW EXECUTE FUNCTION fn_request_history();

-- 3 списание ТМЦ со склада при утверждении главным инженером
CREATE FUNCTION fn_usage_approve() RETURNS trigger AS $$
BEGIN
    IF NEW.approval = 'approved' AND OLD.approval <> 'approved' THEN
        UPDATE material
           SET stock_qty = stock_qty - NEW.quantity
         WHERE id = NEW.material_id;
        NEW.approved_at := app_now();
    END IF;
    RETURN NEW;
END $$ LANGUAGE plpgsql;

CREATE TRIGGER tr_usage_approve
    BEFORE UPDATE OF approval ON material_usage
    FOR EACH ROW EXECUTE FUNCTION fn_usage_approve();

-- 4 представление: заявки с остатком нормативного срока
CREATE VIEW v_request_sla AS
SELECT r.id,
       h.address || ', ' || p.number AS place,
       c.name AS category,
       r.priority,
       r.status,
       e.full_name AS master,
       r.deadline_at,
       r.status NOT IN ('done', 'closed')
           AND r.deadline_at < app_now() AS overdue
  FROM request r
  JOIN premise p        ON p.id = r.premise_id
  JOIN house h          ON h.id = p.house_id
  JOIN work_category c  ON c.id = r.category_id
  LEFT JOIN work_order w ON w.request_id = r.id
  LEFT JOIN employee e  ON e.id = w.master_id;
