import { expect, test } from '@playwright/test'

import { ask, configureAssistant, scriptModel } from './assistant-model.ts'
import { expectSignedIn, signIn, testUsers } from './auth.ts'
import { resetUserData } from './database.ts'
import {
  addMemberThroughUi,
  closeSpaceSettings,
  createSpaceThroughUi,
  expectActiveSpace,
  memberRow,
  openSpaceSettings,
  switchToSpace,
} from './spaces.ts'
import {
  antiforgeryHeader,
  apiOrigin,
  cardId,
  createTodo,
  currentSpaceId,
  currentUserId,
  todoCard,
} from './todos.ts'

/**
 * Regenerates the images the README shows, by driving the application the way
 * the browser suite does rather than assembling a mock-up.
 *
 * Excluded from the ordinary run through `testIgnore`, because a capture is
 * not a test: it asserts only enough to be sure it is photographing the state
 * it claims to. Run it with `corepack yarn screenshots`.
 */
const sharedSpaceName = 'Project Alpha'
const shot = (name: string) => `../../docs/screenshots/${name}.png`

test('the sharing walkthrough', async ({ browser }) => {
  // Two sign-ins through the real provider, a grant, a downgrade, and two
  // navigations: more round trips than any single test makes.
  test.setTimeout(240_000)

  const aliceContext = await browser.newContext({
    viewport: { width: 1280, height: 900 },
  })
  const bobContext = await browser.newContext({
    viewport: { width: 1280, height: 900 },
  })
  const alice = await aliceContext.newPage()
  const bob = await bobContext.newPage()

  try {
    await signIn(bob, 'bob')
    await resetUserData(bob)
    const bobId = await currentUserId(bob)

    await signIn(alice, 'alice')
    await resetUserData(alice)
    await createSpaceThroughUi(alice, sharedSpaceName)
    await createTodo(alice, 'Draft the brief')
    await createTodo(alice, 'Send the invitations')
    await alice.screenshot({ path: shot('01-a-space-of-her-own') })

    await openSpaceSettings(alice)
    await addMemberThroughUi(alice, {
      term: testUsers.bob.username,
      subjectId: bobId,
      permission: 'Write',
    })
    await expect(memberRow(alice, bobId)).toContainText(testUsers.bob.displayName)
    await alice.screenshot({ path: shot('02-sharing-it-with-bob') })
    await closeSpaceSettings(alice)

    // Bob reaches it from his own selector, not from a link.
    await bob.goto('/')
    await expectSignedIn(bob)
    await switchToSpace(bob, sharedSpaceName)
    await expect(todoCard(bob, 'Draft the brief')).toHaveCount(1)
    await createTodo(bob, 'Book the room')
    await bob.screenshot({ path: shot('03-bob-works-in-it') })

    await openSpaceSettings(alice)
    await alice.getByTestId(`member-permission-${bobId}`).selectOption({ label: 'Read' })
    await expect(alice.getByTestId(`member-permission-${bobId}`)).toHaveValue('1')
    await closeSpaceSettings(alice)

    // He is already on it, so the page is asked for again rather than switched
    // to: nothing is pushed to him, and the narrowing shows when he looks.
    await bob.reload()
    await expectSignedIn(bob)
    await expectActiveSpace(bob, sharedSpaceName)
    await expect(bob.getByTestId('space-permission')).toContainText('Read-only')
    await bob.screenshot({ path: shot('04-read-only-for-bob') })
  } finally {
    await aliceContext.close()
    await bobContext.close()
  }
})

/**
 * The version check, photographed at the moment it refuses: the edit was made
 * against a TODO someone else had already moved on.
 */
test('a stale save', async ({ page }) => {
  test.setTimeout(120_000)

  await signIn(page)
  await resetUserData(page)

  const card = await createTodo(page, 'Refresh the pricing page')
  const id = await cardId(card)

  // Editing lives behind Manage, so the card is opened before the other writer
  // moves underneath it.
  await card.getByRole('button', { name: 'Manage' }).click()
  await expect(card.getByRole('region', { name: 'Manage Refresh the pricing page' }))
    .toBeVisible()

  const spaceId = await currentSpaceId(page)

  // The other writer, acting behind the page's back at the version it holds.
  const response = await page.request.put(`${apiOrigin}/api/spaces/${spaceId}/todos/${id}`, {
    headers: await antiforgeryHeader(page),
    data: {
      name: 'Refresh the pricing page',
      description: 'Someone else got here first',
      dueDate: '2026-09-01',
      priority: 1,
      version: 1,
    },
  })
  expect(response.ok()).toBeTruthy()

  await card.getByRole('button', { name: 'Edit details' }).click()
  const editForm = page.getByRole('group', { name: 'Edit Refresh the pricing page' })
  await editForm.getByLabel('Name').fill('Refresh the pricing page and the FAQ')
  await editForm.getByRole('button', { name: 'Save changes' }).click()

  await expect(page.getByRole('alert'))
    .toContainText('This TODO was changed by another user.')
  await page.screenshot({ path: shot('05-a-stale-save') })
})

/** A prerequisite that is not finished, and a dependent that cannot start. */
test('a blocked dependency', async ({ page }) => {
  test.setTimeout(120_000)

  await signIn(page)
  await resetUserData(page)

  await createTodo(page, 'Sign off the budget')
  let dependent = await createTodo(page, 'Order the equipment')

  await dependent.getByRole('button', { name: 'Manage' }).click()
  await dependent.getByLabel('Dependency for Order the equipment')
    .selectOption({ label: 'Sign off the budget' })
  await dependent.getByRole('button', { name: 'Add', exact: true }).click()

  dependent = todoCard(page, 'Order the equipment')
  await expect(dependent.getByText('Blocked', { exact: true })).toBeVisible()
  await dependent.getByRole('button', { name: 'Manage' }).click()
  await expect(
    dependent.getByLabel('Status for Order the equipment').locator('option[value="1"]'),
  ).toHaveAttribute('disabled', '')
  await page.screenshot({ path: shot('06-a-blocked-dependency') })
})

/** The assistant proposing a deletion rather than performing one. */
test('the assistant asking first', async ({ page }) => {
  test.setTimeout(180_000)

  await signIn(page)
  await resetUserData(page)
  await configureAssistant(page)

  const card = await createTodo(page, 'Cancel the old subscription')
  const id = await cardId(card)

  await scriptModel(page, [{ tool: 'delete_todos', arguments: { ids: [id] } }])
  await ask(page, 'Delete the subscription one.')

  const dialog = page.getByTestId('assistant-confirmation')
  await expect(dialog).toBeVisible({ timeout: 20_000 })
  await expect(dialog).toContainText('Cancel the old subscription')
  await page.screenshot({ path: shot('07-the-assistant-asks-first') })
})
