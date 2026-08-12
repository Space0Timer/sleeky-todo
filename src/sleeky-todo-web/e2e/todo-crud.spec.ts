import { expect, test, type Locator, type Page } from '@playwright/test'
import { execFile } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

const execFileAsync = promisify(execFile)
const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))

function todoCard(page: Page, name: string): Locator {
  return page.locator('[data-testid^="todo-"]').filter({
    has: page.getByRole('heading', { name, exact: true }),
  })
}

async function createTodo(
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

test.beforeEach(async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Keep today clear.' })).toBeVisible()
  await expect(page.getByText('Loading TODOs…')).toHaveCount(0)
})

test('creates, edits, archives, soft-deletes, and restores a TODO', async ({ page }) => {
  let card = await createTodo(page, 'UI lifecycle TODO')
  await expect(card.getByText('v1')).toBeVisible()

  await card.getByRole('button', { name: 'Manage' }).click()
  await card.getByRole('button', { name: 'Edit details' }).click()
  const editForm = page.getByRole('group', { name: 'Edit UI lifecycle TODO' })
  await editForm.getByLabel('Name').fill('UI lifecycle updated')
  await editForm.getByRole('button', { name: 'Save changes' }).click()

  card = todoCard(page, 'UI lifecycle updated')
  await expect(card.getByText('v2')).toBeVisible()
  await card.getByRole('button', { name: 'Manage' }).click()
  await card.getByLabel('Status for UI lifecycle updated').selectOption({ label: 'Archived' })
  await expect(card).toHaveCount(0)

  await page.getByRole('tab', { name: 'Archived' }).click()
  card = todoCard(page, 'UI lifecycle updated')
  await expect(card.getByText('v3')).toBeVisible()
  await card.getByRole('button', { name: 'Manage' }).click()
  await card.getByLabel('Status for UI lifecycle updated').selectOption({ label: 'Not started' })
  await expect(card).toHaveCount(0)

  await page.getByRole('tab', { name: 'Active' }).click()
  card = todoCard(page, 'UI lifecycle updated')
  await expect(card.getByText('v4')).toBeVisible()
  await card.getByRole('button', { name: 'Delete' }).click()
  await expect(card).toHaveCount(0)

  await page.getByRole('tab', { name: 'Trash' }).click()
  card = todoCard(page, 'UI lifecycle updated')
  await expect(card.getByText('v5')).toBeVisible()
  await expect(card).toContainText('Deleted')
  await expect(card).toContainText('Purge')
  await card.getByRole('button', { name: 'Restore' }).click()
  await expect(card).toHaveCount(0)

  await page.getByRole('tab', { name: 'Active' }).click()
  card = todoCard(page, 'UI lifecycle updated')
  await expect(card.getByText('v6')).toBeVisible()
})

test('shows API validation errors next to their fields', async ({ page }) => {
  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill('   ')
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByRole('button', { name: 'Add TODO' }).click()

  await expect(page.getByRole('alert')).toContainText('Validation failed.')
  await expect(form).toContainText('A TODO name is required.')
})

test('shows a concurrency conflict and reloads the latest version', async ({ page }) => {
  let card = await createTodo(page, 'UI stale TODO')
  const id = (await card.locator('.todo-id').textContent())?.trim()
  expect(id).toBeTruthy()

  await card.getByRole('button', { name: 'Manage' }).click()
  await expect(card.getByRole('region', { name: 'Manage UI stale TODO' })).toBeVisible()

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

  await card.getByRole('button', { name: 'Edit details' }).click()
  const editForm = page.getByRole('group', { name: 'Edit UI stale TODO' })
  await editForm.getByLabel('Name').fill('My stale change')
  await editForm.getByRole('button', { name: 'Save changes' }).click()

  const alert = page.getByRole('alert')
  await expect(alert).toContainText('This TODO was changed by another user.')
  await alert.getByRole('button', { name: 'Reload latest version' }).click()

  card = todoCard(page, 'Changed by another writer')
  await expect(card.getByText('v2')).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('enforces dependency blocking, unblocking, and cycle rules', async ({ page }) => {
  let prerequisite = await createTodo(page, 'UI prerequisite')
  let dependent = await createTodo(page, 'UI dependent')

  await dependent.getByRole('button', { name: 'Manage' }).click()
  await dependent.getByLabel('Dependency for UI dependent')
    .selectOption({ label: 'UI prerequisite' })
  await dependent.getByRole('button', { name: 'Add', exact: true }).click()

  dependent = todoCard(page, 'UI dependent')
  await expect(dependent.getByText('Blocked', { exact: true })).toBeVisible()
  await expect(dependent).toContainText('UI prerequisite')
  await dependent.getByRole('button', { name: 'Manage' }).click()
  const blockedStatus = dependent.getByLabel('Status for UI dependent')
  await expect(blockedStatus.locator('option[value="1"]')).toHaveAttribute('disabled', '')
  await expect(blockedStatus.locator('option[value="2"]')).toHaveAttribute('disabled', '')

  prerequisite = todoCard(page, 'UI prerequisite')
  await prerequisite.getByRole('button', { name: 'Manage' }).click()
  await prerequisite.getByLabel('Status for UI prerequisite').selectOption({ label: 'Completed' })

  dependent = todoCard(page, 'UI dependent')
  await expect(dependent.getByText('Blocked', { exact: true })).toHaveCount(0)
  await dependent.getByRole('button', { name: 'Manage' }).click()
  await dependent.getByLabel('Status for UI dependent').selectOption({ label: 'In progress' })
  await expect(todoCard(page, 'UI dependent').getByText('In progress', { exact: true })).toBeVisible()

  prerequisite = todoCard(page, 'UI prerequisite')
  await prerequisite.getByRole('button', { name: 'Manage' }).click()
  await prerequisite.getByLabel('Dependency for UI prerequisite')
    .selectOption({ label: 'UI dependent' })
  await prerequisite.getByRole('button', { name: 'Add', exact: true }).click()

  await expect(page.getByRole('alert')).toContainText('Adding this dependency would create a cycle.')
})

test('creates the next occurrence when a recurring TODO is completed', async ({ page }) => {
  const card = await createTodo(page, 'UI monthly recurring', {
    dueDate: '2026-08-31',
    recurring: true,
  })
  await expect(card.getByText('Repeats', { exact: true })).toBeVisible()

  await card.getByRole('button', { name: 'Manage' }).click()
  await card.getByLabel('Status for UI monthly recurring').selectOption({ label: 'Completed' })

  await expect(page.getByRole('status')).toContainText('Completed. Next occurrence:')
  const occurrences = todoCard(page, 'UI monthly recurring')
  await expect(occurrences).toHaveCount(2)
  await expect(occurrences.filter({ hasText: '2026-09-30' })).toBeVisible()
})

test('filters, sorts, and loads a second cursor page without duplicates', async ({ page }) => {
  const names = Array.from({ length: 13 }, (_, index) => (
    `UI page ${String(index).padStart(2, '0')}`
  ))
  const documents = names.map((name, index) => ({
    _id: `ui-page-${String(index).padStart(2, '0')}`,
    name,
    nameNormalized: name.toLowerCase(),
    description: 'Cursor acceptance record',
    dueDate: '2027-04-19',
    status: 'NotStarted',
    priority: 'Low',
    dependencyIds: [],
    recurrence: null,
    seriesId: null,
    occurrenceNumber: null,
    version: 1,
    createdAt: '2026-08-12T00:00:00.000Z',
    updatedAt: '2026-08-12T00:00:00.000Z',
    deletedAt: null,
    purgeAt: null,
  }))
  const seedScript = `const docs=${JSON.stringify(documents)}; docs.forEach((doc) => { doc.createdAt=new Date(doc.createdAt); doc.updatedAt=new Date(doc.updatedAt); }); db.getSiblingDB('sleekyTodoPlaywright').todoItems.insertMany(docs);`
  await execFileAsync('docker', [
    'compose',
    'exec',
    '-T',
    'mongodb',
    'mongosh',
    '--quiet',
    '--eval',
    seedScript,
  ], { cwd: repositoryRoot })

  await page.reload()
  await page.getByLabel('Status filter').selectOption({ label: 'Not started' })
  await page.getByLabel('Priority filter').selectOption({ label: 'Low' })
  await page.getByLabel('Due from filter').fill('2027-04-19')
  await page.getByLabel('Due to filter').fill('2027-04-19')
  await page.getByLabel('Dependency filter').selectOption({ label: 'Unblocked' })
  await page.getByLabel('Sort field').selectOption({ label: 'Name' })

  const activeCards = page.getByRole('region', { name: 'Active' })
    .locator('[data-testid^="todo-"]')
  await expect(activeCards).toHaveCount(12)
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible()
  expect(await activeCards.locator('h3').allTextContents()).toEqual(names.slice(0, 12))

  await page.getByRole('button', { name: 'Load more' }).click()
  await expect(activeCards).toHaveCount(13)
  const ids = await activeCards.evaluateAll((cards) => cards.map((card) => card.getAttribute('data-testid')))
  expect(new Set(ids).size).toBe(ids.length)
  expect(await activeCards.locator('h3').allTextContents()).toEqual(names)

  await page.getByLabel('Sort direction').selectOption({ label: 'Descending' })
  await expect(activeCards).toHaveCount(12)
  expect(await activeCards.locator('h3').allTextContents()).toEqual(names.slice(1).reverse())
})
