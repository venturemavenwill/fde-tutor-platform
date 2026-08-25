export type LearningState =
  | 'Orient'
  | 'ExpectationRequired'
  | 'ColdStartRequired'
  | 'PrimingRequired'
  | 'UnpaidRemedyRequired'
  | 'SourceAvailable'
  | 'CriterionAvailable'
  | 'ComparisonRequired'
  | 'RevisionAvailable'
  | 'AuthenticTransferRequired'
  | 'RetrievalScheduleRequired'
  | 'RetrievalScheduled'
  | 'RetrievalDue'
  | 'Complete'

export type PolicyDecision = {
  schemaVersion: '1.0.0'
  policyVersion: string
  contentNodeId: 'S083'
  state: LearningState
  permittedActions: string[]
  criterionRevealAllowed: boolean
  paidProposalImprovementAllowed: boolean
  evidenceBearing: false
  masteryEffect: 'NONE'
  humanReviewRequired: boolean
  groundingResult: 'GROUNDED' | 'GROUNDING_REQUIRED' | 'NOT_APPLICABLE'
  denialReason: string
}

export type LearnerEvent = {
  eventId: string
  eventType: string
  eventVersion: number
  occurredAt: string
  recordedAt: string
  tenantId: string
  learnerId: string
  sessionId: string
  contentNodeId: 'S083'
  contentRevision: string
  correlationId: string
  causationId: string | null
  idempotencyKey: string
  actor: { type: string; id: string }
  payload: Record<string, unknown>
}

export type SessionState = {
  sessionId: string | null
  contentNodeId: 'S083'
  contentRevision: string
  policy: PolicyDecision
  timeline: LearnerEvent[]
  projectionVersion: number
}

export type DueRetrieval = {
  sessionId: string
  contentNodeId: 'S083'
  dueAt: string
  isDue: boolean
}

export type LearningHome = {
  current: SessionState | null
  dueRetrievals: DueRetrieval[]
}

export type Prompt = {
  id: string
  prompt: string
}

export type S083Content = {
  contentNodeId: 'S083'
  contentRevision: string
  sourceAbsentRecall: boolean
  title: string
  expectedDurationMinutes: number
  organizer: string
  vocabulary: { term: string; definition: string }[]
  expectationPrompt: string
  coldStartPrompts: Prompt[]
  primingPrompts: Prompt[]
  unpaidRemedyPrompt: string
  comparisonPrompt: string
  authenticTransfer: {
    prompt: string
    artifactClassification: 'AUTHENTIC_WORK'
    pilotRestriction: 'SYNTHETIC_REDACTED_OR_EXPLICITLY_APPROVED'
  }
  assessmentBearing: false
  masteryEffect: 'NONE'
}

export type Criterion = {
  contentNodeId: 'S083'
  contentRevision: string
  elements: string[]
}

export type CommandAccepted = {
  event: LearnerEvent
  isDuplicate: boolean
  policy: PolicyDecision
}

export type CriterionReveal = {
  command: CommandAccepted
  criterion: Criterion
}

export type SourceOpen = {
  command: CommandAccepted
  sourceHtml: string
}

export type ApiFailure = {
  code: string
  message: string
  requiredAction?: string
}

export type PlatformRole =
  | 'Learner'
  | 'Instructor'
  | 'Reviewer'
  | 'Author'
  | 'Administrator'
  | 'Operator'

export type AuthorizationDisposition =
  | 'Allow'
  | 'Deny'
  | 'Deferred'
  | 'External'

export type AccessConsole = {
  schemaVersion: '1.0.0'
  matrixVersion: string
  currentUser: {
    tenantId: string
    objectId: string
    externalSubject: string
    authenticationMode: 'Development' | 'Entra'
    isSynthetic: boolean
    roles: PlatformRole[]
    effectiveCapabilities: string[]
  }
  userManagement: {
    assignmentAuthority: string
    roleMutationAvailable: false
    enrolmentAvailable: false
    directoryMode: 'InMemory' | 'Postgres'
  }
  roles: {
    id: PlatformRole
    label: string
    description: string
  }[]
  capabilities: {
    id: string
    label: string
    constraint: string
    access: Record<PlatformRole, AuthorizationDisposition>
  }[]
}

export type ObservedUser = {
  tenantId: string
  objectId: string
  externalSubject: string
  authenticationMode: 'Development' | 'Entra'
  roles: PlatformRole[]
  firstObservedAt: string
  lastObservedAt: string
}
