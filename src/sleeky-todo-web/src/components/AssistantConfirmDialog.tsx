import { type ConfirmationRequest } from '../types/assistant.ts'
import { todoStatusLabels, type TodoVersionReference } from '../types/todo.ts'
import styles from './AssistantConfirmDialog.module.scss'
import { Button } from './common/index.ts'

type AssistantConfirmDialogProps = {
  busy: boolean
  request: ConfirmationRequest
  onCancel: () => void
  onConfirm: (tool: string, items: TodoVersionReference[]) => void
}

/**
 * The assistant's confirmation, shown the same way the bulk delete dialog shows
 * its own: name what is about to happen, to which TODOs, at which state.
 *
 * It does not re-read, and that is the difference from `BulkDeleteDialog`. The
 * proposal already carries the state the server read when it made it, and
 * confirming sends exactly those versions back — which is what makes a repeated
 * confirmation fail on the moved version rather than act on whatever has since
 * taken their place. The browser's own dialog reads on open because nothing
 * else has read for it.
 */
export function AssistantConfirmDialog({
  busy,
  request,
  onCancel,
  onConfirm,
}: AssistantConfirmDialogProps) {
  return (
    <div className={styles.backdrop} role="presentation">
      <dialog
        className={styles.dialog}
        aria-label="Confirm assistant action"
        data-testid="assistant-confirmation"
        open
      >
        <h2>{request.prompt}</h2>

        <ul className={styles.items}>
          {request.items.map((item) => (
            <li key={item.id}>
              {item.name}
              <span className={styles.state}>
                {todoStatusLabels[item.status]}
              </span>
            </li>
          ))}
        </ul>

        <p className={styles.note}>
          Deleted TODOs stay in the trash for ninety days.
        </p>

        <div className={styles.dialogActions}>
          <Button variant="secondary" disabled={busy} onClick={onCancel}>
            Cancel
          </Button>
          <Button
            variant="danger"
            disabled={busy}
            onClick={() => onConfirm(
              request.tool,
              request.items.map((item) => ({ id: item.id, version: item.version })),
            )}
          >
            {busy ? 'Working…' : 'Confirm'}
          </Button>
        </div>
      </dialog>
    </div>
  )
}
