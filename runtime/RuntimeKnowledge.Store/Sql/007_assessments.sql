CREATE TABLE workspace_projection.assessments (
    generation uuid NOT NULL,
    assessment_id uuid NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    snapshot_id text NOT NULL CHECK (snapshot_id ~ '^[0-9a-f]{64}$'),
    request_id text NOT NULL,
    author_issuer text NOT NULL,
    author_subject text NOT NULL,
    saved_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    frozen bytea NOT NULL,
    markdown bytea NOT NULL,
    started_from uuid,
    PRIMARY KEY (generation, assessment_id)
);
CREATE INDEX assessments_newest
    ON workspace_projection.assessments (generation, saved_at DESC, assessment_id DESC);

CREATE TABLE workspace_projection.assessment_revisions (
    generation uuid NOT NULL,
    assessment_id uuid NOT NULL,
    version integer NOT NULL CHECK (version > 0),
    editor_issuer text NOT NULL,
    editor_subject text NOT NULL,
    edited_at timestamptz NOT NULL,
    frozen bytea NOT NULL,
    markdown bytea NOT NULL,
    PRIMARY KEY (generation, assessment_id, version)
);

GRANT SELECT, INSERT, UPDATE ON workspace_projection.assessments TO workspace_api, workspace_publisher;
GRANT SELECT, INSERT ON workspace_projection.assessment_revisions TO workspace_api, workspace_publisher;
