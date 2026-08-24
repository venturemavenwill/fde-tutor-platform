import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, test, vi } from 'vitest'
import { PromptSetForm } from './PromptSetForm'

const prompts = [
  { id: 'one', prompt: 'First prompt' },
  { id: 'two', prompt: 'Second prompt' },
]

test('restores multi-prompt drafts after remount', async () => {
  const user = userEvent.setup()
  const first = render(
    <PromptSetForm
      draftKey="cold-start-draft"
      prompts={prompts}
      submitLabel="Save"
      busy={false}
      onSubmit={vi.fn().mockResolvedValue(false)}
    />,
  )
  await user.type(screen.getByRole('textbox', { name: /First prompt/ }), 'First draft')
  await user.type(screen.getByRole('textbox', { name: /Second prompt/ }), 'Second draft')
  first.unmount()

  render(
    <PromptSetForm
      draftKey="cold-start-draft"
      prompts={prompts}
      submitLabel="Save"
      busy={false}
      onSubmit={vi.fn().mockResolvedValue(false)}
    />,
  )

  expect(screen.getByRole('textbox', { name: /First prompt/ })).toHaveValue(
    'First draft',
  )
  expect(screen.getByRole('textbox', { name: /Second prompt/ })).toHaveValue(
    'Second draft',
  )
})
