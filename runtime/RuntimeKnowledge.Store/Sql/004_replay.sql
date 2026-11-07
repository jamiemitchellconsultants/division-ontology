CREATE TABLE workspace_projection.rebuilds (
    generation uuid PRIMARY KEY,
    graph_name text NOT NULL UNIQUE CHECK (graph_name ~ '^workspace_g[0-9a-f]{32}$'),
    through_position bigint NOT NULL CHECK (through_position > 0),
    snapshot_id text NOT NULL,
    activation_position bigint NOT NULL,
    state_digest text NOT NULL CHECK (state_digest ~ '^[0-9a-f]{64}$')
);
