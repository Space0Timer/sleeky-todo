import type { ButtonHTMLAttributes } from 'react'

import styles from './Button.module.scss'

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'text'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant: ButtonVariant
}

/**
 * `type` defaults to `button` because the HTML default is `submit`, which makes
 * any button placed in a form submit it unless every call site remembers
 * otherwise. The two buttons that do submit say so explicitly.
 *
 * The `text` variant reads as a link rather than a control, so it replaces the
 * shared button box instead of adding a tone to it.
 */
export function Button({ variant, className, type = 'button', ...rest }: ButtonProps) {
  const variantClass = variant === 'text'
    ? styles.text
    : `${styles.button} ${styles[variant]}`

  return (
    <button
      className={className ? `${variantClass} ${className}` : variantClass}
      type={type}
      {...rest}
    />
  )
}
