import { useCallback, useEffect, useRef, useState } from 'react'
import DOMPurify from 'dompurify'
import './App.css'
import { api, ApiError } from './api'
import { AccessConsole } from './components/AccessConsole'
import { PromptSetForm } from './components/PromptSetForm'
import { ResponseForm } from './components/ResponseForm'
import type {
  AccessConsole as AccessConsoleContract,
  CommandAccepted,
  Criterion,
  LearningHome,
  ObservedUser,
  S083Content,
  SessionState,
} from './contracts'

const stateLabels: Record<string, string> = {
  ExpectationRequired: 'Set your expectation',
  ColdStartRequired: 'Retrieve before opening the source',
  PrimingRequired: 'Commit your starting view',
  UnpaidRemedyRequired: 'Write the unpaid remedy',
  SourceAvailable: 'Study the durable method',
  CriterionAvailable: 'Compare with the criterion',
  ComparisonRequired: 'Name the difference',
  RevisionAvailable: 'Revise the design',
  AuthenticTransferRequired: 'Apply it to authentic work',
  RetrievalScheduleRequired: 'Schedule your return',
  RetrievalScheduled: 'Let time do some work',
  RetrievalDue: 'Retrieve in a changed context',
  Complete: 'Learning loop complete',
}

async function selectSessionForHome(
  content: S083Content,
  home: LearningHome,
): Promise<SessionState | undefined> {
  const due = home.dueRetrievals
    .filter((item) => item.isDue)
    .sort((left, right) => left.dueAt.localeCompare(right.dueAt))[0]
  const selected =
    due && home.current?.sessionId !== due.sessionId
      ? await api.getSession(due.sessionId)
      : home.current ?? undefined
  if (selected && selected.contentRevision !== content.contentRevision) {
    throw new Error(
      'Your session uses a content revision that is not currently loaded. Do not continue until that revision is restored.',
    )
  }
  return selected
}

async function loadSessionMaterials(selected?: SessionState) {
  if (!selected || selected.policy.state === 'RetrievalDue') {
    return { criterion: undefined, sourceHtml: undefined }
  }
  const [criterion, source] = await Promise.all([
    selected.timeline.some((event) => event.eventType === 'ModelAnswerRevealed')
      ? api.getCriterion(selected.sessionId!)
      : Promise.resolve(undefined),
    selected.timeline.some((event) => event.eventType === 'SourceViewed')
      ? api.getSource(selected.sessionId!)
      : Promise.resolve(undefined),
  ])
  return { criterion, sourceHtml: source?.sourceHtml }
}

function retrievalScheduleStorageKey(sessionId: string) {
  return `fde-tutor:retrieval-schedule:${sessionId}`
}

function getOrCreateRetrievalDueAt(sessionId: string): string {
  const key = retrievalScheduleStorageKey(sessionId)
  const stored = localStorage.getItem(key)
  if (stored && !Number.isNaN(Date.parse(stored))) {
    return stored
  }
  const dueAt = new Date(Date.now() + 2 * 24 * 60 * 60 * 1000).toISOString()
  localStorage.setItem(key, dueAt)
  return dueAt
}

type SiteHeaderProps = {
  access: AccessConsoleContract
  accessOpen: boolean
  expectedDurationMinutes?: number
  retrievalDue?: boolean
  onToggleAccess: () => void
}

function SiteHeader({
  access,
  accessOpen,
  expectedDurationMinutes,
  retrievalDue = false,
  onToggleAccess,
}: SiteHeaderProps) {
  return (
    <header className="site-header">
      <a className="brand" href="#main">
        <span>FDE</span> Tutor
      </a>
      <div className="header-meta">
        {expectedDurationMinutes !== undefined && (
          <>
            <span>S083</span>
            <span>{expectedDurationMinutes} minutes</span>
          </>
        )}
        {retrievalDue && <span className="mode">Retrieval due</span>}
        <span className="mode">
          {access.currentUser.isSynthetic ? 'Local development' : 'Workforce'}
        </span>
        <button
          type="button"
          className="access-toggle"
          aria-expanded={accessOpen}
          aria-controls="identity-access"
          onClick={onToggleAccess}
        >
          Identity &amp; access
          <span>{access.currentUser.roles.length}</span>
        </button>
      </div>
    </header>
  )
}

function App() {
  const [access, setAccess] = useState<AccessConsoleContract>()
  const [observedUsers, setObservedUsers] = useState<ObservedUser[]>([])
  const [accessOpen, setAccessOpen] = useState(false)
  const [content, setContent] = useState<S083Content>()
  const [home, setHome] = useState<LearningHome>()
  const [session, setSession] = useState<SessionState>()
  const [criterion, setCriterion] = useState<Criterion>()
  const [sourceHtml, setSourceHtml] = useState<string>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [notice, setNotice] = useState<string>()
  const [recallLockPending, setRecallLockPending] = useState(false)
  const stateHeading = useRef<HTMLHeadingElement>(null)
  const reconciliationGeneration = useRef(0)
  const currentState = session?.policy.state
  const selectedSessionDueAt =
    currentState === 'RetrievalScheduled'
      ? session?.timeline
          .filter((event) => event.eventType === 'RetrievalScheduled')
          .at(-1)?.payload.dueAt
      : undefined
  const nextDueAt = [
    ...(home?.dueRetrievals.map((item) => item.dueAt) ?? []),
    ...(typeof selectedSessionDueAt === 'string' ? [selectedSessionDueAt] : []),
  ].sort()[0]

  const handleError = (cause: unknown) => {
    setError(
      cause instanceof ApiError
        ? cause.details?.message ?? cause.message
        : cause instanceof Error
          ? cause.message
          : 'The platform could not complete that request.',
    )
  }

  const enterRecallLockForCause = (cause: unknown) => {
    if (
      cause instanceof ApiError &&
      (cause.details?.code === 'DUE_RETRIEVAL_REQUIRED' ||
        cause.details?.code === 'SOURCE_ABSENT_RECALL_REQUIRED')
    ) {
      setRecallLockPending(true)
      setCriterion(undefined)
      setSourceHtml(undefined)
    }
  }

  const reconcileAuthoritativeState = useCallback(async (): Promise<
    LearningHome | undefined
  > => {
    if (!content) return undefined
    const generation = ++reconciliationGeneration.current
    const [refreshedContent, refreshedHome] = await Promise.all([
      api.getContent(),
      api.getHome(),
    ])
    const selected = await selectSessionForHome(
      refreshedContent,
      refreshedHome,
    )
    const materials = await loadSessionMaterials(selected)
    if (generation !== reconciliationGeneration.current) {
      return refreshedHome
    }
    setContent(refreshedContent)
    setHome(refreshedHome)
    setSession(selected)
    setCriterion(materials.criterion)
    setSourceHtml(materials.sourceHtml)
    setRecallLockPending(false)
    return refreshedHome
  }, [content])

  useEffect(() => {
    const load = async () => {
      const loadedAccess = await api.getAccess()
      setAccess(loadedAccess)
      if (loadedAccess.currentUser.roles.includes('Administrator')) {
        setObservedUsers(await api.getObservedUsers())
      }
      if (!loadedAccess.currentUser.roles.includes('Learner')) {
        setAccessOpen(true)
        return
      }

      const [loadedContent, loadedHome] = await Promise.all([
        api.getContent(),
        api.getHome(),
      ])
      const selectedSession = await selectSessionForHome(
        loadedContent,
        loadedHome,
      )
      const materials = await loadSessionMaterials(selectedSession)
      setContent(loadedContent)
      setHome(loadedHome)
      setSession(selectedSession ?? undefined)
      setCriterion(materials.criterion)
      setSourceHtml(materials.sourceHtml)
    }
    load().catch(handleError)
  }, [])

  useEffect(() => {
    if (currentState) {
      stateHeading.current?.focus()
    }
  }, [currentState, session?.sessionId])

  useEffect(() => {
    if (!content || typeof nextDueAt !== 'string') {
      return
    }

    let refreshing = false
    let timeout: number | undefined
    const refresh = async (): Promise<LearningHome | undefined> => {
      if (refreshing) return
      refreshing = true
      try {
        const refreshed = await reconcileAuthoritativeState()
        setNotice(undefined)
        return refreshed
      } catch {
        setNotice('The next learning act could not be refreshed. Reload to continue.')
        return undefined
      } finally {
        refreshing = false
      }
    }
    const dueTime = Date.parse(nextDueAt)
    const arm = () => {
      const delay = Math.min(
        Math.max(dueTime - Date.now(), 0),
        2_147_000_000,
      )
      timeout = window.setTimeout(async () => {
        if (Date.now() >= dueTime) {
          setRecallLockPending(true)
          setCriterion(undefined)
          setSourceHtml(undefined)
        }
        const refreshed = await refresh()
        const stillPending = refreshed?.dueRetrievals.some(
          (item) => item.dueAt === nextDueAt && !item.isDue,
        )
        if (!refreshed) {
          timeout = window.setTimeout(arm, 1000)
        } else if (Date.now() < dueTime || stillPending) {
          timeout = window.setTimeout(arm, stillPending ? 1000 : 0)
        }
      }, delay)
    }
    arm()
    return () => {
      if (timeout !== undefined) window.clearTimeout(timeout)
    }
  }, [content, nextDueAt, reconcileAuthoritativeState])

  useEffect(() => {
    if (!content) return
    const refresh = () => {
      if (nextDueAt && Date.now() >= Date.parse(nextDueAt)) {
        setRecallLockPending(true)
        setCriterion(undefined)
        setSourceHtml(undefined)
      }
      void reconcileAuthoritativeState().catch(() => {
        setNotice('The current learning act could not be refreshed. Reload to continue.')
      })
    }
    const refreshWhenVisible = () => {
      if (document.visibilityState === 'visible') refresh()
    }
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', refreshWhenVisible)
    return () => {
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', refreshWhenVisible)
    }
  }, [content, nextDueAt, reconcileAuthoritativeState])

  function applyAcceptedCommand(result: CommandAccepted): SessionState {
    const priorTimeline =
      session?.sessionId === result.event.sessionId ? session.timeline : []
    const timeline = priorTimeline.some(
      (item) => item.eventId === result.event.eventId,
    )
      ? priorTimeline
      : [...priorTimeline, result.event]
    return {
      sessionId: result.event.sessionId,
      contentNodeId: result.event.contentNodeId,
      contentRevision: result.event.contentRevision,
      policy: result.policy,
      timeline,
      projectionVersion: timeline.length,
    }
  }

  const run = async (
    operation: () => Promise<CommandAccepted>,
    onFailure?: (cause: unknown) => void,
  ): Promise<boolean> => {
    const generationAtStart = reconciliationGeneration.current
    setBusy(true)
    setError(undefined)
    setNotice(undefined)
    try {
      const result = await operation()
      if (
        result.isDuplicate ||
        generationAtStart !== reconciliationGeneration.current
      ) {
        try {
          await reconcileAuthoritativeState()
        } catch {
          setNotice(
            'The command was accepted, but the current learning act could not be refreshed. Reload to continue.',
          )
        }
        return true
      }

      reconciliationGeneration.current += 1
      const updated = applyAcceptedCommand(result)
      setSession(updated)
      setHome((current) =>
        current
          ? { ...current, current: updated }
          : { current: updated, dueRetrievals: [] },
      )
      if (result.event.eventType === 'RetrievalCompleted' && content) {
        try {
          await reconcileAuthoritativeState()
        } catch {
          setNotice(
            'Your retrieval was saved, but the next learning act could not be refreshed. Reload to continue.',
          )
        }
      }
      return true
    } catch (cause) {
      handleError(cause)
      enterRecallLockForCause(cause)
      onFailure?.(cause)
      if (cause instanceof ApiError && cause.status === 409 && content) {
        try {
          await reconcileAuthoritativeState()
        } catch {
          setNotice(
            'The server rejected stale state, and the current learning act could not be refreshed. Reload to continue.',
          )
        }
      }
      return false
    } finally {
      setBusy(false)
    }
  }

  if (!access) {
    return (
      <output className="loading-shell">
        {error ?? 'Loading identity and authorization…'}
      </output>
    )
  }
  const canUseLearnerRuntime = access.currentUser.roles.includes('Learner')
  if (!canUseLearnerRuntime) {
    return (
      <div className="app-shell">
        <SiteHeader
          access={access}
          accessOpen={accessOpen}
          onToggleAccess={() => setAccessOpen((current) => !current)}
        />
        <main id="main">
          {accessOpen && (
            <AccessConsole
              access={access}
              users={observedUsers}
              onClose={() => setAccessOpen(false)}
            />
          )}
          <section className="role-gate" aria-labelledby="role-gate-title">
            <p className="step">Access boundary</p>
            <h1 id="role-gate-title">The Learner app role is required for S083</h1>
            <p>
              Your identity is valid, but its effective Microsoft Entra app roles
              do not permit learner-state reads or commands. Role assignments are
              managed outside this application.
            </p>
          </section>
        </main>
      </div>
    )
  }
  if (!content || !home) {
    return (
      <output className="loading-shell">
        {error ?? 'Loading the S083 learning experience…'}
      </output>
    )
  }
  const sourceAbsentRecall =
    content.sourceAbsentRecall ||
    recallLockPending ||
    currentState === 'RetrievalDue'

  return (
    <div className="app-shell">
      <SiteHeader
        access={access}
        accessOpen={accessOpen}
        expectedDurationMinutes={content.expectedDurationMinutes}
        retrievalDue={home.dueRetrievals.some((item) => item.isDue)}
        onToggleAccess={() => setAccessOpen((current) => !current)}
      />

      <main id="main">
        {accessOpen && (
          <AccessConsole
            access={access}
            users={observedUsers}
            onClose={() => setAccessOpen(false)}
          />
        )}
        <section className="hero" aria-labelledby="page-title">
          <p className="eyebrow">Stage 9 · Compound</p>
          <h1 id="page-title">{content.title}</h1>
          <p className="lede">
            {sourceAbsentRecall
              ? 'Complete the changed-context retrieval without reopening the organizer, vocabulary, source, criterion, or prior responses.'
              : content.organizer}
          </p>
          <div className="posture">
            <strong>Practice, not a mastery score.</strong>
            <span>
              This session records your reasoning, revisions, support used, and
              feedback.
            </span>
          </div>
        </section>

        <div className="learning-grid">
          <section className="learning-act" aria-live="polite">
            {recallLockPending ? (
              <output className="quiet-state">
                <p>
                  Source material is locked while the platform refreshes due
                  retrieval work.
                </p>
              </output>
            ) : !session ? (
              <>
                <p className="step">Begin</p>
                <h2 ref={stateHeading} tabIndex={-1}>
                  Start with your own reasoning
                </h2>
                <p>
                  Commit an answer before seeing the comparison criterion. Your
                  acknowledged work is retained across sessions and devices.
                </p>
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => run(() => api.startSession(content.contentRevision))}
                >
                  {busy ? 'Starting…' : 'Start S083'}
                </button>
              </>
            ) : (
              <LearningAct
                content={content}
                session={session}
                criterion={criterion}
                sourceHtml={sourceHtml}
                busy={busy}
                onRun={run}
                onReveal={async () => {
                  const generationAtStart = reconciliationGeneration.current
                  setBusy(true)
                  setError(undefined)
                  try {
                    const result = await api.revealCriterion(
                      session.sessionId!,
                      content.contentRevision,
                    )
                    if (
                      result.command.isDuplicate ||
                      generationAtStart !== reconciliationGeneration.current
                    ) {
                      await reconcileAuthoritativeState().catch(() => {
                        setNotice(
                          'The criterion reveal was saved, but the current learning act could not be refreshed. Reload to continue.',
                        )
                      })
                      return
                    }
                    reconciliationGeneration.current += 1
                    setCriterion(result.criterion)
                    const updated = applyAcceptedCommand(result.command)
                    setSession(updated)
                    setHome((current) => ({ ...current!, current: updated }))
                  } catch (cause) {
                    handleError(cause)
                    enterRecallLockForCause(cause)
                    if (cause instanceof ApiError && cause.status === 409) {
                      await reconcileAuthoritativeState().catch(() => {
                        setNotice(
                          'The criterion is locked by newer learner state. Reload to continue.',
                        )
                      })
                    }
                  } finally {
                    setBusy(false)
                  }
                }}
                onOpenSource={async () => {
                  const generationAtStart = reconciliationGeneration.current
                  setBusy(true)
                  setError(undefined)
                  try {
                    const result = await api.openSource(
                      session.sessionId!,
                      content.contentRevision,
                    )
                    if (
                      result.command.isDuplicate ||
                      generationAtStart !== reconciliationGeneration.current
                    ) {
                      await reconcileAuthoritativeState().catch(() => {
                        setNotice(
                          'Opening the source was saved, but the current learning act could not be refreshed. Reload to continue.',
                        )
                      })
                      return
                    }
                    reconciliationGeneration.current += 1
                    setSourceHtml(result.sourceHtml)
                    const updated = applyAcceptedCommand(result.command)
                    setSession(updated)
                    setHome((current) => ({ ...current!, current: updated }))
                  } catch (cause) {
                    handleError(cause)
                    enterRecallLockForCause(cause)
                    if (cause instanceof ApiError && cause.status === 409) {
                      await reconcileAuthoritativeState().catch(() => {
                        setNotice(
                          'The source is locked by newer learner state. Reload to continue.',
                        )
                      })
                    }
                  } finally {
                    setBusy(false)
                  }
                }}
                headingRef={stateHeading}
              />
            )}

            {error && (
              <div className="error" role="alert">
                <strong>That was not saved.</strong>
                <span>{error}</span>
              </div>
            )}
            {notice && (
              <output className="notice">{notice}</output>
            )}
          </section>

          {sourceAbsentRecall ? (
            <aside aria-labelledby="vocabulary-title">
              <h2 id="vocabulary-title">Source-absent retrieval</h2>
              <p>
                Instructional cues are hidden until the retrieval response is
                durably recorded.
              </p>
            </aside>
          ) : (
            <aside aria-labelledby="vocabulary-title">
              <h2 id="vocabulary-title">Working vocabulary</h2>
              <dl>
                {content.vocabulary.map((item) => (
                  <div key={item.term}>
                    <dt>{item.term}</dt>
                    <dd>{item.definition}</dd>
                  </div>
                ))}
              </dl>
            </aside>
          )}
        </div>
      </main>
    </div>
  )
}

type LearningActProps = {
  content: S083Content
  session: SessionState
  criterion?: Criterion
  sourceHtml?: string
  busy: boolean
  onRun: (
    operation: () => Promise<CommandAccepted>,
    onFailure?: (cause: unknown) => void,
  ) => Promise<boolean>
  onReveal: () => Promise<void>
  onOpenSource: () => Promise<void>
  headingRef: React.RefObject<HTMLHeadingElement | null>
}

function LearningAct({
  content,
  session,
  criterion,
  sourceHtml,
  busy,
  onRun,
  onReveal,
  onOpenSource,
  headingRef,
}: LearningActProps) {
  const state = session.policy.state
  const sessionId = session.sessionId!
  const revision = content.contentRevision
  const unpaidRemedy = session.timeline.find(
    (event) => event.eventType === 'UnpaidRemedyRecorded',
  )?.payload.response

  return (
    <>
      <p className="step">{session.projectionVersion} saved learning events</p>
      <h2 ref={headingRef} tabIndex={-1}>
        {stateLabels[state] ?? 'Continue'}
      </h2>

      {state === 'ExpectationRequired' && (
        <ResponseForm
          key={`${sessionId}:expectation`}
          draftKey={`${sessionId}:expectation`}
          prompt={content.expectationPrompt}
          submitLabel="Record expectation"
          busy={busy}
          onSubmit={(response) =>
            onRun(() =>
              api.recordText(sessionId, 'expectation', revision, response),
            )
          }
        />
      )}

      {state === 'ColdStartRequired' && (
        <>
          <p>
            These are unscored cold-start prompts. Attempt them from memory; they
            cannot fail a prerequisite or raise a competency level.
          </p>
          <PromptSetForm
            key={`${sessionId}:cold-start`}
            draftKey={`${sessionId}:cold-start`}
            prompts={content.coldStartPrompts}
            submitLabel="Save cold-start responses"
            busy={busy}
            onSubmit={(responses) =>
              onRun(() =>
                api.recordResponses(sessionId, 'cold-start', revision, responses),
              )
            }
          />
        </>
      )}

      {state === 'PrimingRequired' && (
        <PromptSetForm
          key={`${sessionId}:priming`}
          draftKey={`${sessionId}:priming`}
          prompts={content.primingPrompts}
          submitLabel="Commit starting view"
          busy={busy}
          onSubmit={(responses) =>
            onRun(() =>
              api.recordResponses(sessionId, 'priming', revision, responses),
            )
          }
        />
      )}

      {state === 'UnpaidRemedyRequired' && (
        <>
          <p>
            Do not improve the paid proposal yet. Test whether your diagnosis
            survives without your organization’s commercial interest.
          </p>
          <ResponseForm
            key={`${sessionId}:unpaid-remedy`}
            draftKey={`${sessionId}:unpaid-remedy`}
            prompt={content.unpaidRemedyPrompt}
            submitLabel="Record unpaid remedy"
            busy={busy}
            onSubmit={(response) =>
              onRun(() =>
                api.recordText(sessionId, 'unpaid-remedy', revision, response),
              )
            }
          />
        </>
      )}

      {state === 'CriterionAvailable' && (
        <>
          {sourceHtml && <SourcePanel sourceHtml={sourceHtml} />}
          <p>
            Your unpaid remedy is durably recorded. You can now open the
            comparison criterion without changing or erasing that first answer.
          </p>
          <button type="button" disabled={busy} onClick={onReveal}>
            {busy ? 'Opening…' : 'Open comparison criterion'}
          </button>
        </>
      )}

      {state === 'SourceAvailable' && (
        <>
          <p>
            Your unpaid remedy is durably recorded. Open the authored material
            now; the four-element criterion remains separate until you request
            the comparison.
          </p>
          <button type="button" disabled={busy} onClick={onOpenSource}>
            {busy ? 'Opening…' : 'Open session material'}
          </button>
        </>
      )}

      {state === 'ComparisonRequired' && criterion && (
        <>
          {sourceHtml && <SourcePanel sourceHtml={sourceHtml} />}
          {typeof unpaidRemedy === 'string' && (
            <RecordedResponsePanel response={unpaidRemedy} />
          )}
          <CriterionPanel criterion={criterion} />
          <ResponseForm
            key={`${sessionId}:comparison`}
            draftKey={`${sessionId}:comparison`}
            prompt={content.comparisonPrompt}
            submitLabel="Record comparison"
            busy={busy}
            onSubmit={(response) =>
              onRun(() =>
                api.recordText(sessionId, 'comparison', revision, response),
              )
            }
          />
        </>
      )}

      {state === 'RevisionAvailable' && criterion && (
        <>
          {sourceHtml && <SourcePanel sourceHtml={sourceHtml} />}
          {typeof unpaidRemedy === 'string' && (
            <RecordedResponsePanel response={unpaidRemedy} />
          )}
          <CriterionPanel criterion={criterion} />
          <ResponseForm
            key={`${sessionId}:revision`}
            draftKey={`${sessionId}:revision`}
            prompt="Revise the reinforcement design now. Keep the customer-owned remedy complete and state plainly what any paid version adds."
            submitLabel="Record revision"
            busy={busy}
            onSubmit={(response) =>
              onRun(() => api.recordText(sessionId, 'revision', revision, response))
            }
          />
        </>
      )}

      {state === 'AuthenticTransferRequired' && (
        <>
          <div className="posture-note">
            Use only synthetic, redacted, or explicitly approved work. Do not
            paste customer-confidential material into this development runtime.
          </div>
          <ResponseForm
            key={`${sessionId}:authentic-transfer`}
            draftKey={`${sessionId}:authentic-transfer`}
            prompt={content.authenticTransfer.prompt}
            submitLabel="Record transfer response"
            busy={busy}
            onSubmit={(response) =>
              onRun(() =>
                api.recordText(
                  sessionId,
                  'authentic-transfer',
                  revision,
                  response,
                ),
              )
            }
          />
        </>
      )}

      {state === 'RetrievalScheduleRequired' && (
        <>
          <p>
            Schedule a changed-context return. S084 and S085 carry-forward remain
            outside this slice.
          </p>
          <button
            type="button"
            disabled={busy}
            onClick={async () => {
              const dueAt = getOrCreateRetrievalDueAt(sessionId)
              const accepted = await onRun(
                () => api.scheduleRetrieval(sessionId, revision, dueAt),
                (cause) => {
                  if (
                    cause instanceof ApiError &&
                    cause.details?.code === 'INVALID_DUE_AT'
                  ) {
                    localStorage.removeItem(
                      retrievalScheduleStorageKey(sessionId),
                    )
                  }
                },
              )
              if (accepted) {
                localStorage.removeItem(retrievalScheduleStorageKey(sessionId))
              }
            }}
          >
            {busy ? 'Scheduling…' : 'Schedule for two days'}
          </button>
        </>
      )}

      {state === 'RetrievalScheduled' && (
        <div className="quiet-state">
          <p>
            Your first pass is saved. Return when the changed-context retrieval
            becomes due; rereading now would test familiarity, not recall.
          </p>
        </div>
      )}

      {state === 'RetrievalDue' && (
        <ResponseForm
          key={`${sessionId}:retrieval`}
          draftKey={`${sessionId}:retrieval`}
          prompt="Without reopening the source, explain how you would tell whether reinforcement survives supplier departure and how you would test the first failing condition."
          submitLabel="Record changed-context retrieval"
          busy={busy}
          onSubmit={(response) =>
            onRun(() => api.recordText(sessionId, 'retrieval', revision, response))
          }
        />
      )}

      {state === 'Complete' && (
        <div className="complete-state">
          <p className="completion-mark" aria-hidden="true">
            ✓
          </p>
          <p>
            The S083 learning loop is complete. This records participation and
            reasoning—not mastery or entrustment.
          </p>
        </div>
      )}
    </>
  )
}

function SourcePanel({ sourceHtml }: { sourceHtml: string }) {
  return (
    <section
      className="source-material"
      aria-label="S083 session material"
      dangerouslySetInnerHTML={{
        __html: DOMPurify.sanitize(sourceHtml, {
          USE_PROFILES: { html: true },
        }),
      }}
    />
  )
}

function RecordedResponsePanel({ response }: { response: string }) {
  return (
    <section className="recorded-response" aria-labelledby="recorded-response-title">
      <p className="eyebrow">Your acknowledged first answer</p>
      <h3 id="recorded-response-title">The unpaid remedy you recorded</h3>
      <blockquote>{response}</blockquote>
    </section>
  )
}

function CriterionPanel({ criterion }: { criterion: Criterion }) {
  return (
    <section className="criterion" aria-labelledby="criterion-title">
      <p className="eyebrow">Compare, then revise</p>
      <h3 id="criterion-title">A customer-owned remedy names all four</h3>
      <ol>
        {criterion.elements.map((element) => (
          <li key={element}>{element}</li>
        ))}
      </ol>
    </section>
  )
}

export default App
