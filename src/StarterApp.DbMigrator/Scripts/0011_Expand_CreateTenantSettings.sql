CREATE TABLE tenant_settings (
    tenant_id varchar(100) NOT NULL,
    palette varchar(32) NOT NULL,
    updated_by varchar(200) NOT NULL,
    date_created timestamptz NOT NULL CONSTRAINT df_tenant_settings_date_created DEFAULT now(),
    last_updated timestamptz NOT NULL CONSTRAINT df_tenant_settings_last_updated DEFAULT now(),
    CONSTRAINT pk_tenant_settings PRIMARY KEY (tenant_id)
);
