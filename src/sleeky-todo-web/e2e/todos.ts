import { expect, type Locator, type Page } from '@playwright/test'

import { type TodoStatus } from '../src/types/todo.ts'

export const apiOrigin = 'http://127.0.0.1:5173'

/**
 * Requests made through the Playwright request context carry the session
 * cookie but no antiforgery header, which the API requires for mutations.
 */
export async function antiforgeryHeader(page: Page): Promise<Record<string, string>> {
  const response = await page.request.get(`${apiOrigin}/api/auth/antiforgery`)
  const token = (await response.json()) as { headerName: string; token: string }

  return { [token.headerName]: token.token }
}

/** Every TODO is owner-scoped, so seeded documents need the signed-in user. */
export async function currentUserId(page: Page): Promise<string> {
  const response = await page.request.get(`${apiOrigin}/api/auth/me`)
  const user = (await response.json()) as { userId: string | null }

  if (user.userId === null) {
    throw new Error('The current user endpoint reported no signed-in user.')
  }

  return user.userId
}

export function todoCard(page: Page, name: string): Locator {
  return page.locator('[data-testid^="todo-"]').filter({
    has: page.getByRole('heading', { name, exact: true }),
  })
}

export async function createTodo(
  page: Page,
  name: string,
  options: { dueDate?: string; recurring?: boolean } = {},
): Promise<Locator> {
  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill(name)
  await form.getByLabel('Description').fill(`Details for ${name}`)
  await form.getByLabel('Due date').fill(options.dueDate ?? '2026-08-31')
  await form.getByLabel('Priority').selectOption({ label: 'High' })

  if (options.recurring) {
    await form.getByLabel('Repeat this TODO').check()
    await form.getByLabel('Recurrence frequency').selectOption({ label: 'Monthly' })
  }

  await form.getByRole('button', { name: 'Add TODO' }).click()

  const card = todoCard(page, name)
  await expect(card).toHaveCount(1)
  await expect(card).toBeVisible()
  return card
}

export function selectCard(card: Locator): Promise<void> {
  return card.getByRole('checkbox').check()
}

/**
 * Cards carry their own Delete, so a toolbar action has to be reached through
 * the toolbar rather than by name alone.
 */
export function bulkAction(page: Page, name: string): Locator {
  return page.getByLabel('Bulk actions').getByRole('button', { name, exact: true })
}

/**
 * Moves a TODO behind the running page, which is how a stale version is staged:
 * the list keeps the version it loaded while the store moves on.
 */
export async function changeStatusOutOfBand(
  page: Page,
  id: string,
  version: number,
  status: TodoStatus,
): Promise<void> {
  const response = await page.request.put(`${apiOrigin}/api/todos/${id}/status`, {
    data: { status, version },
    headers: await antiforgeryHeader(page),
  })

  expect(response.ok()).toBeTruthy()
}

/**
 * Reads a TODO's stored version through the selection probe, which reports
 * soft-deleted TODOs, so a version can be staged from the trash as well.
 */
export async function currentVersion(page: Page, id: string): Promise<number> {
  const response = await page.request.get(`${apiOrigin}/api/todos/selection?id=${id}`)
  const selection = (await response.json()) as {
    items: { id: string; version: number }[]
  }
  const found = selection.items.find((item) => item.id === id)

  if (found === undefined) {
    throw new Error(`The TODO ${id} did not resolve through the selection probe.`)
  }

  return found.version
}

/**
 * Restores a TODO behind the running page, which is how a restore conflict is
 * staged: the trash keeps the version it loaded while the store moves on and
 * the document stops being deleted.
 */
export async function restoreOutOfBand(
  page: Page,
  id: string,
  version: number,
): Promise<void> {
  const response = await page.request.post(`${apiOrigin}/api/todos/${id}/restore`, {
    data: { version },
    headers: await antiforgeryHeader(page),
  })

  expect(response.ok()).toBeTruthy()
}

export async function cardId(card: Locator): Promise<string> {
  return (await card.getByTestId('record-id').innerText()).trim()
}
