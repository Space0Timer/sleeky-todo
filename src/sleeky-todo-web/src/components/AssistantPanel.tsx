import { useCallback, useEffect, useState } from 'react'

import { getAssistantSettings } from '../api/assistant.ts'
import { useAssistant } from '../hooks/useAssistant.ts'
import { type AssistantSettings } from '../types/assistant.ts'
import { AssistantConfirmDialog } from './AssistantConfirmDialog.tsx'
import styles from './AssistantPanel.module.scss'
import { AssistantSettingsForm } from './AssistantSettingsForm.tsx'
import { Button } from './common/index.ts'

type AssistantPanelProps = {
  onTodosChanged: () => void
}

/**
 * The assistant, alongside the list rather than in place of it. Every write it
 * makes is one the toolbar could have made, so the list stays the source of
 * truth and refreshes from `todos_changed` exactly as a bulk action does.
 */
export function AssistantPanel({ onTodosChanged }: AssistantPanelProps) {
  const [settings, setSettings] = useState<AssistantSettings | null>(null)
  const [showSettings, setShowSettings] = useState(false)
  const [draft, setDraft] = useState('')
  const assistant = useAssistant({ onTodosChanged })

  const load = useCallback(async () => {
    try {
      setSettings(await getAssistantSettings())
    } catch {
      setSettings(null)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const submit = () => {
    const message = draft.trim()
    if (message === '' || assistant.pending) return

    setDraft('')
    void assistant.ask(message)
  }

  return (
    <section className={styles.panel} aria-label="Assistant">
      <header className={styles.header}>
        <h2>Assistant</h2>
        <div className={styles.headerActions}>
          {assistant.entries.length > 0 && (
            <Button variant="text" onClick={assistant.reset}>
              New chat
            </Button>
          )}
          <Button
            variant="text"
            aria-expanded={showSettings}
            onClick={() => setShowSettings((current) => !current)}
          >
            {showSettings ? 'Close settings' : 'Settings'}
          </Button>
        </div>
      </header>

      {showSettings && settings !== null && (
        <AssistantSettingsForm
          settings={settings}
          onChanged={(next) => {
            if (next === null) void load()
            else setSettings(next)
          }}
        />
      )}

      {settings !== null && !settings.isUsable && !showSettings && (
        <p className={styles.notice} data-testid="assistant-not-configured">
          No AI provider is set up yet. Open settings to add one.
        </p>
      )}

      <ol className={styles.transcript} data-testid="assistant-transcript">
        {assistant.entries.map((entry, index) => (
          <li
            // Entries are append-only and never reordered, so their position is
            // a stable identity; nothing else about them is unique.
            key={index}
            className={styles[entry.kind]}
            data-testid={`assistant-${entry.kind}`}
          >
            {entry.kind === 'tool' ? entry.summary : entry.text}
          </li>
        ))}
        {assistant.pending && <li className={styles.working}>Working…</li>}
      </ol>

      {assistant.error !== null && (
        <p className={styles.error} data-testid="assistant-error">
          {assistant.error}
        </p>
      )}

      <div className={styles.composer}>
        <label className={styles.composerField}>
          <span className={styles.composerLabel}>Ask the assistant</span>
          <textarea
            aria-label="Ask the assistant"
            rows={2}
            value={draft}
            disabled={assistant.pending}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault()
                submit()
              }
            }}
          />
        </label>
        <Button
          variant="primary"
          disabled={assistant.pending || draft.trim() === ''}
          onClick={submit}
        >
          Send
        </Button>
      </div>

      {assistant.confirmation !== null && (
        <AssistantConfirmDialog
          busy={assistant.pending}
          request={assistant.confirmation}
          onCancel={assistant.cancel}
          onConfirm={(tool, items) => void assistant.confirm(tool, items)}
        />
      )}
    </section>
  )
}
