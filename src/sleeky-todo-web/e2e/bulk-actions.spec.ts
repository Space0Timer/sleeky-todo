import { expect, test } from '@playwright/test'

import { todoStatus } from '../src/types/todo.ts'
import { signIn } from './auth.ts'
import { resetOwnedData } from './database.ts'
import {
  bulkAction,
  cardId,
  changeStatusOutOfBand,
  createTodo,
  currentVersion,
  restoreOutOfBand,
  selectCard,
  todoCard,
} from './todos.ts'

test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetOwnedData(page)
})

test('completes several selected TODOs in one request', async ({ page }) => {
  const first = await createTodo(page, 'Bulk complete one')
  const second = await createTodo(page, 'Bulk complete two')

  await selectCard(first)
  await selectCard(second)
  await expect(page.getByTestId('bulk-selected-count')).toHaveText('2 selected')

  await bulkAction(page, 'Complete').click()

  await expect(todoCard(page, 'Bulk complete one').getByText('Completed')).toBeVisible()
  await expect(todoCard(page, 'Bulk complete two').getByText('Completed')).toBeVisible()
})

test('selects every loaded TODO and archives them together', async ({ page }) => {
  await createTodo(page, 'Select all one')
  await createTodo(page, 'Select all two')

  await page.getByRole('checkbox', { name: /Select loaded/ }).check()
  await bulkAction(page, 'Archive').click()

  await expect(todoCard(page, 'Select all one')).toHaveCount(0)
  await expect(todoCard(page, 'Select all two')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Archived' }).click()
  await expect(todoCard(page, 'Select all one')).toHaveCount(1)
  await expect(todoCard(page, 'Select all two')).toHaveCount(1)
})

test('unarchives a selection from the archived scope', async ({ page }) => {
  const card = await createTodo(page, 'Bulk unarchive')
  await selectCard(card)
  await bulkAction(page, 'Archive').click()
  await expect(todoCard(page, 'Bulk unarchive')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Archived' }).click()
  await selectCard(todoCard(page, 'Bulk unarchive'))
  await bulkAction(page, 'Unarchive').click()
  await expect(todoCard(page, 'Bulk unarchive')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Active' }).click()
  await expect(todoCard(page, 'Bulk unarchive').getByText('Not started')).toBeVisible()
})

test('an archived TODO offers no editing, prerequisites, or completion', async ({ page }) => {
  const card = await createTodo(page, 'Frozen card')
  await selectCard(card)
  await bulkAction(page, 'Archive').click()

  await page.getByRole('tab', { name: 'Archived' }).click()
  const archived = todoCard(page, 'Frozen card')
  await archived.getByRole('button', { name: 'Manage' }).click()

  await expect(archived.getByRole('button', { name: 'Edit details' })).toHaveCount(0)
  await expect(archived.getByLabel('Dependency for Frozen card')).toHaveCount(0)

  // The select stays: moving out of Archived is how a TODO is unarchived. Only
  // the transition the domain refuses is withheld.
  const status = archived.getByLabel('Status for Frozen card')
  await expect(status.getByRole('option', { name: 'Completed' })).toHaveCount(0)
  await expect(status.getByRole('option', { name: 'Not started' })).toHaveCount(1)
})

test('restores a selection from the trash', async ({ page }) => {
  const first = await createTodo(page, 'Bulk restore one')
  const second = await createTodo(page, 'Bulk restore two')

  await selectCard(first)
  await selectCard(second)
  await bulkAction(page, 'Delete').click()
  await page.getByRole('dialog', { name: 'Confirm bulk deletion' })
    .getByRole('button', { name: 'Delete' }).click()
  await expect(todoCard(page, 'Bulk restore one')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Trash' }).click()
  await page.getByRole('checkbox', { name: /Select loaded/ }).check()
  await bulkAction(page, 'Restore').click()
  await expect(todoCard(page, 'Bulk restore one')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Active' }).click()
  await expect(todoCard(page, 'Bulk restore one')).toHaveCount(1)
  await expect(todoCard(page, 'Bulk restore two')).toHaveCount(1)
})

test('the delete dialog reports a TODO that changed since it was selected', async ({ page }) => {
  const stable = await createTodo(page, 'Delete stable')
  const drifting = await createTodo(page, 'Delete drifting')
  const driftingId = await cardId(drifting)

  await selectCard(stable)
  await selectCard(drifting)

  // Move it behind the page, so the list still holds the version it loaded.
  await changeStatusOutOfBand(page, driftingId, 1, todoStatus.inProgress)

  await bulkAction(page, 'Delete').click()
  const dialog = page.getByRole('dialog', { name: 'Confirm bulk deletion' })

  await expect(dialog.getByText(/1 changed since you selected them/)).toBeVisible()
  await expect(dialog.getByText(/Delete drifting/)).toBeVisible()

  // Confirming sends the versions the dialog displayed, so the batch applies.
  await dialog.getByRole('button', { name: 'Delete' }).click()
  await expect(todoCard(page, 'Delete stable')).toHaveCount(0)
  await expect(todoCard(page, 'Delete drifting')).toHaveCount(0)
})

test('a stale selection is repaired and retried', async ({ page }) => {
  const first = await createTodo(page, 'Repair one')
  const second = await createTodo(page, 'Repair two')
  const secondId = await cardId(second)

  await selectCard(first)
  await selectCard(second)

  // Archiving behind the page makes the held version stale and blocks the
  // completion the toolbar is about to attempt.
  await changeStatusOutOfBand(page, secondId, 1, todoStatus.archived)

  await bulkAction(page, 'Complete').click()

  // The silent retry re-reads versions and resends, so the batch reaches the
  // rule that actually forbids it rather than stopping at the stale version.
  await expect(page.getByRole('alert')).toContainText(
    /archived|out of date/i,
  )
  await expect(todoCard(page, 'Repair one').getByText('Not started')).toBeVisible()
})

test('a conflicted restore is not retried silently', async ({ page }) => {
  const card = await createTodo(page, 'Restore conflict')
  const id = await cardId(card)

  await selectCard(card)
  await bulkAction(page, 'Delete').click()
  await page.getByRole('dialog', { name: 'Confirm bulk deletion' })
    .getByRole('button', { name: 'Delete' }).click()
  await expect(todoCard(page, 'Restore conflict')).toHaveCount(0)

  await page.getByRole('tab', { name: 'Trash' }).click()
  await selectCard(todoCard(page, 'Restore conflict'))

  // Restoring behind the page inverts the intent the trash is holding: the
  // TODO is already back, and the version the list carries has moved on.
  await restoreOutOfBand(page, id, await currentVersion(page, id))

  // The batch route is counted rather than the outcome, because a retry would
  // fail the same way it did the first time and report the same banner.
  let attempts = 0
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().endsWith('/api/todos/restore')) {
      attempts += 1
    }
  })

  await bulkAction(page, 'Restore').click()

  await expect(page.getByRole('alert')).toContainText(/out of date/i)
  expect(attempts).toBe(1)
})

test('a filter that hides a selected TODO drops it from the count', async ({ page }) => {
  await createTodo(page, 'Filter staying')
  const leaving = await createTodo(page, 'Filter leaving')

  await leaving.getByRole('button', { name: 'Manage' }).click()
  await leaving.getByLabel('Status for Filter leaving').selectOption({ label: 'In progress' })
  await expect(todoCard(page, 'Filter leaving').getByText('In progress')).toBeVisible()

  await selectCard(todoCard(page, 'Filter staying'))
  await selectCard(todoCard(page, 'Filter leaving'))
  await expect(page.getByTestId('bulk-selected-count')).toHaveText('2 selected')

  // The count reads from the selection while an action reads from what is on
  // screen, so a filter that drops a selected TODO has to drop it from both.
  await page.getByLabel('Status filter').selectOption({ label: 'Not started' })
  await expect(todoCard(page, 'Filter leaving')).toHaveCount(0)
  await expect(page.getByTestId('bulk-selected-count')).toHaveText('1 selected')
})

/**
 * The gap the delete path leaves open. The existing drift test moves a version
 * before the dialog opens, so the dialog hydrates and displays the moved
 * version and the batch succeeds. This moves it *after* hydration, which is the
 * only way to make a deletion actually lose the version race — and the only
 * thing standing between that and a silent retry is the rule that confines
 * retries to status changes.
 */
test('a deletion that loses the race after the dialog opened is not retried', async ({ page }) => {
  const stable = await createTodo(page, 'Late drift stable')
  const drifting = await createTodo(page, 'Late drift moving')
  const driftingId = await cardId(drifting)

  await selectCard(stable)
  await selectCard(drifting)
  await bulkAction(page, 'Delete').click()

  // Wait for the dialog to finish reading, so the versions it is holding are
  // the ones about to go stale rather than ones it never had.
  const dialog = page.getByRole('dialog', { name: 'Confirm bulk deletion' })
  await expect(dialog.getByText(/unchanged since you selected them/)).toBeVisible()

  await changeStatusOutOfBand(page, driftingId, 1, todoStatus.inProgress)

  let attempts = 0
  page.on('request', (request) => {
    if (request.method() === 'DELETE' && request.url().endsWith('/api/todos')) {
      attempts += 1
    }
  })

  await dialog.getByRole('button', { name: 'Delete' }).click()

  await expect(page.getByRole('alert')).toContainText(/out of date/i)
  expect(attempts).toBe(1)

  // All or nothing: the batch was abandoned, so neither TODO was deleted.
  await expect(todoCard(page, 'Late drift stable')).toHaveCount(1)
  await expect(todoCard(page, 'Late drift moving')).toHaveCount(1)
})
