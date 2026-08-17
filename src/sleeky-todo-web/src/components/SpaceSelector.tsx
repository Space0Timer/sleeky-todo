import { useState } from 'react'
import { useNavigate } from 'react-router'

import { useSpaces } from '../spaces/SpaceContext.ts'
import { canWrite, type SpaceSummary } from '../types/space.ts'
import { CreateSpaceDialog } from './CreateSpaceDialog.tsx'
import styles from './SpaceSelector.module.scss'
import { SpaceSettingsDialog } from './SpaceSettingsDialog.tsx'
import { Badge, Button } from './common/index.ts'

type SpaceSelectorProps = {
  space: SpaceSummary
}

/**
 * Which Space the page is showing, and the way to another. Choosing one
 * navigates rather than setting state: the URL is the record of the open
 * Space, and the route remounts the page from it.
 */
export function SpaceSelector({ space }: SpaceSelectorProps) {
  const { spaces } = useSpaces()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  const [managing, setManaging] = useState(false)

  return (
    <div className={styles.spaceSelector}>
      <label className={styles.field}>
        <span className={styles.label}>Space</span>
        <select
          data-testid="space-selector"
          value={space.id}
          onChange={(event) => {
            void navigate(`/spaces/${encodeURIComponent(event.target.value)}`)
          }}
        >
          {spaces.map((candidate) => (
            <option
              data-testid={`space-option-${candidate.id}`}
              key={candidate.id}
              value={candidate.id}
            >
              {candidate.name}
            </option>
          ))}
        </select>
      </label>

      <div className={styles.row}>
        {/*
          Only the absence of write access is worth a badge. Write and Owner
          both see every control, so naming the level would say nothing the
          page does not already show.
        */}
        <span data-testid="space-permission">
          {!canWrite(space.permission) && <Badge tone="neutral">Read-only</Badge>}
        </span>
        <span className={styles.actions}>
          {/*
            Offered to every member, not only to Owners. Who else is in a Space
            is part of knowing what you are working in, and the dialog draws
            only the controls the reader's own level allows.
          */}
          <Button
            data-testid="manage-space"
            variant="text"
            onClick={() => setManaging(true)}
          >
            Manage space…
          </Button>
          <Button
            data-testid="create-space"
            variant="text"
            onClick={() => setCreating(true)}
          >
            New space…
          </Button>
        </span>
      </div>

      {managing && (
        <SpaceSettingsDialog space={space} onClose={() => setManaging(false)} />
      )}

      {creating && (
        <CreateSpaceDialog
          onCancel={() => setCreating(false)}
          onCreated={(created) => {
            setCreating(false)
            void navigate(`/spaces/${encodeURIComponent(created.id)}`)
          }}
        />
      )}
    </div>
  )
}
