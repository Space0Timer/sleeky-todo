import styles from './SessionStatus.module.scss'

/**
 * Shown while the session check is still in flight. Both the login page and the
 * protected route wrapper reach this state, and they have to look identical:
 * the user sees one of them purely according to which route they landed on.
 */
export function SessionStatus() {
  return (
    <main className={styles.sessionStatus} aria-busy="true">
      <p>Checking your session…</p>
    </main>
  )
}
