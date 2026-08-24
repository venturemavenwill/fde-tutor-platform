import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, test, vi } from 'vitest'
import { ResponseForm } from './ResponseForm'

test('retains the learner draft when the API does not accept it', async () => {
  const user = userEvent.setup()
  const onSubmit = vi.fn().mockResolvedValue(false)
  render(
    <ResponseForm
      draftKey="failed-draft"
      prompt="Write your answer"
      submitLabel="Save"
      busy={false}
      onSubmit={onSubmit}
    />,
  )

  const response = screen.getByRole('textbox', { name: 'Write your answer' })
  await user.type(response, 'My unsaved learner response')
  await user.click(screen.getByRole('button', { name: 'Save' }))

  expect(onSubmit).toHaveBeenCalledWith('My unsaved learner response')
  expect(response).toHaveValue('My unsaved learner response')
  expect(localStorage.getItem('fde-tutor:draft:failed-draft')).toBe(
    'My unsaved learner response',
  )
})

test('switching session keys restores the destination draft', async () => {
  const user = userEvent.setup()
  localStorage.setItem('fde-tutor:draft:session-b:retrieval', 'Session B draft')
  const view = render(
    <ResponseForm
      key="session-a:retrieval"
      draftKey="session-a:retrieval"
      prompt="Changed-context response"
      submitLabel="Save"
      busy={false}
      onSubmit={vi.fn().mockResolvedValue(false)}
    />,
  )
  await user.type(
    screen.getByRole('textbox', { name: 'Changed-context response' }),
    'Session A draft',
  )

  view.rerender(
    <ResponseForm
      key="session-b:retrieval"
      draftKey="session-b:retrieval"
      prompt="Changed-context response"
      submitLabel="Save"
      busy={false}
      onSubmit={vi.fn().mockResolvedValue(false)}
    />,
  )

  expect(
    screen.getByRole('textbox', { name: 'Changed-context response' }),
  ).toHaveValue('Session B draft')
  expect(localStorage.getItem('fde-tutor:draft:session-a:retrieval')).toBe(
    'Session A draft',
  )
})
