import { expect, type Page } from '@playwright/test'

export const stubProviderUrl = 'http://127.0.0.1:4599'

/** One round of the function-calling loop: either the model speaks, or it calls a tool. */
export type ModelReply =
  | { text: string }
  | { tool: string; arguments?: Record<string, unknown> }

/**
 * Writes what the model will say next. Replies are consumed one per round, so
 * a script reads as the sequence of calls a model would make.
 */
export async function scriptModel(page: Page, replies: ModelReply[]): Promise<void> {
  const response = await page.request.post(`${stubProviderUrl}/__script`, {
    data: replies,
  })

  expect(response.ok()).toBeTruthy()
}

/**
 * Points the assistant at the stub through its own settings form rather than
 * through configuration, so the turn runs on exactly the path a user's own
 * provider would.
 */
export async function configureAssistant(page: Page): Promise<void> {
  const panel = page.getByRole('region', { name: 'Assistant' })

  await panel.getByRole('button', { name: 'Settings' }).click()
  await panel.getByLabel('Provider').selectOption({ label: 'OpenAI-compatible' })
  await panel.getByLabel('Model').fill('stub')
  await panel.getByLabel('Base URL').fill(stubProviderUrl)
  await panel.getByLabel('API key').fill('stub-key')
  await panel.getByRole('button', { name: 'Save' }).click()

  await expect(page.getByTestId('assistant-settings-status')).toHaveText('Saved.')
  await panel.getByRole('button', { name: 'Close settings' }).click()
}

export async function ask(page: Page, message: string): Promise<void> {
  const panel = page.getByRole('region', { name: 'Assistant' })

  await panel.getByLabel('Ask the assistant').fill(message)
  await panel.getByRole('button', { name: 'Send' }).click()
}
