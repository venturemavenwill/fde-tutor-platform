import { render, screen } from '@testing-library/react'
import axe from 'axe-core'
import { beforeEach, expect, test, vi } from 'vitest'
import App from './App'

const content = {
  contentNodeId: 'S083',
  contentRevision: '032601ea05b48ed716e72ac217a0024ec6ae413b0b27113c704ba6ab4f332522',
  title: 'Momentum, and Why It Decays',
  expectedDurationMinutes: 27,
  organizer: 'Momentum is a set of conditions, not a mood.',
  vocabulary: [
    {
      term: 'decay path',
      definition: 'A named condition whose failure erodes momentum.',
    },
  ],
  expectationPrompt: 'What do you expect to learn?',
  coldStartPrompts: [],
  primingPrompts: [],
  unpaidRemedyPrompt: 'Write the unpaid remedy.',
  comparisonPrompt: 'Compare your answer.',
  assessmentBearing: false,
  masteryEffect: 'NONE',
}

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      const body = url.endsWith('/content')
        ? content
        : { current: null, dueRetrievals: [] }
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )
})

test('introduces S083 without exposing the locked criterion', async () => {
  render(<App />)

  expect(
    await screen.findByRole('heading', {
      name: 'Momentum, and Why It Decays',
    }),
  ).toBeInTheDocument()
  expect(screen.getByText('Practice, not a mastery score.')).toBeInTheDocument()
  expect(
    screen.queryByText('A customer-owned remedy names all four'),
  ).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Start S083' })).toBeEnabled()
})

test('initial learner page has no automated accessibility violations', async () => {
  const { container } = render(<App />)
  await screen.findByRole('button', { name: 'Start S083' })

  const results = await axe.run(container, {
    rules: {
      'color-contrast': { enabled: false },
    },
  })
  expect(results.violations).toEqual([])
})

test('prioritizes a due retrieval over a newer incomplete session', async () => {
  const policy = (state: string) => ({
    schemaVersion: '1.0.0',
    policyVersion: 's083-policy-1',
    contentNodeId: 'S083',
    state,
    permittedActions:
      state === 'RetrievalDue' ? ['CompleteRetrieval'] : ['RecordExpectation'],
    criterionRevealAllowed: false,
    paidProposalImprovementAllowed: false,
    evidenceBearing: false,
    masteryEffect: 'NONE',
    humanReviewRequired: false,
    groundingResult: 'NOT_APPLICABLE',
    denialReason: 'None',
  })
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      let body: unknown
      if (url.endsWith('/content')) {
        body = content
      } else if (url.endsWith('/learning-home')) {
        body = {
          current: {
            sessionId: 'new-session',
            contentNodeId: 'S083',
            contentRevision: content.contentRevision,
            policy: policy('ExpectationRequired'),
            timeline: [],
            projectionVersion: 1,
          },
          dueRetrievals: [
            {
              sessionId: 'due-session',
              contentNodeId: 'S083',
              dueAt: '2026-08-24T00:00:00Z',
              isDue: true,
            },
          ],
        }
      } else {
        body = {
          sessionId: 'due-session',
          contentNodeId: 'S083',
          contentRevision: content.contentRevision,
          policy: policy('RetrievalDue'),
          timeline: [],
          projectionVersion: 9,
        }
      }
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )

  render(<App />)

  expect(
    await screen.findByRole('heading', { name: 'Retrieve in a changed context' }),
  ).toBeInTheDocument()
  expect(screen.getByText('Retrieval due')).toBeInTheDocument()
  expect(screen.queryByText(content.organizer)).not.toBeInTheDocument()
  expect(screen.queryByText('decay path')).not.toBeInTheDocument()
  expect(screen.getByText('Source-absent retrieval')).toBeInTheDocument()
})

test('renders the acknowledged unpaid remedy during comparison', async () => {
  const unpaidRemedy =
    'The customer owner runs a durable cadence from an automatically produced drift register.'
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      let body: unknown
      if (url.endsWith('/content')) {
        body = content
      } else if (url.endsWith('/learning-home')) {
        body = {
          current: {
            sessionId: 'comparison-session',
            contentNodeId: 'S083',
            contentRevision: content.contentRevision,
            policy: {
              schemaVersion: '1.0.0',
              policyVersion: 's083-policy-1',
              contentNodeId: 'S083',
              state: 'ComparisonRequired',
              permittedActions: ['RecordComparison'],
              criterionRevealAllowed: true,
              paidProposalImprovementAllowed: true,
              evidenceBearing: false,
              masteryEffect: 'NONE',
              humanReviewRequired: false,
              groundingResult: 'NOT_APPLICABLE',
              denialReason: 'ComparisonRequired',
            },
            timeline: [
              {
                eventId: 'unpaid-event',
                eventType: 'UnpaidRemedyRecorded',
                payload: { response: unpaidRemedy },
              },
              {
                eventId: 'source-event',
                eventType: 'SourceViewed',
                payload: { acknowledged: true },
              },
              {
                eventId: 'reveal-event',
                eventType: 'ModelAnswerRevealed',
                payload: { acknowledged: true },
              },
            ],
            projectionVersion: 3,
          },
          dueRetrievals: [],
        }
      } else if (url.endsWith('/criterion')) {
        body = {
          contentNodeId: 'S083',
          contentRevision: content.contentRevision,
          elements: ['Owner', 'Cadence', 'Artifact', 'Trigger'],
        }
      } else {
        body = { sourceHtml: '<article><h2>Durable method</h2></article>' }
      }
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )

  render(<App />)

  expect(await screen.findByText(unpaidRemedy)).toBeInTheDocument()
  expect(
    screen.getByRole('heading', { name: 'The unpaid remedy you recorded' }),
  ).toBeInTheDocument()
})

test('refreshes an open scheduled session when its due time arrives', async () => {
  let homeRequests = 0
  const policy = (state: string) => ({
    schemaVersion: '1.0.0',
    policyVersion: 's083-policy-1',
    contentNodeId: 'S083',
    state,
    permittedActions:
      state === 'RetrievalDue' ? ['CompleteRetrieval'] : [],
    criterionRevealAllowed: false,
    paidProposalImprovementAllowed: false,
    evidenceBearing: false,
    masteryEffect: 'NONE',
    humanReviewRequired: false,
    groundingResult: 'NOT_APPLICABLE',
    denialReason: state === 'RetrievalDue' ? 'None' : 'RetrievalNotDue',
  })
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      let body: unknown
      if (url.endsWith('/content')) {
        body = content
      } else {
        homeRequests += 1
        const due = homeRequests > 1
        body = {
          current: {
            sessionId: 'scheduled-session',
            contentNodeId: 'S083',
            contentRevision: content.contentRevision,
            policy: policy(due ? 'RetrievalDue' : 'RetrievalScheduled'),
            timeline: [
              {
                eventId: 'schedule-event',
                eventType: 'RetrievalScheduled',
                payload: { dueAt: '2020-01-01T00:00:00Z' },
              },
            ],
            projectionVersion: 9,
          },
          dueRetrievals: due
            ? [
                {
                  sessionId: 'scheduled-session',
                  contentNodeId: 'S083',
                  dueAt: '2020-01-01T00:00:00Z',
                  isDue: true,
                },
              ]
            : [],
        }
      }
      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )

  render(<App />)

  expect(
    await screen.findByRole('heading', { name: 'Retrieve in a changed context' }),
  ).toBeInTheDocument()
  expect(homeRequests).toBeGreaterThan(1)
})
