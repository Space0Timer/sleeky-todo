import { useEffect, useState } from 'react'

/**
 * Reports `value` only once it has stopped changing for `delay` milliseconds.
 *
 * Typing in a search box produces a value per keystroke, and each one would
 * otherwise become its own request. Waiting for a pause sends one request for
 * a word rather than one per letter.
 */
export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(timer)
  }, [delay, value])

  return debounced
}
