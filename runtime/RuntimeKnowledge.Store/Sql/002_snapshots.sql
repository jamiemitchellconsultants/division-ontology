CREATE TABLE workspace_projection.state (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    generation uuid NOT NULL,
    graph_name text NOT NULL CHECK (graph_name ~ '^workspace_g[0-9a-f]{1,32}$'),
    snapshot_id text,
    activation_position bigint,
    writes_paused boolean NOT NULL DEFAULT false
);
INSERT INTO workspace_projection.state (generation, graph_name)
VALUES ('00000000-0000-0000-0000-000000000001', 'workspace_g1');

CREATE TABLE workspace_projection.snapshots (
    generation uuid NOT NULL,
    snapshot_id text NOT NULL CHECK (snapshot_id ~ '^[0-9a-f]{64}$'),
    manifest jsonb NOT NULL,
    gaps jsonb NOT NULL,
    PRIMARY KEY (generation, snapshot_id)
);
CREATE TABLE workspace_projection.vertices (
    generation uuid NOT NULL,
    record_key text COLLATE "C" NOT NULL,
    PRIMARY KEY (generation, record_key)
);
CREATE TABLE workspace_projection.published_records (
    generation uuid NOT NULL,
    snapshot_id text NOT NULL,
    record_key text COLLATE "C" NOT NULL,
    kind text NOT NULL,
    body jsonb NOT NULL,
    PRIMARY KEY (generation, snapshot_id, record_key),
    FOREIGN KEY (generation, snapshot_id)
        REFERENCES workspace_projection.snapshots (generation, snapshot_id)
);
CREATE INDEX published_records_kind
    ON workspace_projection.published_records (generation, snapshot_id, kind, record_key);

GRANT SELECT ON workspace_projection.state TO workspace_api, workspace_publisher, workspace_indexer;
GRANT UPDATE (snapshot_id, activation_position) ON workspace_projection.state TO workspace_publisher;
GRANT SELECT ON workspace_projection.snapshots, workspace_projection.published_records
    TO workspace_api, workspace_publisher, workspace_indexer;
GRANT INSERT ON workspace_projection.snapshots, workspace_projection.vertices,
    workspace_projection.published_records TO workspace_publisher;
GRANT SELECT ON workspace_projection.vertices TO workspace_publisher;
