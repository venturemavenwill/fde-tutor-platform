import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const migration = await readFile(
  new URL('../../../infra/db/migrations/0001_learner_events.sql', import.meta.url),
  'utf8',
)
const userMigration = await readFile(
  new URL('../../../infra/db/migrations/0002_platform_users.sql', import.meta.url),
  'utf8',
)

test('canonical events have a database sequence and append-only trigger', () => {
  assert.match(migration, /recorded_sequence bigint GENERATED ALWAYS AS IDENTITY/i)
  assert.match(migration, /stream_version bigint NOT NULL/i)
  assert.match(migration, /uq_learner_events_stream_version/i)
  assert.match(migration, /CREATE TRIGGER learner_events_append_only/i)
  assert.match(migration, /BEFORE UPDATE OR DELETE ON learner_events/i)
})

test('event and outbox records are transactionally linkable', () => {
  assert.match(
    migration,
    /event_id uuid NOT NULL UNIQUE REFERENCES learner_events\(event_id\) ON DELETE RESTRICT/i,
  )
  assert.match(
    migration,
    /CONSTRAINT uq_learner_events_tenant_idempotency\s+UNIQUE \(tenant_id, idempotency_key\)/i,
  )
})

test('projection state is replayable and exposes poison state', () => {
  assert.match(migration, /CREATE TABLE IF NOT EXISTS processed_projection_events/i)
  assert.match(migration, /failure_event_id uuid NULL/i)
  assert.match(migration, /last_error varchar\(2000\) NULL/i)
  assert.doesNotMatch(migration, /mastery/i)
  assert.doesNotMatch(migration, /entrustment/i)
})

test('the user directory is tenant keyed and does not make email an identity key', () => {
  assert.match(userMigration, /CREATE TABLE IF NOT EXISTS platform_users/i)
  assert.match(userMigration, /PRIMARY KEY \(tenant_id, object_id\)/i)
  assert.match(userMigration, /roles jsonb NOT NULL/i)
  assert.doesNotMatch(userMigration, /email/i)
})
