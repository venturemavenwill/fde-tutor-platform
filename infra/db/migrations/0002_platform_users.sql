BEGIN;

CREATE TABLE IF NOT EXISTS platform_users (
    tenant_id uuid NOT NULL,
    object_id uuid NOT NULL,
    external_subject varchar(80) NOT NULL,
    authentication_mode varchar(32) NOT NULL,
    roles jsonb NOT NULL DEFAULT '[]'::jsonb,
    first_observed_at timestamptz NOT NULL,
    last_observed_at timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, object_id),
    CONSTRAINT ck_platform_users_roles_array
        CHECK (jsonb_typeof(roles) = 'array')
);

CREATE INDEX IF NOT EXISTS ix_platform_users_tenant_last_observed
    ON platform_users (tenant_id, last_observed_at DESC, object_id);

COMMENT ON TABLE platform_users IS
    'Tenant-scoped observed Entra subjects and app roles. Entra remains the role-assignment authority.';

REVOKE TRUNCATE ON platform_users FROM PUBLIC;

COMMIT;
