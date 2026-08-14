import { useState } from 'react'

import {
  deleteAssistantSettings,
  saveAssistantSettings,
  testAssistantConnection,
} from '../api/assistant.ts'
import {
  assistantProvider,
  assistantProviderLabels,
  type AssistantProvider,
  type AssistantSettings,
  type AssistantSettingsDraft,
} from '../types/assistant.ts'
import styles from './AssistantSettingsForm.module.scss'
import { Button } from './common/index.ts'

type AssistantSettingsFormProps = {
  settings: AssistantSettings
  onChanged: (settings: AssistantSettings | null) => void
}

/**
 * The key field is write-only in both directions: nothing populates it, because
 * no endpoint returns a key, and leaving it empty keeps whatever is stored. The
 * only way to change a key is to type a new one.
 */
export function AssistantSettingsForm({
  settings,
  onChanged,
}: AssistantSettingsFormProps) {
  const [draft, setDraft] = useState<AssistantSettingsDraft>({
    provider: readProvider(settings.provider),
    baseUrl: settings.baseUrl ?? '',
    model: settings.model,
    apiKey: '',
  })
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string | null>(null)

  const save = async () => {
    setBusy(true)
    setStatus(null)
    try {
      const saved = await saveAssistantSettings(draft)
      setDraft((current) => ({ ...current, apiKey: '' }))
      onChanged(saved)
      setStatus('Saved.')
    } catch (caught) {
      setStatus(caught instanceof Error ? caught.message : 'Could not save.')
    } finally {
      setBusy(false)
    }
  }

  const test = async () => {
    setBusy(true)
    setStatus(null)
    try {
      const result = await testAssistantConnection()
      setStatus(result.succeeded
        ? 'The provider answered.'
        : result.error ?? 'The provider did not answer.')
    } catch (caught) {
      setStatus(caught instanceof Error ? caught.message : 'Could not reach the provider.')
    } finally {
      setBusy(false)
    }
  }

  const remove = async () => {
    setBusy(true)
    setStatus(null)
    try {
      await deleteAssistantSettings()
      onChanged(null)
    } catch (caught) {
      setStatus(caught instanceof Error ? caught.message : 'Could not remove.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <fieldset className={styles.form} disabled={busy}>
      <legend className={styles.legend}>Assistant provider</legend>

      <label className={styles.field}>
        Provider
        <select
          aria-label="Provider"
          value={draft.provider}
          onChange={(event) => setDraft({
            ...draft,
            provider: event.target.value as AssistantProvider,
          })}
        >
          {Object.values(assistantProvider).map((provider) => (
            <option key={provider} value={provider}>
              {assistantProviderLabels[provider]}
            </option>
          ))}
        </select>
      </label>

      <label className={styles.field}>
        Model
        <input
          aria-label="Model"
          value={draft.model}
          onChange={(event) => setDraft({ ...draft, model: event.target.value })}
        />
      </label>

      {draft.provider === assistantProvider.openAiCompatible && (
        <label className={styles.field}>
          Base URL
          <input
            aria-label="Base URL"
            placeholder="https://openrouter.ai/api/v1"
            value={draft.baseUrl}
            onChange={(event) => setDraft({ ...draft, baseUrl: event.target.value })}
          />
        </label>
      )}

      <label className={styles.field}>
        API key
        <input
          aria-label="API key"
          type="password"
          autoComplete="off"
          placeholder={settings.hasKey ? 'Stored — type to replace' : 'Required'}
          value={draft.apiKey}
          onChange={(event) => setDraft({ ...draft, apiKey: event.target.value })}
        />
      </label>

      <p className={styles.note}>
        Your key is stored encrypted and never shown again. Turns run on it, so
        the tokens they spend are yours.
      </p>

      {status !== null && (
        <p className={styles.status} data-testid="assistant-settings-status">
          {status}
        </p>
      )}

      <div className={styles.actions}>
        {settings.hasKey && (
          <Button variant="text" onClick={() => void remove()}>
            Remove
          </Button>
        )}
        <Button variant="secondary" onClick={() => void test()}>
          Test
        </Button>
        <Button
          variant="primary"
          disabled={draft.model.trim() === ''}
          onClick={() => void save()}
        >
          {busy ? 'Working…' : 'Save'}
        </Button>
      </div>
    </fieldset>
  )
}

/**
 * The server reports the provider it has stored, which may predate a rename or
 * come from application configuration. An unrecognised name falls back rather
 * than leaving the select with no matching option.
 */
function readProvider(value: string): AssistantProvider {
  return Object.values(assistantProvider).find((provider) => provider === value)
    ?? assistantProvider.anthropic
}
