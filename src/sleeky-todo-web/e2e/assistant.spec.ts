import { expect, test } from '@playwright/test'

import { signIn } from './auth.ts'
import { resetOwnedData } from './database.ts'
import { apiOrigin } from './todos.ts'

/**
 * These cover the paths that need no provider: the panel, the settings form,
 * and the write-only rule around the key. A turn that actually calls a model
 * needs a real key and a real provider, so the loop, the tool layer, and the
 * confirmation gate are covered against a scripted chat client in
 * `Sleeky.Todo.Assistant.Tests` instead of here.
 */
test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetOwnedData(page)
})

test('the assistant panel asks for a provider before it can help', async ({ page }) => {
  const panel = page.getByRole('region', { name: 'Assistant' })

  await expect(panel).toBeVisible()
  await expect(page.getByTestId('assistant-not-configured')).toBeVisible()
})

test('a turn with no provider configured answers rather than failing', async ({ page }) => {
  const panel = page.getByRole('region', { name: 'Assistant' })

  await panel.getByLabel('Ask the assistant').fill('What is due today?')
  await panel.getByRole('button', { name: 'Send' }).click()

  // The stream is read off the response body, so the answer arriving at all is
  // the evidence that parsing worked end to end.
  await expect(page.getByTestId('assistant-assistant')).toContainText(
    /assistant settings/i,
  )
  await expect(page.getByTestId('assistant-user')).toContainText('What is due today?')
})

test('a saved key is never handed back to the browser', async ({ page }) => {
  const secret = 'sk-e2e-secret-value'
  const panel = page.getByRole('region', { name: 'Assistant' })

  await panel.getByRole('button', { name: 'Settings' }).click()
  await panel.getByLabel('Model').fill('claude-sonnet-5')
  await panel.getByLabel('API key').fill(secret)
  await panel.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByTestId('assistant-settings-status')).toHaveText('Saved.')

  // The field is cleared rather than repopulated, because there is nothing to
  // repopulate it from.
  await expect(panel.getByLabel('API key')).toHaveValue('')
  await expect(panel.getByLabel('API key')).toHaveAttribute(
    'placeholder',
    /Stored/,
  )

  const settings = await page.request.get(`${apiOrigin}/api/assistant/settings`)
  const body = await settings.text()

  expect(body).not.toContain(secret)
  expect(body).toContain('"hasKey":true')
})

test('removing a provider clears the stored key', async ({ page }) => {
  const panel = page.getByRole('region', { name: 'Assistant' })

  await panel.getByRole('button', { name: 'Settings' }).click()
  await panel.getByLabel('Model').fill('claude-sonnet-5')
  await panel.getByLabel('API key').fill('sk-e2e-removable')
  await panel.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByTestId('assistant-settings-status')).toHaveText('Saved.')

  await panel.getByRole('button', { name: 'Remove' }).click()

  await expect(panel.getByLabel('API key')).toHaveAttribute(
    'placeholder',
    'Required',
  )
})
