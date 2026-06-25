-- Owner-scoped data seed for the DAST (OWASP ZAP) gate (dast/run-dast.sh).
--
-- NOT a DbUp migration — it lives outside src/StarterApp.DbMigrator/Scripts on
-- purpose and must never run against a real environment. It loads a MODEST
-- volume of rows (this is a security scan, not a load test) owned by the ZAP
-- gateway identity (dast-user-01 / dast-tenant-01 — the identity injected by
-- the replacer job in dast/automation.yaml) so by-id and list probes return
-- real rows. Without it the DB is empty, every probe returns 404/empty, and
-- response-differential detection (SQLi, traversal) is blind to whether an
-- injection actually changed anything.
--
-- It ALSO seeds a SECOND owner (dast-user-02 / dast-tenant-02) with a handful
-- of rows at FIXED, KNOWN ids so run-dast.sh can run a scripted IDOR / owner-
-- scope regression probe: fetch/mutate owner-02's resources while presenting
-- owner-01's identity and assert the API hides them (404/empty) or forbids the
-- mutation (403). A handler that dropped its IOwnerOnlyPolicy check would leak
-- the row or accept the mutation, which the probe turns into a build failure.
--
-- Idempotent: every block is guarded (ON CONFLICT / sentinel NOT EXISTS / fixed
-- ids with ON CONFLICT DO NOTHING), so re-running adds nothing.
--
-- Column names/types mirror src/StarterApp.DbMigrator/Scripts/0001 (and the
-- owner-column DEFAULTs were dropped in 0004, so owner_subject/tenant_id MUST be
-- supplied explicitly on every insert — a bare insert now fails with 23502).

-- ============================================================================
-- Owner 1 (dast-user-01 / dast-tenant-01) — the scanned identity.
-- ~50 customers / ~50 products / ~50 orders, one line item each.
-- ============================================================================

-- 50 customers; conflict target is the owner-scoped unique email index.
INSERT INTO customers (name, email, owner_subject, tenant_id)
SELECT 'DAST Seed Customer ' || i,
       'dast-seed-' || i || '@zap.test',
       'dast-user-01',
       'dast-tenant-01'
FROM generate_series(1, 50) AS i
ON CONFLICT (tenant_id, owner_subject, email) DO NOTHING;

-- 50 products; no natural key, so a sentinel row guards the whole block. Stock
-- is generous so an order-placement probe never depletes a product mid-scan.
INSERT INTO products (name, description, price_amount, price_currency, stock, owner_subject, tenant_id)
SELECT 'DAST Seed Product ' || i,
       'Seeded product for the ZAP DAST scan',
       (10 + (i % 90))::numeric(18, 2),
       'USD',
       100000,
       'dast-user-01',
       'dast-tenant-01'
FROM generate_series(1, 50) AS i
WHERE NOT EXISTS (
    SELECT 1
    FROM products
    WHERE owner_subject = 'dast-user-01'
      AND tenant_id = 'dast-tenant-01'
      AND name = 'DAST Seed Product 1'
);

-- 50 orders spread across the seeded customers with mixed statuses and a 30-day
-- date spread. Status strings must match the OrderStatus enum exactly.
WITH seed_customers AS (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM customers
    WHERE owner_subject = 'dast-user-01'
      AND tenant_id = 'dast-tenant-01'
      AND email LIKE 'dast-seed-%'
),
customer_count AS (
    SELECT count(*) AS cnt FROM seed_customers
)
INSERT INTO orders (id, customer_id, order_date, status, owner_subject, tenant_id)
SELECT gen_random_uuid(),
       sc.id,
       now() - ((i % 30) || ' days')::interval,
       (ARRAY['Pending', 'Confirmed', 'Shipped', 'Delivered'])[1 + (i % 4)],
       'dast-user-01',
       'dast-tenant-01'
FROM generate_series(1, 50) AS i
JOIN customer_count cc ON true
JOIN seed_customers sc ON sc.rn = 1 + (i % cc.cnt)
WHERE NOT EXISTS (
    SELECT 1
    FROM orders o
    JOIN customers c ON c.id = o.customer_id
    WHERE o.owner_subject = 'dast-user-01'
      AND c.email LIKE 'dast-seed-%'
);

-- One line item per seeded order (quantity 1-3), product round-robin.
WITH seed_products AS (
    SELECT id, name, price_amount, price_currency, row_number() OVER (ORDER BY id) AS rn
    FROM products
    WHERE owner_subject = 'dast-user-01'
      AND tenant_id = 'dast-tenant-01'
      AND name LIKE 'DAST Seed Product %'
),
product_count AS (
    SELECT count(*) AS cnt FROM seed_products
),
seed_orders AS (
    SELECT o.id, row_number() OVER (ORDER BY o.id) AS rn
    FROM orders o
    JOIN customers c ON c.id = o.customer_id
    WHERE o.owner_subject = 'dast-user-01'
      AND c.email LIKE 'dast-seed-%'
      AND NOT EXISTS (SELECT 1 FROM order_items oi WHERE oi.order_id = o.id)
)
INSERT INTO order_items (order_id, product_id, product_name, quantity, unit_price_excluding_gst, currency, gst_rate)
SELECT so.id,
       sp.id,
       sp.name,
       1 + (so.rn % 3),
       sp.price_amount,
       sp.price_currency,
       0.1000
FROM seed_orders so
JOIN product_count pc ON true
JOIN seed_products sp ON sp.rn = 1 + (so.rn % pc.cnt);

-- ============================================================================
-- Owner 2 (dast-user-02 / dast-tenant-02) — the IDOR target.
--
-- Inserted at FIXED, KNOWN ids so run-dast.sh's cross-owner curl probe can
-- reference them without parsing the DB. Customers/products use integer IDENTITY
-- columns; we override the generated value with OVERRIDING SYSTEM VALUE and pick
-- ids in a high range (900000+) that the IDENTITY sequence will not reach during
-- a scan, so there is no collision with owner-01's auto-assigned ids. The order
-- uses a fixed sentinel guid so the by-id order probe has a deterministic target.
--
-- These rows must stay INVISIBLE to owner-01: the probe asserts a cross-owner
-- GET returns 404/empty and a cross-owner DELETE returns 403.
-- ============================================================================

-- Fixed-id customer owned by owner-02.
INSERT INTO customers (id, name, email, owner_subject, tenant_id)
OVERRIDING SYSTEM VALUE
VALUES (900001, 'DAST Cross-Owner Customer', 'dast-crossowner@zap.test', 'dast-user-02', 'dast-tenant-02')
ON CONFLICT (id) DO NOTHING;

-- Fixed-id product owned by owner-02.
INSERT INTO products (id, name, description, price_amount, price_currency, stock, owner_subject, tenant_id)
OVERRIDING SYSTEM VALUE
VALUES (900001, 'DAST Cross-Owner Product', 'Owner-02 product — must be hidden from owner-01', 42.00, 'USD', 100, 'dast-user-02', 'dast-tenant-02')
ON CONFLICT (id) DO NOTHING;

-- Fixed-guid order owned by owner-02, attached to owner-02's customer.
INSERT INTO orders (id, customer_id, order_date, status, owner_subject, tenant_id)
VALUES ('00000000-0000-0000-0000-0000dad70002', 900001, now(), 'Pending', 'dast-user-02', 'dast-tenant-02')
ON CONFLICT (id) DO NOTHING;

-- Row-count summary so the seeding step's output shows what the scan sees.
SELECT 'owner1.customers' AS entity, count(*) AS seeded
FROM customers
WHERE owner_subject = 'dast-user-01' AND email LIKE 'dast-seed-%'
UNION ALL
SELECT 'owner1.products', count(*)
FROM products
WHERE owner_subject = 'dast-user-01' AND name LIKE 'DAST Seed Product %'
UNION ALL
SELECT 'owner1.orders', count(*)
FROM orders
WHERE owner_subject = 'dast-user-01'
UNION ALL
SELECT 'owner1.order_items', count(*)
FROM order_items oi
JOIN orders o ON o.id = oi.order_id
WHERE o.owner_subject = 'dast-user-01'
UNION ALL
SELECT 'owner2.customers', count(*)
FROM customers
WHERE owner_subject = 'dast-user-02'
UNION ALL
SELECT 'owner2.products', count(*)
FROM products
WHERE owner_subject = 'dast-user-02'
UNION ALL
SELECT 'owner2.orders', count(*)
FROM orders
WHERE owner_subject = 'dast-user-02';
