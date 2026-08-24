import { useId } from 'react'
import { useDraft } from '../useDraft'

type ResponseFormProps = {
  draftKey: string
  prompt: string
  submitLabel: string
  busy: boolean
  onSubmit: (response: string) => Promise<boolean>
}

export function ResponseForm({
  draftKey,
  prompt,
  submitLabel,
  busy,
  onSubmit,
}: ResponseFormProps) {
  const id = useId()
  const [response, setResponse, clear] = useDraft(draftKey)

  return (
    <form
      className="response-form"
      onSubmit={async (event) => {
        event.preventDefault()
        if (!response.trim()) return
        if (await onSubmit(response)) {
          clear()
        }
      }}
    >
      <label htmlFor={id}>{prompt}</label>
      <textarea
        id={id}
        value={response}
        onChange={(event) => setResponse(event.target.value)}
        rows={7}
        maxLength={12000}
        required
        disabled={busy}
      />
      <div className="form-footer">
        <span>{response.length.toLocaleString()} / 12,000</span>
        <button type="submit" disabled={busy || !response.trim()}>
          {busy ? 'Saving…' : submitLabel}
        </button>
      </div>
    </form>
  )
}
