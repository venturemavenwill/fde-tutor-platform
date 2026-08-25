import type {
  AccessConsole as AccessConsoleContract,
  AuthorizationDisposition,
  ObservedUser,
} from '../contracts'

type AccessConsoleProps = {
  access: AccessConsoleContract
  users: ObservedUser[]
  onClose: () => void
}

const dispositionLabels: Record<AuthorizationDisposition, string> = {
  Allow: 'Allowed',
  Deny: 'Denied',
  Deferred: 'Later phase',
  External: 'External authority',
}

export function AccessConsole({
  access,
  users,
  onClose,
}: AccessConsoleProps) {
  const isAdministrator = access.currentUser.roles.includes('Administrator')

  return (
    <section
      id="identity-access"
      className="access-console"
      aria-labelledby="identity-access-title"
    >
      <div className="access-console-heading">
        <div>
          <p className="step">G-02 · Identity and tenancy</p>
          <h2 id="identity-access-title">Identity and user access</h2>
        </div>
        <button type="button" className="secondary-button" onClick={onClose}>
          Close
        </button>
      </div>

      <p className="access-intro">
        Microsoft Entra app roles are the production assignment authority. This
        console shows effective access and tenant-scoped observed users; it
        cannot enrol users or change role assignments.
      </p>

      <div className="access-summary-grid">
        <section aria-labelledby="current-user-title">
          <h3 id="current-user-title">Current user</h3>
          <dl className="identity-list">
            <div>
              <dt>Provider</dt>
              <dd>
                {access.currentUser.authenticationMode}
                {access.currentUser.isSynthetic ? ' · synthetic' : ''}
              </dd>
            </div>
            <div>
              <dt>Tenant ID</dt>
              <dd>
                <code>{access.currentUser.tenantId}</code>
              </dd>
            </div>
            <div>
              <dt>Object ID</dt>
              <dd>
                <code>{access.currentUser.objectId}</code>
              </dd>
            </div>
            <div>
              <dt>Effective roles</dt>
              <dd className="role-list">
                {access.currentUser.roles.length > 0 ? (
                  access.currentUser.roles.map((role) => (
                    <span className="role-chip" key={role}>
                      {role}
                    </span>
                  ))
                ) : (
                  <strong className="access-denied">
                    No platform app role assigned
                  </strong>
                )}
              </dd>
            </div>
          </dl>
        </section>

        <section aria-labelledby="management-boundary-title">
          <h3 id="management-boundary-title">User management boundary</h3>
          <dl className="identity-list">
            <div>
              <dt>Assignment authority</dt>
              <dd>{access.userManagement.assignmentAuthority}</dd>
            </div>
            <div>
              <dt>Observed directory</dt>
              <dd>{access.userManagement.directoryMode}</dd>
            </div>
            <div>
              <dt>Role mutation</dt>
              <dd>Not available in this application</dd>
            </div>
            <div>
              <dt>Pilot enrolment</dt>
              <dd>Not open</dd>
            </div>
          </dl>
        </section>
      </div>

      {isAdministrator && (
        <section className="observed-users" aria-labelledby="observed-users-title">
          <h3 id="observed-users-title">Observed users in this tenant</h3>
          <p>
            This operational directory contains durable subjects and latest
            observed app roles only. It does not contain email or learner
            responses.
          </p>
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">Object ID</th>
                  <th scope="col">Roles</th>
                  <th scope="col">Provider</th>
                  <th scope="col">Last observed</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={`${user.tenantId}:${user.objectId}`}>
                    <td>
                      <code>{user.objectId}</code>
                    </td>
                    <td>{user.roles.join(', ') || 'No role'}</td>
                    <td>{user.authenticationMode}</td>
                    <td>{new Date(user.lastObservedAt).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <section className="authorization-matrix" aria-labelledby="matrix-title">
        <h3 id="matrix-title">Authorization matrix</h3>
        <p>
          <code>{access.matrixVersion}</code> is enforced for Phase 1. Later-phase
          cells are visible but do not expose runtime routes.
        </p>
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">Capability</th>
                {access.roles.map((role) => (
                  <th scope="col" key={role.id}>
                    {role.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {access.capabilities.map((capability) => (
                <tr key={capability.id}>
                  <th scope="row">
                    <span>{capability.label}</span>
                    <small>{capability.constraint}</small>
                  </th>
                  {access.roles.map((role) => {
                    const disposition = capability.access[role.id]
                    return (
                      <td key={role.id}>
                        <span
                          className={`access-disposition access-${disposition.toLowerCase()}`}
                        >
                          {dispositionLabels[disposition]}
                        </span>
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </section>
  )
}
