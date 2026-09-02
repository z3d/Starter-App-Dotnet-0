-- Three indexes that a neighbouring index already serves. PostgreSQL answers a prefix lookup from
-- the wider index, and the outbox one shares its partial predicate with ix_outbox_messages_claimable
-- while its leading column is pinned to NULL by that very predicate. Each cost an extra entry on
-- every insert to the busiest write-path tables and served no query in the API or reporting pack.
-- Named indexes make the drop deterministic (data-access skill, constraint naming).

DROP INDEX IF EXISTS ix_customers_tenant_id_owner_subject;   -- prefix of UNIQUE ix_customers_tenant_id_owner_subject_email
DROP INDEX IF EXISTS ix_orders_tenant_id_owner_subject;      -- prefix of ix_orders_tenant_id_owner_subject_customer_id
DROP INDEX IF EXISTS ix_outbox_messages_unprocessed;         -- subset of ix_outbox_messages_claimable
