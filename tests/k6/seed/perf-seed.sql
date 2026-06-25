-- Bulk data seed for the k6 performance gate (tests/k6/run-perf.sh).
--
-- NOT a DbUp migration — it lives outside src/StarterApp.DbMigrator/Scripts on
-- purpose and must never run against a real environment. It bulk-loads rows
-- owned by the k6 gateway identity (k6-user / k6-tenant) so list, pagination,
-- and index paths are exercised at realistic data volume instead of an
-- almost-empty database. PerfSeedScriptTests runs this file against the real
-- schema in CI, so schema drift breaks the build here, not the nightly run.
--
-- Idempotent: every block is guarded (ON CONFLICT / sentinel NOT EXISTS), so
-- re-running adds nothing.

-- 20k customers; conflict target is the owner-scoped unique email index.
INSERT INTO customers (name, email, owner_subject, tenant_id)
SELECT 'Perf Seed Customer ' || i,
       'perf-seed-' || i || '@k6.test',
       'k6-user',
       'k6-tenant'
FROM generate_series(1, 20000) AS i
ON CONFLICT (tenant_id, owner_subject, email) DO NOTHING;

-- 20k products; no natural key, so a sentinel row guards the whole block.
-- Stock mirrors the k6 setup convention (100k) so order placement never
-- depletes a seeded product mid-run.
INSERT INTO products (name, description, price_amount, price_currency, stock, owner_subject, tenant_id)
SELECT 'Perf Seed Product ' || i,
       'Bulk-seeded product for the k6 load gate',
       (10 + (i % 490))::numeric(18, 2),
       'USD',
       100000,
       'k6-user',
       'k6-tenant'
FROM generate_series(1, 20000) AS i
WHERE NOT EXISTS (
    SELECT 1
    FROM products
    WHERE owner_subject = 'k6-user'
      AND tenant_id = 'k6-tenant'
      AND name = 'Perf Seed Product 1'
);

-- 20k orders spread across the seeded customers with mixed statuses and a
-- 90-day date spread, so status- and date-indexed queries see realistic
-- selectivity. Status strings must match the OrderStatus enum exactly.
WITH seed_customers AS (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM customers
    WHERE owner_subject = 'k6-user'
      AND tenant_id = 'k6-tenant'
      AND email LIKE 'perf-seed-%'
),
customer_count AS (
    SELECT count(*) AS cnt FROM seed_customers
)
INSERT INTO orders (id, customer_id, order_date, status, owner_subject, tenant_id)
SELECT gen_random_uuid(),
       sc.id,
       now() - ((i % 90) || ' days')::interval,
       (ARRAY['Pending', 'Confirmed', 'Shipped', 'Delivered'])[1 + (i % 4)],
       'k6-user',
       'k6-tenant'
FROM generate_series(1, 20000) AS i
JOIN customer_count cc ON true
JOIN seed_customers sc ON sc.rn = 1 + (i % cc.cnt)
WHERE NOT EXISTS (
    SELECT 1
    FROM orders o
    JOIN customers c ON c.id = o.customer_id
    WHERE o.owner_subject = 'k6-user'
      AND c.email LIKE 'perf-seed-%'
);

-- One line item per seeded order (quantity 1-3), product round-robin.
WITH seed_products AS (
    SELECT id, name, price_amount, price_currency, row_number() OVER (ORDER BY id) AS rn
    FROM products
    WHERE owner_subject = 'k6-user'
      AND tenant_id = 'k6-tenant'
      AND name LIKE 'Perf Seed Product %'
),
product_count AS (
    SELECT count(*) AS cnt FROM seed_products
),
seed_orders AS (
    SELECT o.id, row_number() OVER (ORDER BY o.id) AS rn
    FROM orders o
    JOIN customers c ON c.id = o.customer_id
    WHERE o.owner_subject = 'k6-user'
      AND c.email LIKE 'perf-seed-%'
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

-- ---------------------------------------------------------------------------
-- Selectivity dimension: a few additional owner identities with a SMALL slice
-- of rows each, so owner_subject / tenant_id carry real cardinality and the
-- WHERE owner_scope predicate cost is actually measured. The k6 VUs still
-- authenticate as the primary k6-user (lib/config.js), so the primary
-- identity's data volume stays ~20k; these rows only widen the index domain.
--
-- IMPORTANT: these rows use DISTINCT name/email prefixes ('Alt Seed ...'),
-- never the 'perf-seed-%' / 'Perf Seed Product %' patterns the primary blocks
-- and PerfSeedScriptTests filter on. That keeps the test's >= 20000 primary
-- counts and its zero-foreign-owner assertion (which scans the primary
-- prefixes) intact while still adding cardinality. ~1000 customers + ~1000
-- products + ~1000 orders per identity across k6-user-2..k6-user-5.
INSERT INTO customers (name, email, owner_subject, tenant_id)
SELECT 'Alt Seed Customer ' || g.owner || '-' || i,
       'alt-seed-' || g.owner || '-' || i || '@k6.test',
       'k6-user-' || g.owner,
       'k6-tenant-' || g.owner
FROM generate_series(2, 5) AS g(owner)
CROSS JOIN generate_series(1, 1000) AS i
ON CONFLICT (tenant_id, owner_subject, email) DO NOTHING;

INSERT INTO products (name, description, price_amount, price_currency, stock, owner_subject, tenant_id)
SELECT 'Alt Seed Product ' || g.owner || '-' || i,
       'Alternate-owner product for k6 selectivity cardinality',
       (10 + (i % 490))::numeric(18, 2),
       'USD',
       100000,
       'k6-user-' || g.owner,
       'k6-tenant-' || g.owner
FROM generate_series(2, 5) AS g(owner)
CROSS JOIN generate_series(1, 1000) AS i
WHERE NOT EXISTS (
    SELECT 1
    FROM products
    WHERE owner_subject = 'k6-user-2'
      AND tenant_id = 'k6-tenant-2'
      AND name = 'Alt Seed Product 2-1'
);

-- ~1000 orders per alt identity, attached to that identity's own customers so
-- the owner-scope join stays consistent. Mixed statuses for index selectivity.
WITH alt_customers AS (
    SELECT id, owner_subject, tenant_id,
           row_number() OVER (PARTITION BY owner_subject ORDER BY id) AS rn
    FROM customers
    WHERE email LIKE 'alt-seed-%'
),
alt_counts AS (
    SELECT owner_subject, count(*) AS cnt FROM alt_customers GROUP BY owner_subject
)
INSERT INTO orders (id, customer_id, order_date, status, owner_subject, tenant_id)
SELECT gen_random_uuid(),
       ac.id,
       now() - ((i % 90) || ' days')::interval,
       (ARRAY['Pending', 'Confirmed', 'Shipped', 'Delivered'])[1 + (i % 4)],
       ac.owner_subject,
       ac.tenant_id
FROM generate_series(2, 5) AS g(owner)
CROSS JOIN generate_series(1, 1000) AS i
JOIN alt_counts c ON c.owner_subject = 'k6-user-' || g.owner
JOIN alt_customers ac ON ac.owner_subject = 'k6-user-' || g.owner
                     AND ac.rn = 1 + (i % c.cnt)
WHERE NOT EXISTS (
    SELECT 1
    FROM orders o
    JOIN customers cu ON cu.id = o.customer_id
    WHERE cu.email LIKE 'alt-seed-%'
);

-- Row-count summary so the seeding step's output shows what the run sees.
SELECT 'customers' AS entity, count(*) AS seeded
FROM customers
WHERE owner_subject = 'k6-user' AND email LIKE 'perf-seed-%'
UNION ALL
SELECT 'products', count(*)
FROM products
WHERE owner_subject = 'k6-user' AND name LIKE 'Perf Seed Product %'
UNION ALL
SELECT 'orders', count(*)
FROM orders o
WHERE o.owner_subject = 'k6-user'
UNION ALL
SELECT 'order_items', count(*)
FROM order_items oi
JOIN orders o ON o.id = oi.order_id
WHERE o.owner_subject = 'k6-user'
UNION ALL
SELECT 'alt_customers', count(*)
FROM customers
WHERE email LIKE 'alt-seed-%'
UNION ALL
SELECT 'alt_products', count(*)
FROM products
WHERE name LIKE 'Alt Seed Product %'
UNION ALL
SELECT 'alt_orders', count(*)
FROM orders o
JOIN customers c ON c.id = o.customer_id
WHERE c.email LIKE 'alt-seed-%';
