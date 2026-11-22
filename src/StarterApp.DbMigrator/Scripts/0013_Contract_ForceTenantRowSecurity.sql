-- completes: 0012_Expand_TenantRowSecurity.sql
-- From here a connection that has not bound a tenant sees no tenant rows, the tables' owner included.

UPDATE order_items AS item
SET tenant_id = parent.tenant_id
FROM orders AS parent
WHERE parent.id = item.order_id AND item.tenant_id IS NULL;

ALTER TABLE order_items ALTER COLUMN tenant_id SET NOT NULL;

ALTER TABLE customers ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE products ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE orders ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE order_items ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE idempotency_records ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
ALTER TABLE tenant_settings ENABLE ROW LEVEL SECURITY, FORCE ROW LEVEL SECURITY;
