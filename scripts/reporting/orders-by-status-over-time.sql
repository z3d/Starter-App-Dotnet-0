-- Order flow per status per day (last 30 days). Aggregates ACROSS owners — this is an
-- operator view, not a tenant-facing one. ix_orders_order_date supports the range scan.

SELECT date_trunc('day', order_date) AS day, status, count(*) AS orders
FROM orders
WHERE order_date >= now() - interval '30 days'
GROUP BY 1, 2
ORDER BY 1 DESC, 2;

-- Orders stuck in a non-terminal status for more than 7 days
SELECT id, customer_id, status, order_date, last_updated
FROM orders
WHERE status IN ('Pending', 'Confirmed', 'Processing')
  AND last_updated < now() - interval '7 days'
ORDER BY last_updated;
