import {
  maximumBulkSelection,
  todoScope,
  todoStatus,
  type TodoScope,
  type TodoStatus,
} from '../types/todo.ts'
import styles from './BulkToolbar.module.scss'
import { Button } from './common/index.ts'

type BulkToolbarProps = {
  busy: boolean
  loadedCount: number
  overLimit: boolean
  selectedCount: number
  selectedStatuses: Set<TodoStatus>
  scope: TodoScope
  onDelete: () => void
  onRestore: () => void
  onSelectLoaded: (select: boolean) => void
  onStatus: (status: TodoStatus) => void
}

export function BulkToolbar({
  busy,
  loadedCount,
  overLimit,
  selectedCount,
  selectedStatuses,
  scope,
  onDelete,
  onRestore,
  onSelectLoaded,
  onStatus,
}: BulkToolbarProps) {
  const allLoadedSelected = loadedCount > 0 && selectedCount === loadedCount
  const disabled = busy || selectedCount === 0 || overLimit

  // Reopening demotes work that is already under way, which is rarely what a
  // mixed selection means, so it is withheld rather than silently applied.
  const demotesInProgress = selectedStatuses.has(todoStatus.inProgress)

  return (
    <section className={styles.bulkToolbar} aria-label="Bulk actions">
      <label className={styles.selectAll}>
        <input
          checked={allLoadedSelected}
          disabled={busy || loadedCount === 0}
          type="checkbox"
          onChange={(event) => onSelectLoaded(event.target.checked)}
        />
        Select loaded ({loadedCount})
      </label>

      <span className={styles.count} data-testid="bulk-selected-count">
        {selectedCount} selected
      </span>

      <div className={styles.actions}>
        {scope === todoScope.active && (
          <>
            <Button
              variant="primary"
              disabled={disabled}
              onClick={() => onStatus(todoStatus.completed)}
            >
              Complete
            </Button>
            <Button
              variant="secondary"
              disabled={disabled || demotesInProgress}
              title={demotesInProgress
                ? 'Deselect in-progress TODOs to reopen the rest.'
                : undefined}
              onClick={() => onStatus(todoStatus.open)}
            >
              Reopen
            </Button>
            <Button
              variant="secondary"
              disabled={disabled}
              onClick={() => onStatus(todoStatus.archived)}
            >
              Archive
            </Button>
          </>
        )}

        {scope === todoScope.archived && (
          <Button
            variant="primary"
            disabled={disabled}
            onClick={() => onStatus(todoStatus.open)}
          >
            Unarchive
          </Button>
        )}

        {/*
          Trash offers recovery only. Deletion there would mean purging, which
          the retention window owns rather than the user.
        */}
        {scope === todoScope.deleted ? (
          <Button variant="primary" disabled={disabled} onClick={onRestore}>
            Restore
          </Button>
        ) : (
          <Button variant="danger" disabled={disabled} onClick={onDelete}>
            Delete
          </Button>
        )}
      </div>

      {overLimit && (
        <p className={styles.limitNote} role="alert">
          Bulk actions apply to at most {maximumBulkSelection} TODOs
          ({selectedCount} selected). Deselect some to continue.
        </p>
      )}

      {demotesInProgress && scope === todoScope.active && (
        <p className={styles.limitNote}>
          Reopen is unavailable while the selection contains in-progress TODOs.
        </p>
      )}
    </section>
  )
}
