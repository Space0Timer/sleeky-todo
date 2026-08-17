import { expect, test } from '@playwright/test'

import { signIn } from './auth.ts'
import { resetUserData } from './database.ts'
import { renameSeeded, seedTodos } from './seed.ts'
import {
  antiforgeryHeader,
  apiOrigin,
  createTodo,
  currentSpaceId,
  currentUserId,
  todoCard,
} from './todos.ts'

test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
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
  await card.getByLabel('Status for UI lifecycle updated').selectOption({ label: 'Open' })
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

/**
 * The two toasts leave on different terms. A success message has been read by
 * the time it is on screen, so it clears itself; an error can carry a trace ID
 * or the reload action, so it waits to be dismissed.
 *
 * Dismissing it keeps the per-field messages, because the toast only summarises
 * the problem that produced them and the form is where it gets fixed.
 */
test('expires a success toast and holds an error until it is dismissed', async ({ page }) => {
  await createTodo(page, 'UI toast lifecycle')

  const notice = page.getByRole('status')
  await expect(notice).toContainText('TODO created.')
  // Outlives the assertion default, because the toast is on its own timer.
  await expect(notice).toHaveCount(0, { timeout: 15_000 })

  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill('   ')
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByRole('button', { name: 'Add TODO' }).click()

  const error = page.getByRole('alert')
  await expect(error).toContainText('Validation failed.')

  // Waited out rather than polled: what is asserted is that nothing happens
  // well past the point the success toast would have cleared itself.
  await page.waitForTimeout(8_000)
  await expect(error).toContainText('Validation failed.')

  await error.getByRole('button', { name: 'Dismiss' }).click()
  await expect(page.getByRole('alert')).toHaveCount(0)
  await expect(form).toContainText('A TODO name is required.')
})

test('shows a concurrency conflict and reloads the latest version', async ({ page }) => {
  let card = await createTodo(page, 'UI stale TODO')
  const id = (await card.locator('[data-testid="record-id"]').textContent())?.trim()
  expect(id).toBeTruthy()

  await card.getByRole('button', { name: 'Manage' }).click()
  await expect(card.getByRole('region', { name: 'Manage UI stale TODO' })).toBeVisible()

  const spaceId = await currentSpaceId(page)
  const response = await page.request.put(
    `${apiOrigin}/api/spaces/${spaceId}/todos/${id}`,
    {
      headers: await antiforgeryHeader(page),
      data: {
        name: 'Changed by another writer',
        description: 'External change',
        dueDate: '2026-09-01',
        priority: 1,
        version: 1,
      },
    },
  )
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
  const spaceId = await currentSpaceId(page)
  const createdByUserId = await currentUserId(page)
  const names = Array.from({ length: 13 }, (_, index) => (
    `UI page ${String(index).padStart(2, '0')}`
  ))
  await seedTodos(spaceId, createdByUserId, names)

  await page.reload()
  await page.getByLabel('Status filter').selectOption({ label: 'Open' })
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

/**
 * Keyset pagination reads live data rather than a snapshot, so an edit landing
 * between two page fetches can push a TODO the user has already seen past the
 * cursor, and the next page carries it a second time. The API is answering both
 * requests correctly, which is what makes this the client's problem: it holds
 * the only copy of the earlier page.
 *
 * The rename runs against MongoDB instead of a second browser tab because the
 * point is the timing, not the mechanism. It has to land after the first page
 * is on screen and before the second is asked for, which a UI edit cannot
 * promise, and this is the same drift a second user editing their shared view
 * would produce.
 */
test('shows one card when an edit moves a seen TODO onto the next page', async ({ page }) => {
  const spaceId = await currentSpaceId(page)
  const createdByUserId = await currentUserId(page)
  const names = Array.from({ length: 13 }, (_, index) => (
    `UI drift ${String(index).padStart(2, '0')}`
  ))
  await seedTodos(spaceId, createdByUserId, names)

  await page.reload()
  await page.getByLabel('Sort field').selectOption({ label: 'Name' })

  const activeCards = page.getByRole('region', { name: 'Active' })
    .locator('[data-testid^="todo-"]')
  await expect(activeCards).toHaveCount(12)
  expect(await activeCards.locator('h3').allTextContents()).toEqual(names.slice(0, 12))

  // Renaming the first card to sort last moves it behind the cursor the page
  // is holding, so the second page returns it alongside the one TODO that has
  // not been seen yet. The version has to rise with it: that is how the client
  // tells the newer read from the copy it is already showing.
  const drifted = 'UI drift 99'
  await renameSeeded(0, drifted)

  await page.getByRole('button', { name: 'Load more' }).click()

  // Thirteen TODOs exist and thirteen cards are on screen, each a distinct
  // record: the drifted one is not counted twice.
  await expect(activeCards).toHaveCount(13)
  const ids = await activeCards.evaluateAll((cards) => cards.map((card) => card.getAttribute('data-testid')))
  expect(new Set(ids).size).toBe(ids.length)

  // It also carries the new name rather than the stale one, and holds the place
  // the user last saw it rather than jumping to where it now sorts.
  expect(await activeCards.locator('h3').allTextContents()).toEqual([
    drifted,
    ...names.slice(1),
  ])
})
