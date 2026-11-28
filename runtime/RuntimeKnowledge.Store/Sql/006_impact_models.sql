CREATE TABLE workspace_projection.impact_models (
    generation uuid NOT NULL,
    snapshot_id text NOT NULL,
    package bytea NOT NULL,
    package_sha256 text NOT NULL CHECK (package_sha256 ~ '^[0-9a-f]{64}$'),
    PRIMARY KEY (generation, snapshot_id),
    FOREIGN KEY (generation, snapshot_id)
        REFERENCES workspace_projection.snapshots (generation, snapshot_id)
);
GRANT SELECT ON workspace_projection.impact_models TO workspace_api, workspace_publisher;
GRANT INSERT ON workspace_projection.impact_models TO workspace_publisher;
