import type { ReactNode } from 'react'

import styles from './EmptyState.module.scss'

export function EmptyState({ children }: { children: ReactNode }) {
  return <p className={styles.emptyState}>{children}</p>
}
