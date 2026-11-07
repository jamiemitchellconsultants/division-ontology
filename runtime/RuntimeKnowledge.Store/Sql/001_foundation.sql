CREATE EXTENSION IF NOT EXISTS age;
CREATE EXTENSION IF NOT EXISTS vector WITH SCHEMA public;

DO $roles$
BEGIN
    BEGIN
        CREATE ROLE workspace_api NOLOGIN;
    EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL;
    END;
    BEGIN
        CREATE ROLE workspace_publisher NOLOGIN;
    EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL;
    END;
    BEGIN
        CREATE ROLE workspace_indexer NOLOGIN;
    EXCEPTION WHEN duplicate_object OR unique_violation THEN NULL;
    END;
END
$roles$;

CREATE SCHEMA workspace_journal;
CREATE SCHEMA workspace_projection;

CREATE TABLE workspace_journal.head (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    position bigint NOT NULL CHECK (position >= 0)
);
INSERT INTO workspace_journal.head (position) VALUES (0);

CREATE FUNCTION workspace_journal.next_position() RETURNS bigint
LANGUAGE sql VOLATILE
AS $body$
    UPDATE workspace_journal.head
        SET position = position + 1
        WHERE singleton
        RETURNING position
$body$;

CREATE TABLE workspace_journal.events (
    position bigint PRIMARY KEY CHECK (position > 0),
    event_id uuid NOT NULL UNIQUE,
    event_type text NOT NULL CHECK (event_type <> ''),
    schema_version integer NOT NULL CHECK (schema_version > 0),
    actor_issuer text,
    actor_subject text,
    occurred_at timestamptz NOT NULL,
    snapshot_id text CHECK (snapshot_id ~ '^[a-f0-9]{64}$'),
    aggregate_id uuid,
    aggregate_revision integer CHECK (aggregate_revision > 0),
    payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
    CHECK ((actor_issuer IS NULL) = (actor_subject IS NULL)),
    CHECK ((aggregate_id IS NULL) = (aggregate_revision IS NULL)),
    UNIQUE (aggregate_id, aggregate_revision)
);

SELECT ag_catalog.create_graph('workspace_g1');
SELECT ag_catalog.create_vlabel('workspace_g1', 'Entity');
SELECT ag_catalog.create_elabel('workspace_g1', 'Relationship');

GRANT USAGE ON SCHEMA workspace_journal TO workspace_api, workspace_publisher;
GRANT SELECT, INSERT ON workspace_journal.events TO workspace_api, workspace_publisher;
GRANT SELECT, UPDATE (position) ON workspace_journal.head TO workspace_api, workspace_publisher;
REVOKE EXECUTE ON FUNCTION workspace_journal.next_position() FROM PUBLIC;
GRANT EXECUTE ON FUNCTION workspace_journal.next_position() TO workspace_api, workspace_publisher;
GRANT USAGE ON SCHEMA workspace_projection TO workspace_api, workspace_publisher, workspace_indexer;
GRANT USAGE ON SCHEMA ag_catalog, workspace_g1 TO workspace_api, workspace_publisher;
GRANT SELECT ON ag_catalog.ag_graph, ag_catalog.ag_label TO workspace_api, workspace_publisher;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA workspace_g1 TO workspace_api, workspace_publisher;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA workspace_g1 TO workspace_api, workspace_publisher;
