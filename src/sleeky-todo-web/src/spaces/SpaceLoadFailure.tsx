import { Button } from '../components/common/index.ts'
import styles from './SpaceLoadFailure.module.scss'

type SpaceLoadFailureProps = {
  detail: string
  retrying: boolean
  onRetry: () => void
}

/**
 * Nothing on the page can render without the Space list, so a failed first
 * read is a page-level state rather than a toast over an empty shell. It
 * stands alone the way the login page does, and offers the one action that
 * helps.
 */
export function SpaceLoadFailure({ detail, retrying, onRetry }: SpaceLoadFailureProps) {
  return (
    <main className={styles.spaceLoadFailure} role="alert">
      <h1>Sleeky To-Do</h1>
      <p>
        <strong>Your spaces could not be loaded.</strong>
      </p>
      <p>{detail}</p>
      <Button variant="primary" disabled={retrying} onClick={onRetry}>
        {retrying ? 'Trying again…' : 'Try again'}
      </Button>
    </main>
  )
}
