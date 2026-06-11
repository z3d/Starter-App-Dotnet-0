-- Rows per tenant/owner per resource table. Aggregates ACROSS owners (operator view).
-- All three tables carry ix_*_tenant_id_owner_subject, so the group-bys are index-friendly.

SELECT 'customers' AS entity, tenant_id, owner_subject, count(*) AS rows
FROM customers
GROUP BY tenant_id, owner_subject
UNION ALL
SELECT 'products', tenant_id, owner_subject, count(*)
FROM products
GROUP BY tenant_id, owner_subject
UNION ALL
SELECT 'orders', tenant_id, owner_subject, count(*)
FROM orders
GROUP BY tenant_id, owner_subject
ORDER BY entity, rows DESC;
