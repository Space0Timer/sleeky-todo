import { expect, test, type Page } from '@playwright/test'

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
import { createTodo, currentSpaceId, currentUserId, todoCard } from './todos.ts'

const sharedSpaceName = 'Project Alpha'
const unavailableNotice = 'That space is no longer available.'

/** The create form is the plainest thing a Read member does not get. */
function createForm(page: Page) {
  return page.getByRole('group', { name: 'Create a TODO' })
}

/**
 * The requirement itself, driven by two people in two browsers.
 *
 * Bob signs in before anything else, and not for tidiness: the user directory
 * holds only people who have completed a sign-in, so until he has, Alice's
 * search cannot find him and the grant she would make would be refused. That
 * is the one piece of the flow a demo has to know about.
 *
 * There is no realtime channel. Each time one of them should see the other's
 * work, the page is reloaded — that is the product's behaviour, not a
 * concession the test is making.
 */
test('two people share one list, at the level the owner sets', async ({ browser }) => {
  const aliceContext = await browser.newContext()
  const bobContext = await browser.newContext()
  const alice = await aliceContext.newPage()
  const bob = await bobContext.newPage()

  try {
    await signIn(bob, 'bob')
    await resetUserData(bob)
    const bobId = await currentUserId(bob)

    await signIn(alice, 'alice')
    await resetUserData(alice)
    const alphaId = await createSpaceThroughUi(alice, sharedSpaceName)
    await createTodo(alice, 'Draft the brief')

    // Alice shares it.
    await openSpaceSettings(alice)
    await addMemberThroughUi(alice, {
      term: testUsers.bob.username,
      subjectId: bobId,
      permission: 'Write',
    })
    await expect(memberRow(alice, bobId)).toContainText(testUsers.bob.displayName)
    await closeSpaceSettings(alice)

    // Bob finds it in his own selector and works in it.
    await bob.goto('/')
    await expectSignedIn(bob)
    await switchToSpace(bob, sharedSpaceName)
    expect(await currentSpaceId(bob)).toBe(alphaId)
    await expect(todoCard(bob, 'Draft the brief')).toHaveCount(1)
    await createTodo(bob, 'Book the room')

    // Alice sees what Bob added, once she asks for the page again.
    await alice.reload()
    await expectSignedIn(alice)
    await expect(todoCard(alice, 'Book the room')).toHaveCount(1)

    // Alice narrows him to Read.
    await openSpaceSettings(alice)
    await alice
      .getByTestId(`member-permission-${bobId}`)
      .selectOption({ label: 'Read' })
    await expect(alice.getByTestId(`member-permission-${bobId}`)).toHaveValue('1')
    await closeSpaceSettings(alice)

    await bob.reload()
    await expectSignedIn(bob)
    await expectActiveSpace(bob, sharedSpaceName)
    await expect(bob.getByTestId('space-permission')).toContainText('Read-only')
    await expect(createForm(bob)).toHaveCount(0)
    await expect(todoCard(bob, 'Draft the brief')).toHaveCount(1)

    // A Read member still sees who is in the Space, and none of the controls.
    await openSpaceSettings(bob)
    await expect(memberRow(bob, bobId)).toContainText('you')
    await expect(bob.getByTestId('member-search')).toHaveCount(0)
    await expect(bob.getByTestId(`member-remove-${bobId}`)).toHaveCount(0)
    await closeSpaceSettings(bob)

    // Alice takes it back.
    await openSpaceSettings(alice)
    await alice.getByTestId(`member-remove-${bobId}`).click()
    await expect(memberRow(alice, bobId)).toHaveCount(0)
    await closeSpaceSettings(alice)

    // Bob's next visit has nowhere to land, and is told so rather than shown
    // an empty list.
    await bob.goto(`/spaces/${alphaId}`)
    await expectSignedIn(bob)
    await expect(bob.getByText(unavailableNotice)).toBeVisible()
    await expectActiveSpace(bob, 'My Space')
    await expect(todoCard(bob, 'Draft the brief')).toHaveCount(0)
  } finally {
    await aliceContext.close()
    await bobContext.close()
  }
})

/**
 * The rename is in the same dialog as the sharing, and its effect belongs to
 * the selector rather than to the dialog: the list the provider holds is
 * refreshed by the same call that saves the name.
 */
test('an owner renames the space and the selector says so', async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
  await createSpaceThroughUi(page, 'Project Delta')

  await openSpaceSettings(page)
  await page.getByTestId('space-rename-input').fill('Project Epsilon')
  await page.getByTestId('space-rename-submit').click()
  await closeSpaceSettings(page)

  await expectActiveSpace(page, 'Project Epsilon')
})

test('the rename field refuses an empty name', async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
  await createSpaceThroughUi(page, 'Project Zeta')

  await openSpaceSettings(page)
  await page.getByTestId('space-rename-input').fill('   ')
  await page.getByTestId('space-rename-submit').click()

  await expect(page.getByTestId('space-settings-dialog'))
    .toContainText('A space name is required.')
  await closeSpaceSettings(page)
  await expectActiveSpace(page, 'Project Zeta')
})

/**
 * The floor the server puts under a search, met by the client rather than
 * argued with: one character asks for nothing, so nothing is asked for.
 */
test('the member search waits for something worth searching for', async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
  await createSpaceThroughUi(page, 'Project Eta')

  const searches: string[] = []
  page.on('request', (request) => {
    const { pathname, search } = new URL(request.url())
    if (pathname === '/api/users/search') searches.push(search)
  })

  await openSpaceSettings(page)

  // A term nobody matches, so the assertion is about the request rather than
  // about who happens to have signed in during this run.
  await page.getByTestId('member-search').fill('z')
  await page.waitForTimeout(600)
  expect(searches).toEqual([])

  await page.getByTestId('member-search').fill('zz')
  await expect(page.getByTestId('member-results')).toContainText('Nobody matches')

  expect(searches).toEqual(['?q=zz'])
})
