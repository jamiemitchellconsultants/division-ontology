CREATE TABLE workspace_projection.contributions (
    generation uuid NOT NULL,
    contribution_id uuid NOT NULL,
    revision integer NOT NULL CHECK (revision > 0),
    kind text NOT NULL CHECK (kind IN ('annotation', 'relationship')),
    removed boolean NOT NULL,
    target_key text,
    body jsonb NOT NULL,
    PRIMARY KEY (generation, contribution_id),
    CHECK ((kind = 'annotation') = (target_key IS NOT NULL))
);
CREATE INDEX active_annotations
    ON workspace_projection.contributions (generation, target_key)
    WHERE kind = 'annotation' AND NOT removed;

CREATE TABLE workspace_projection.contribution_index_work (
    generation uuid NOT NULL,
    contribution_id uuid NOT NULL,
    revision integer NOT NULL,
    PRIMARY KEY (generation, contribution_id, revision)
);

GRANT SELECT ON workspace_projection.contributions TO workspace_api, workspace_publisher, workspace_indexer;
GRANT INSERT, UPDATE ON workspace_projection.contributions TO workspace_api;
GRANT SELECT, INSERT ON workspace_projection.contribution_index_work TO workspace_api;
GRANT SELECT, DELETE ON workspace_projection.contribution_index_work TO workspace_indexer;
CREATE INDEX journal_activations ON workspace_journal.events (position DESC)
    WHERE event_type = 'snapshotActivated';
CREATE INDEX journal_by_contribution ON workspace_journal.events (aggregate_id, position)
    WHERE aggregate_id IS NOT NULL;
