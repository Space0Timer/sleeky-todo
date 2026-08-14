import { useEffect, useState } from 'react'

import { lookupTodoSelection } from '../api/todos.ts'
import {
  todoStatusLabels,
  type Todo,
  type TodoVersionReference,
} from '../types/todo.ts'
import styles from './BulkDeleteDialog.module.scss'
import { Button } from './common/index.ts'

type BulkDeleteDialogProps = {
  busy: boolean
  selection: TodoVersionReference[]
  onCancel: () => void
  onConfirm: (selection: TodoVersionReference[]) => void
}

/**
 * Deletion is the one batch whose intent can invert when the world moves under
 * it, so the dialog reads the selection's current state on open and confirms
 * with the versions it displayed rather than the ones the user selected with.
 */
export function BulkDeleteDialog({
  busy,
  selection,
  onCancel,
  onConfirm,
}: BulkDeleteDialogProps) {
  const [current, setCurrent] = useState<Todo[] | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let cancelled = false
    const ids = selection.map((reference) => reference.id)

    void lookupTodoSelection(ids).then((result) => {
      if (!cancelled) setCurrent(result.items)
    }).catch(() => {
      if (!cancelled) setFailed(true)
    })

    return () => { cancelled = true }
  }, [selection])

  const sentById = new Map(selection.map((reference) => [reference.id, reference.version]))
  const drifted = (current ?? []).filter(
    (todo) => sentById.get(todo.id) !== todo.version,
  )
  const vanished = current === null
    ? 0
    : selection.length - current.length

  return (
    <div className={styles.backdrop} role="presentation">
      <dialog className={styles.dialog} aria-label="Confirm bulk deletion" open>
        <h2>Delete {selection.length} TODO(s)?</h2>

        {failed ? (
          <p className={styles.warning}>
            Their current state could not be read. Close this and try again.
          </p>
        ) : current === null ? (
          <p>Checking their current state…</p>
        ) : (
          <>
            {drifted.length === 0 && vanished === 0 && (
              <p>They are unchanged since you selected them.</p>
            )}
            {drifted.length > 0 && (
              <div className={styles.warning}>
                <strong>{drifted.length} changed since you selected them:</strong>
                <ul>
                  {drifted.map((todo) => (
                    <li key={todo.id}>
                      {todo.name} — now {todoStatusLabels[todo.status]}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {vanished > 0 && (
              <p className={styles.warning}>
                {vanished} no longer exist and will be skipped.
              </p>
            )}
          </>
        )}

        <div className={styles.dialogActions}>
          <Button variant="secondary" disabled={busy} onClick={onCancel}>
            Cancel
          </Button>
          <Button
            variant="danger"
            disabled={busy || failed || current === null || current.length === 0}
            onClick={() => onConfirm(
              (current ?? []).map((todo) => ({ id: todo.id, version: todo.version })),
            )}
          >
            {busy ? 'Deleting…' : 'Delete'}
          </Button>
        </div>
      </dialog>
    </div>
  )
}
