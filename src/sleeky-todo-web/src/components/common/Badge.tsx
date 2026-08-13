import type { ReactNode } from 'react'

import styles from './Badge.module.scss'

/**
 * Tones are named for the meaning they carry rather than the field that uses
 * them, so a priority and a status that read the same share one tone.
 */
export type BadgeTone =
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'neutral'
  | 'pending'
  | 'accent'
  | 'version'

export function Badge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return <span className={`${styles.badge} ${styles[tone]}`}>{children}</span>
}
