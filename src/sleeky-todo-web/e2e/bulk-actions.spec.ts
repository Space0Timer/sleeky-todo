import { expect, test } from '@playwright/test'

import { todoStatus } from '../src/types/todo.ts'
import { signIn } from './auth.ts'
import {
  bulkAction,
  cardId,
  changeStatusOutOfBand,
  createTodo,
  selectCard,
  todoCard,
} from './todos.ts'

test.beforeEach(async ({ page }) => {
  await signIn(page)
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
