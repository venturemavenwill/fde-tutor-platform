import { useEffect, useState } from 'react'

export function useDraft(key: string, initial = '') {
  const storageKey = `fde-tutor:draft:${key}`
  const [value, setValue] = useState(() => localStorage.getItem(storageKey) ?? initial)

  useEffect(() => {
    localStorage.setItem(storageKey, value)
  }, [storageKey, value])

  const clear = () => {
    localStorage.removeItem(storageKey)
    setValue('')
  }

  return [value, setValue, clear] as const
}
