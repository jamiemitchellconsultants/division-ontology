CREATE TABLE workspace_projection.embedding_profiles (
    profile_id text PRIMARY KEY CHECK (profile_id ~ '^[0-9a-f]{64}$'),
    body jsonb NOT NULL
);
CREATE TABLE workspace_projection.search_state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    active_profile_id text REFERENCES workspace_projection.embedding_profiles,
    desired_profile_id text REFERENCES workspace_projection.embedding_profiles
);
INSERT INTO workspace_projection.search_state (singleton) VALUES (true);

CREATE TABLE workspace_projection.embedding_jobs (
    job_id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    generation uuid NOT NULL,
    profile_id text NOT NULL REFERENCES workspace_projection.embedding_profiles,
    scope text NOT NULL,
    record_key text NOT NULL,
    revision integer NOT NULL,
    contribution_id uuid,
    input_text text NOT NULL,
    lease_id uuid,
    lease_until timestamptz,
    available_at timestamptz NOT NULL DEFAULT now(),
    embedding vector,
    UNIQUE (generation, profile_id, scope, record_key, revision)
);
CREATE INDEX embedding_jobs_pending
    ON workspace_projection.embedding_jobs (generation, available_at, job_id) WHERE embedding IS NULL;
ALTER TABLE workspace_projection.snapshots ADD COLUMN index_requested boolean NOT NULL DEFAULT true;

CREATE FUNCTION workspace_journal.lock_head() RETURNS bigint
LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
AS $body$
    SELECT position FROM workspace_journal.head WHERE singleton FOR UPDATE
$body$;
REVOKE EXECUTE ON FUNCTION workspace_journal.lock_head() FROM PUBLIC;
GRANT USAGE ON SCHEMA workspace_journal TO workspace_indexer;
GRANT EXECUTE ON FUNCTION workspace_journal.lock_head() TO workspace_api, workspace_publisher, workspace_indexer;

GRANT SELECT ON workspace_projection.embedding_profiles, workspace_projection.search_state,
    workspace_projection.embedding_jobs TO workspace_api, workspace_publisher, workspace_indexer;
GRANT INSERT ON workspace_projection.embedding_profiles TO workspace_indexer;
GRANT UPDATE ON workspace_projection.search_state TO workspace_indexer;
GRANT INSERT, UPDATE ON workspace_projection.embedding_jobs TO workspace_indexer;
GRANT USAGE ON SEQUENCE workspace_projection.embedding_jobs_job_id_seq TO workspace_indexer;
GRANT UPDATE (index_requested) ON workspace_projection.snapshots TO workspace_publisher;
