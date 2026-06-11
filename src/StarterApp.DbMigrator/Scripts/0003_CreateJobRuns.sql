CREATE TABLE job_runs (
    id uuid NOT NULL,
    job_name varchar(100) NOT NULL,
    started_on_utc timestamptz NOT NULL,
    completed_on_utc timestamptz NULL,
    outcome varchar(20) NULL,
    summary text NULL,
    CONSTRAINT pk_job_runs PRIMARY KEY (id)
);

CREATE INDEX ix_job_runs_job_name_started_on_utc
    ON job_runs (job_name, started_on_utc DESC);
