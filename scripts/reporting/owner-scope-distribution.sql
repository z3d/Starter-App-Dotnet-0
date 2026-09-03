-- Rows per tenant/owner per resource table. Aggregates ACROSS owners (operator view).
-- Every table has a btree led by (tenant_id, owner_subject): products' own index, and the wider
-- customer/order indexes that serve the prefix (0005 dropped the redundant two). Index-friendly group-bys.

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
