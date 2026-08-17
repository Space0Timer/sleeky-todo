import { expect, test } from '@playwright/test'

import { ask, configureAssistant, scriptModel } from './assistant-model.ts'
import { signIn } from './auth.ts'
import { resetUserData } from './database.ts'
import { createSpaceThroughUi, switchToSpace } from './spaces.ts'
import { cardId, createTodo, todoCard } from './todos.ts'

/**
 * A turn is several round trips through the loop, each dispatching for real, so
 * it outlasts the default assertion timeout. This is applied to the assertion
 * that waits for a turn to end, never to the ones checking what it did.
 */
const turnTimeout = 20_000

/**
 * The assistant's actual behaviour, driven through the browser against a stub
 * provider. `assistant.spec.ts` covers the paths that need no model at all;
 * these cover the ones that do — a write refreshing the list, a deletion asking
 * first, and what each button on that dialog does.
 */
test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
  await configureAssistant(page)
})

test('a turn that writes refreshes the list and reports what it did', async ({ page }) => {
  const card = await createTodo(page, 'Assistant completes me')
  const id = await cardId(card)

  await scriptModel(page, [
    { tool: 'get_todos', arguments: { limit: 50 } },
    { tool: 'change_todo_status', arguments: { status: 'Completed', ids: [id] } },
    { text: 'Marked 1 completed.' },
  ])

  await ask(page, 'Complete that one.')

  // The reply lands only once the turn is over, so waiting for it first keeps
  // the assertions below from racing a turn that is still running.
  await expect(page.getByTestId('assistant-assistant'))
    .toContainText('Marked 1 completed.', { timeout: turnTimeout })
  await expect(page.getByTestId('assistant-tool')).toBeVisible()

  // The list is the source of truth, so the proof is the card itself moving.
  await expect(
    todoCard(page, 'Assistant completes me').getByText('Completed'),
  ).toBeVisible()
})

test('a deletion asks before it deletes', async ({ page }) => {
  const card = await createTodo(page, 'Assistant asks first')
  const id = await cardId(card)

  await scriptModel(page, [
    { tool: 'delete_todos', arguments: { ids: [id] } },
  ])

  await ask(page, 'Delete that one.')

  const dialog = page.getByTestId('assistant-confirmation')
  await expect(dialog).toBeVisible({ timeout: turnTimeout })
  await expect(dialog).toContainText('Assistant asks first')

  // Proposed, not done: the TODO is still on the list behind the dialog.
  await expect(todoCard(page, 'Assistant asks first')).toHaveCount(1)
})

test('confirming a deletion applies it and the list catches up', async ({ page }) => {
  const card = await createTodo(page, 'Assistant deletes me')
  const id = await cardId(card)

  await scriptModel(page, [{ tool: 'delete_todos', arguments: { ids: [id] } }])
  await ask(page, 'Delete that one.')
  await expect(page.getByTestId('assistant-confirmation'))
    .toBeVisible({ timeout: turnTimeout })

  // The confirming turn does not consult the model about what to delete, so the
  // only reply it needs is the summary.
  await scriptModel(page, [{ text: 'Deleted it.' }])
  await page.getByTestId('assistant-confirmation')
    .getByRole('button', { name: 'Confirm' }).click()

  await expect(page.getByTestId('assistant-confirmation')).toHaveCount(0)
  await expect(todoCard(page, 'Assistant deletes me'))
    .toHaveCount(0, { timeout: turnTimeout })

  await page.getByRole('tab', { name: 'Trash' }).click()
  await expect(todoCard(page, 'Assistant deletes me')).toHaveCount(1)
})

test('cancelling a deletion changes nothing', async ({ page }) => {
  const card = await createTodo(page, 'Assistant spares me')
  const id = await cardId(card)

  await scriptModel(page, [{ tool: 'delete_todos', arguments: { ids: [id] } }])
  await ask(page, 'Delete that one.')

  const dialog = page.getByTestId('assistant-confirmation')
  await expect(dialog).toBeVisible({ timeout: turnTimeout })
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  await expect(dialog).toHaveCount(0)
  await expect(todoCard(page, 'Assistant spares me')).toHaveCount(1)

  await page.getByRole('tab', { name: 'Trash' }).click()
  await expect(todoCard(page, 'Assistant spares me')).toHaveCount(0)
})

/**
 * The server keeps no history, so a second turn can only write to something the
 * first turn read if the transcript survived the round trip through the client.
 */
test('a second turn writes to what the first turn read', async ({ page }) => {
  const card = await createTodo(page, 'Assistant remembers me')
  const id = await cardId(card)

  await scriptModel(page, [
    { tool: 'get_todos', arguments: { limit: 50 } },
    { text: 'You have one TODO.' },
  ])
  await ask(page, 'What is on my list?')
  await expect(page.getByTestId('assistant-assistant'))
    .toContainText('You have one TODO.', { timeout: turnTimeout })

  // No read this time. It can only succeed on versions carried in the
  // transcript the browser echoed back.
  await scriptModel(page, [
    { tool: 'change_todo_status', arguments: { status: 'Completed', ids: [id] } },
    { text: 'Done.' },
  ])
  await ask(page, 'Complete it.')

  await expect(
    todoCard(page, 'Assistant remembers me').getByText('Completed'),
  ).toBeVisible({ timeout: turnTimeout })
})

/**
 * A confirmation carries the versions the proposal displayed, and it belongs
 * to the Space the proposal was made in. Switching Spaces takes the panel with
 * it, which is what discards the pending question rather than leaving a
 * Confirm button that would act on another list's TODOs.
 */
test('switching spaces discards a pending confirmation', async ({ page }) => {
  const card = await createTodo(page, 'Assistant pending delete')
  const id = await cardId(card)

  await scriptModel(page, [{ tool: 'delete_todos', arguments: { ids: [id] } }])
  await ask(page, 'Delete that one.')
  await expect(page.getByTestId('assistant-confirmation'))
    .toBeVisible({ timeout: turnTimeout })

  await createSpaceThroughUi(page, 'Confirmation Alpha')

  await expect(page.getByTestId('assistant-confirmation')).toHaveCount(0)
  await expect(page.getByTestId('assistant-user')).toHaveCount(0)

  // Nothing was deleted: the proposal was never confirmed.
  await switchToSpace(page, 'My Space')
  await expect(todoCard(page, 'Assistant pending delete')).toHaveCount(1)
})

/**
 * The assistant's tools act only inside the Space the turn named, so a write
 * asked for in one Space lands in that one and nowhere else.
 */
test('a turn writes only in the space it was asked in', async ({ page }) => {
  const personalCard = await createTodo(page, 'Assistant leaves me alone')
  const personalId = await cardId(personalCard)

  await createSpaceThroughUi(page, 'Isolated Alpha')
  const alphaCard = await createTodo(page, 'Assistant works here')
  const alphaId = await cardId(alphaCard)

  await scriptModel(page, [
    { tool: 'change_todo_status', arguments: { status: 'Completed', ids: [alphaId] } },
    { text: 'Marked 1 completed.' },
  ])
  await ask(page, 'Complete that one.')

  await expect(page.getByTestId('assistant-assistant'))
    .toContainText('Marked 1 completed.', { timeout: turnTimeout })
  await expect(
    todoCard(page, 'Assistant works here').getByText('Completed'),
  ).toBeVisible()

  await switchToSpace(page, 'My Space')
  await expect(
    todoCard(page, 'Assistant leaves me alone').getByText('Open', { exact: true }),
  ).toBeVisible()
  expect(personalId).not.toBe(alphaId)
})
