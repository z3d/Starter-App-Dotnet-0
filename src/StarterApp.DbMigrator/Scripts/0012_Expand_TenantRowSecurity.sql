-- The application roles are cluster-wide, so another database on the same server may already have made them.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'starterapp_app') THEN
        CREATE ROLE starterapp_app NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'starterapp_worker') THEN
        CREATE ROLE starterapp_worker NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END
$$;

-- An order's items carry its tenant, so the policy needs no join.
ALTER TABLE order_items
    ADD COLUMN tenant_id varchar(100) NULL;

UPDATE order_items AS item
SET tenant_id = parent.tenant_id
FROM orders AS parent
WHERE parent.id = item.order_id AND item.tenant_id IS NULL;

CREATE POLICY tenant_isolation_customers ON customers
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

CREATE POLICY tenant_isolation_products ON products
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

CREATE POLICY tenant_isolation_orders ON orders
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

CREATE POLICY tenant_isolation_order_items ON order_items
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

CREATE POLICY tenant_isolation_idempotency_records ON idempotency_records
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

CREATE POLICY tenant_isolation_tenant_settings ON tenant_settings
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), ''));

-- The retention timer deletes expired keys of every tenant, and only the worker role may.
CREATE POLICY retention_read_idempotency_records ON idempotency_records
    FOR SELECT TO starterapp_worker
    USING (true);

CREATE POLICY retention_delete_idempotency_records ON idempotency_records
    FOR DELETE TO starterapp_worker
    USING (true);
