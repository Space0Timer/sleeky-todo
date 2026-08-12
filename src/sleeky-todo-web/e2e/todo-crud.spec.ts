import { expect, test, type Locator, type Page } from '@playwright/test'

async function createTodo(
  page: Page,
  name = 'Submit report',
): Promise<Locator> {
  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill(name)
  await form.getByLabel('Description').fill('Monthly project report')
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByLabel('Priority').selectOption({ label: 'High' })
  await form.getByRole('button', { name: 'Add TODO' }).click()

  const card = page.locator('[data-testid^="todo-"]').filter({ hasText: name })
  await expect(card).toBeVisible()
  return card
}

test.beforeEach(async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Keep today clear.' })).toBeVisible()
})

test('creates, edits, soft-deletes, and restores a TODO', async ({ page }) => {
  let card = await createTodo(page)
  await expect(card.getByText('v1')).toBeVisible()

  await card.getByRole('button', { name: 'Edit' }).click()
  const editForm = card.getByRole('group', { name: 'Edit Submit report' })
  await editForm.getByLabel('Name').fill('Review report')
  await editForm.getByRole('button', { name: 'Save changes' }).click()

  card = page.locator('[data-testid^="todo-"]').filter({ hasText: 'Review report' })
  await expect(card.getByText('v2')).toBeVisible()
  await card.getByRole('button', { name: 'Delete' }).click()

  const deletedSection = page.getByRole('region', { name: 'Recently deleted' })
  await expect(deletedSection).toContainText('Review report')
  await expect(deletedSection.getByText('v3')).toBeVisible()
  await deletedSection.getByRole('button', { name: 'Restore' }).click()

  const activeSection = page.getByRole('region', { name: 'Active' })
  await expect(activeSection).toContainText('Review report')
  await expect(activeSection.getByText('v4')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Recently deleted' })).toHaveCount(0)
})

test('shows API validation errors next to their fields', async ({ page }) => {
  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill('   ')
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByRole('button', { name: 'Add TODO' }).click()

  await expect(page.getByRole('alert')).toContainText('Validation failed.')
  await expect(form).toContainText('A TODO name is required.')
})

test('shows a concurrency conflict and can load the latest version', async ({ page }) => {
  let card = await createTodo(page, 'Coordinate release')
  const id = (await card.locator('.todo-id').textContent())?.trim()
  expect(id).toBeTruthy()

  const response = await page.request.put(`http://127.0.0.1:5173/api/todos/${id}`, {
    data: {
      name: 'Changed by another writer',
      description: 'External change',
      dueDate: '2026-09-01',
      priority: 1,
      version: 1,
    },
  })
  expect(response.ok()).toBeTruthy()

  await card.getByRole('button', { name: 'Edit' }).click()
  const editForm = card.getByRole('group', { name: 'Edit Coordinate release' })
  await editForm.getByLabel('Name').fill('My stale change')
  await editForm.getByRole('button', { name: 'Save changes' }).click()

  const alert = page.getByRole('alert')
  await expect(alert).toContainText('Concurrency conflict.')
  await alert.getByRole('button', { name: 'Load latest version' }).click()

  card = page.locator('[data-testid^="todo-"]').filter({ hasText: 'Changed by another writer' })
  await expect(card.getByText('v2')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})
