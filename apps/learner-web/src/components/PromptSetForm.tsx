import { useEffect, useState } from 'react'
import type { Prompt } from '../contracts'

type PromptSetFormProps = {
  draftKey: string
  prompts: Prompt[]
  submitLabel: string
  busy: boolean
  onSubmit: (responses: Record<string, string>) => Promise<boolean>
}

export function PromptSetForm({
  draftKey,
  prompts,
  submitLabel,
  busy,
  onSubmit,
}: PromptSetFormProps) {
  const storageKey = `fde-tutor:draft:${draftKey}`
  const emptyResponses = (): Record<string, string> =>
    Object.fromEntries(prompts.map((prompt) => [prompt.id, ''] as const))
  const [responses, setResponses] = useState<Record<string, string>>(() => {
    const stored = localStorage.getItem(storageKey)
    if (!stored) return emptyResponses()
    try {
      const parsed = JSON.parse(stored) as Record<string, unknown>
      return Object.fromEntries(
        prompts.map((prompt) => {
          const value = parsed[prompt.id]
          return [prompt.id, typeof value === 'string' ? value : ''] as const
        }),
      )
    } catch {
      return emptyResponses()
    }
  })
  const complete = prompts.every((prompt) => responses[prompt.id]?.trim())

  useEffect(() => {
    localStorage.setItem(storageKey, JSON.stringify(responses))
  }, [responses, storageKey])

  return (
    <form
      className="prompt-set"
      onSubmit={async (event) => {
        event.preventDefault()
        if (!complete) return
        if (await onSubmit(responses)) {
          localStorage.removeItem(storageKey)
          setResponses(emptyResponses())
        }
      }}
    >
      {prompts.map((prompt, index) => (
        <div className="prompt-field" key={prompt.id}>
          <label htmlFor={`prompt-${prompt.id}`}>
            <span>{index + 1}</span>
            {prompt.prompt}
          </label>
          <textarea
            id={`prompt-${prompt.id}`}
            rows={4}
            maxLength={12000}
            required
            disabled={busy}
            value={responses[prompt.id] ?? ''}
            onChange={(event) =>
              setResponses((current) => ({
                ...current,
                [prompt.id]: event.target.value,
              }))
            }
          />
        </div>
      ))}
      <button type="submit" disabled={busy || !complete}>
        {busy ? 'Saving…' : submitLabel}
      </button>
    </form>
  )
}
