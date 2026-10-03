CREATE TABLE watched_jobs (
    job_name varchar(100) NOT NULL,
    watched_since_utc timestamptz NOT NULL,
    CONSTRAINT pk_watched_jobs PRIMARY KEY (job_name)
);
