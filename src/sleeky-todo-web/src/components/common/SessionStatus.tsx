import styles from './SessionStatus.module.scss'

/**
 * Shown while something the whole page depends on is still in flight: the
 * session check, and then the Space list. Both the login page and the
 * protected route wrapper reach the first state, and they have to look
 * identical: the user sees one of them purely according to which route they
 * landed on.
 */
export function SessionStatus({ message = 'Checking your session…' }: { message?: string }) {
  return (
    <main className={styles.sessionStatus} aria-busy="true">
      <p>{message}</p>
    </main>
  )
}
