import styles from './FieldError.module.scss'

export function FieldError({ messages }: { messages?: string[] }) {
  if (!messages?.length) {
    return null
  }

  return <span className={styles.fieldError}>{messages.join(' ')}</span>
}
