-- The 'legacy-owner'/'legacy-tenant' column DEFAULTs were a one-time backfill aid for
-- pre-ownership rows. The application has stamped ownership explicitly on every insert
-- since the owner-scoping work landed, so the defaults' only remaining effect is a
-- footgun: raw SQL that forgets the owner columns silently creates rows owned by a
-- placeholder identity instead of failing. Drop them so such inserts fail with 23502.

ALTER TABLE customers
    ALTER COLUMN owner_subject DROP DEFAULT,
    ALTER COLUMN tenant_id DROP DEFAULT;

ALTER TABLE products
    ALTER COLUMN owner_subject DROP DEFAULT,
    ALTER COLUMN tenant_id DROP DEFAULT;

ALTER TABLE orders
    ALTER COLUMN owner_subject DROP DEFAULT,
    ALTER COLUMN tenant_id DROP DEFAULT;
