import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser'

export type AuthHeaders = Record<string, string>
export type AuthContext = {
  headers: AuthHeaders
  identityKey: string
}

const mode = import.meta.env.VITE_AUTH_MODE ?? 'development'
const developmentTenant =
  import.meta.env.VITE_DEVELOPMENT_TENANT_ID ??
  '11111111-1111-1111-1111-111111111111'
const developmentObject =
  import.meta.env.VITE_DEVELOPMENT_OBJECT_ID ??
  '22222222-2222-2222-2222-222222222222'

let msalPromise: Promise<PublicClientApplication> | undefined
let activeAccount: AccountInfo | undefined
let interactionPromise: Promise<never> | undefined

async function getMsal(): Promise<PublicClientApplication> {
  if (msalPromise) {
    return msalPromise
  }

  msalPromise = initializeMsal()
  return msalPromise
}

async function initializeMsal(): Promise<PublicClientApplication> {
  const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID
  const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID
  const redirectUri = import.meta.env.VITE_ENTRA_REDIRECT_URI ?? window.location.origin
  if (!clientId || !tenantId) {
    throw new Error(
      'VITE_ENTRA_CLIENT_ID and VITE_ENTRA_TENANT_ID are required in Entra mode.',
    )
  }

  const application = new PublicClientApplication({
    auth: {
      clientId,
      authority: `https://login.microsoftonline.com/${tenantId}`,
      redirectUri,
    },
    cache: {
      cacheLocation: 'sessionStorage',
    },
  })
  await application.initialize()
  const redirectResult = await application.handleRedirectPromise()
  activeAccount = redirectResult?.account ?? application.getAllAccounts()[0]
  if (activeAccount) {
    application.setActiveAccount(activeAccount)
  }
  return application
}

export async function getAuthContext(): Promise<AuthContext> {
  if (mode === 'development') {
    return {
      headers: {
        'X-Fde-Tenant-Id': developmentTenant,
        'X-Fde-Object-Id': developmentObject,
      },
      identityKey: `${developmentTenant}:${developmentObject}`,
    }
  }

  if (mode !== 'entra') {
    throw new Error(`Unsupported VITE_AUTH_MODE '${mode}'.`)
  }

  const application = await getMsal()
  const scope = import.meta.env.VITE_ENTRA_API_SCOPE
  if (!scope) {
    throw new Error('VITE_ENTRA_API_SCOPE is required in Entra mode.')
  }

  if (!activeAccount) {
    return runInteraction(async () => {
      await application.loginRedirect({ scopes: [scope] })
    }, 'Redirecting to sign in.')
  }

  try {
    const result = await application.acquireTokenSilent({
      account: activeAccount,
      scopes: [scope],
    })
    return {
      headers: { Authorization: `Bearer ${result.accessToken}` },
      identityKey: `${activeAccount.tenantId}:${activeAccount.homeAccountId}`,
    }
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      return runInteraction(async () => {
        await application.acquireTokenRedirect({
          account: activeAccount,
          scopes: [scope],
        })
      }, 'Redirecting to refresh authentication.')
    }
    throw error
  }
}

function runInteraction(
  operation: () => Promise<void>,
  message: string,
): Promise<never> {
  interactionPromise ??= (async () => {
    try {
      await operation()
      throw new Error(message)
    } finally {
      interactionPromise = undefined
    }
  })()
  return interactionPromise
}

export const authMode = mode
