BEGIN;

CREATE TABLE IF NOT EXISTS learner_events (
    recorded_sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    event_id uuid PRIMARY KEY,
    event_type varchar(100) NOT NULL,
    event_version integer NOT NULL CHECK (event_version > 0),
    occurred_at timestamptz NOT NULL,
    recorded_at timestamptz NOT NULL,
    tenant_id uuid NOT NULL,
    learner_id uuid NOT NULL,
    session_id uuid NOT NULL,
    stream_version bigint NOT NULL CHECK (stream_version > 0),
    content_node_id varchar(20) NOT NULL,
    content_revision varchar(64) NOT NULL,
    correlation_id uuid NOT NULL,
    causation_id uuid NULL,
    idempotency_key varchar(128) NOT NULL,
    actor_type varchar(32) NOT NULL,
    actor_id varchar(256) NOT NULL,
    payload jsonb NOT NULL,
    CONSTRAINT uq_learner_events_tenant_idempotency
        UNIQUE (tenant_id, idempotency_key)
);

CREATE INDEX IF NOT EXISTS ix_learner_events_stream
    ON learner_events (
        tenant_id,
        learner_id,
        session_id,
        recorded_at,
        event_id
    );

CREATE UNIQUE INDEX IF NOT EXISTS uq_learner_events_stream_version
    ON learner_events (
        tenant_id,
        learner_id,
        session_id,
        stream_version
    );

CREATE INDEX IF NOT EXISTS ix_learner_events_node
    ON learner_events (
        tenant_id,
        learner_id,
        content_node_id,
        recorded_at,
        event_id
    );

CREATE TABLE IF NOT EXISTS outbox_messages (
    message_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    event_id uuid NOT NULL UNIQUE REFERENCES learner_events(event_id) ON DELETE RESTRICT,
    topic varchar(200) NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL,
    available_at timestamptz NOT NULL,
    claimed_at timestamptz NULL,
    claim_owner varchar(128) NULL,
    published_at timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_error varchar(2000) NULL
);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_available
    ON outbox_messages (published_at, available_at);

CREATE TABLE IF NOT EXISTS projection_checkpoints (
    projection_name varchar(100) NOT NULL,
    partition_key varchar(200) NOT NULL,
    last_recorded_at timestamptz NULL,
    last_event_id uuid NULL,
    failure_event_id uuid NULL,
    failure_count integer NOT NULL DEFAULT 0 CHECK (failure_count >= 0),
    last_error varchar(2000) NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (projection_name, partition_key)
);

CREATE TABLE IF NOT EXISTS processed_projection_events (
    projection_name varchar(100) NOT NULL,
    event_id uuid NOT NULL,
    processed_at timestamptz NOT NULL,
    PRIMARY KEY (projection_name, event_id)
);

CREATE TABLE IF NOT EXISTS s083_progress (
    tenant_id uuid NOT NULL,
    learner_id uuid NOT NULL,
    session_id uuid NOT NULL,
    content_revision varchar(64) NOT NULL,
    state varchar(64) NOT NULL,
    criterion_reveal_allowed boolean NOT NULL,
    paid_proposal_improvement_allowed boolean NOT NULL,
    support_used jsonb NOT NULL DEFAULT '[]'::jsonb,
    projection_version bigint NOT NULL,
    last_event_id uuid NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, learner_id, session_id)
);

CREATE TABLE IF NOT EXISTS due_retrievals (
    tenant_id uuid NOT NULL,
    learner_id uuid NOT NULL,
    session_id uuid NOT NULL,
    content_node_id varchar(20) NOT NULL,
    source_event_id uuid NOT NULL REFERENCES learner_events(event_id) ON DELETE RESTRICT,
    due_at timestamptz NOT NULL,
    completed_event_id uuid NULL REFERENCES learner_events(event_id) ON DELETE RESTRICT,
    PRIMARY KEY (tenant_id, learner_id, session_id, source_event_id)
);

CREATE INDEX IF NOT EXISTS ix_due_retrievals_pending
    ON due_retrievals (completed_event_id, due_at);

COMMENT ON TABLE learner_events IS
    'Append-only canonical learner facts. Corrections append new events.';

CREATE OR REPLACE FUNCTION reject_learner_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION
        'learner_events is append-only; append a correction event instead';
END;
$$;

DROP TRIGGER IF EXISTS learner_events_append_only ON learner_events;
CREATE TRIGGER learner_events_append_only
    BEFORE UPDATE OR DELETE ON learner_events
    FOR EACH ROW
    EXECUTE FUNCTION reject_learner_event_mutation();

REVOKE UPDATE, DELETE, TRUNCATE ON learner_events FROM PUBLIC;

COMMIT;
