import { useState, type FormEvent } from 'react'

import { ApiError } from '../api/http.ts'
import { useSpaces } from '../spaces/SpaceContext.ts'
import { maximumSpaceNameLength, type Space } from '../types/space.ts'
import styles from './CreateSpaceDialog.module.scss'
import { Button, FieldError } from './common/index.ts'

type CreateSpaceDialogProps = {
  onCancel: () => void
  onCreated: (space: Space) => void
}

/**
 * The one thing a user needs to start sharing: a Space with a name. Sharing
 * itself is a separate dialog; this only creates the container and hands the
 * caller the result, which the caller navigates to.
 */
export function CreateSpaceDialog({ onCancel, onCreated }: CreateSpaceDialogProps) {
  const { createSpace } = useSpaces()
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [nameErrors, setNameErrors] = useState<string[] | undefined>(undefined)
  const [failure, setFailure] = useState<string | null>(null)

  /**
   * The same rule the server applies, checked first so a blank or overlong
   * name is reported without a round trip. The server's answer still wins when
   * it disagrees: its errors replace these.
   */
  function validate(candidate: string): string[] | undefined {
    if (candidate === '') return ['A space name is required.']
    if (candidate.length > maximumSpaceNameLength) {
      return [`A space name is at most ${maximumSpaceNameLength} characters.`]
    }
    return undefined
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const trimmed = name.trim()
    const problems = validate(trimmed)
    setNameErrors(problems)
    setFailure(null)
    if (problems !== undefined) return

    setBusy(true)
    try {
      onCreated(await createSpace(trimmed))
    } catch (caught) {
      if (caught instanceof ApiError && caught.kind === 'validation') {
        setNameErrors(caught.problem.errors?.name ?? [caught.message])
      } else {
        setFailure(caught instanceof Error ? caught.message : 'The space could not be created.')
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={styles.backdrop} role="presentation">
      <dialog
        className={styles.dialog}
        aria-label="Create a space"
        data-testid="create-space-dialog"
        open
        onKeyDown={(event) => {
          if (event.key === 'Escape' && !busy) onCancel()
        }}
      >
        <h2>Create a space</h2>
        <p>A space is a TODO list you can share. You start as its owner.</p>

        <form className={styles.form} onSubmit={(event) => void handleSubmit(event)} noValidate>
          <fieldset disabled={busy}>
            <label>
              Name
              <input
                autoFocus
                data-testid="create-space-name"
                maxLength={maximumSpaceNameLength}
                name="name"
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
              <FieldError messages={nameErrors} />
            </label>

            {failure !== null && (
              <div className={styles.failure} role="alert">{failure}</div>
            )}

            <div className={styles.dialogActions}>
              <Button variant="secondary" onClick={onCancel}>
                Cancel
              </Button>
              <Button
                data-testid="create-space-submit"
                type="submit"
                variant="primary"
              >
                {busy ? 'Creating…' : 'Create'}
              </Button>
            </div>
          </fieldset>
        </form>
      </dialog>
    </div>
  )
}
