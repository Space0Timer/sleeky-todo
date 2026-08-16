import type { ReactNode } from 'react'

import styles from './Toast.module.scss'

export type ToastTone = 'error' | 'notice'

type ToastProps = {
  children?: ReactNode
  detail?: string
  meta?: string
  title: string
  tone: ToastTone
  onDismiss: () => void
}

/**
 * The stack the toasts are drawn in. It carries no live-region semantics of its
 * own: each toast announces itself, and an `aria-live` container wrapped around
 * a `role="alert"` would announce the same message a second time.
 */
export function ToastRegion({ children }: { children: ReactNode }) {
  return <div className={styles.toastRegion}>{children}</div>
}

/**
 * Page-level feedback that floats over the page instead of displacing it. The
 * banners this replaced pushed the list and the create form down at the moment
 * something went wrong, which moved the controls out from under the cursor that
 * had just failed to use them.
 *
 * An error is a `section role="alert"` and a notice an `output`, which are the
 * elements the banners used, so both keep the roles assistive technology and
 * the end-to-end suite already resolve them by.
 */
export function Toast({ children, detail, meta, title, tone, onDismiss }: ToastProps) {
  const content = (
    <>
      <div className={styles.body}>
        <strong>{title}</strong>
        {detail && <p>{detail}</p>}
        {meta && <small>{meta}</small>}
      </div>
      <button
        aria-label="Dismiss"
        className={styles.dismiss}
        type="button"
        onClick={onDismiss}
      >
        ×
      </button>
      {children && <div className={styles.actions}>{children}</div>}
    </>
  )

  const className = `${styles.toast} ${tone === 'error' ? styles.error : styles.notice}`

  return tone === 'error'
    ? <section className={className} role="alert">{content}</section>
    : <output className={className}>{content}</output>
}
