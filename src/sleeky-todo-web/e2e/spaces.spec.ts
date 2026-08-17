import { expect, test } from '@playwright/test'

import { expectSignedIn, signIn, signOut, testUsers } from './auth.ts'
import { resetUserData } from './database.ts'
import {
  createSpaceThroughUi,
  expectActiveSpace,
  spaceSelector,
  switchToSpace,
} from './spaces.ts'
import { createTodo, currentSpaceId, todoCard } from './todos.ts'

const personalSpaceName = 'My Space'

test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
})

test('signing in lands in the personal space the server ensures', async ({ page }) => {
  await expectActiveSpace(page, personalSpaceName)
})

/**
 * The whole point of the Space boundary, driven the way a user meets it: a
 * second list exists, the URL says which one is open, and what is in one is
 * not in the other.
 */
test('creates a space, switches between the two, and keeps them separate', async ({
  page,
}) => {
  const personalSpaceId = await currentSpaceId(page)

  const alphaId = await createSpaceThroughUi(page, 'Project Alpha')
  expect(alphaId).not.toBe(personalSpaceId)
  await expect(page).toHaveURL(new RegExp(`/spaces/${alphaId}$`, 'i'))

  await createTodo(page, 'Alpha only TODO')

  await switchToSpace(page, personalSpaceName)
  await expect(page).toHaveURL(new RegExp(`/spaces/${personalSpaceId}$`, 'i'))
  await expect(todoCard(page, 'Alpha only TODO')).toHaveCount(0)

  await switchToSpace(page, 'Project Alpha')
  await expect(todoCard(page, 'Alpha only TODO')).toHaveCount(1)

  // The URL is the record of the open Space, so a reload returns to it rather
  // than to whichever Space a fresh visit would resolve.
  await page.reload()
  await expectSignedIn(page)
  await expectActiveSpace(page, 'Project Alpha')
  await expect(todoCard(page, 'Alpha only TODO')).toHaveCount(1)
})

/**
 * `/` carries no Space of its own, so it has to pick one, and the one the user
 * was last in is the only answer that does not lose their place.
 */
test('the root returns to the space last visited', async ({ page }) => {
  await createSpaceThroughUi(page, 'Project Beta')

  await page.goto('/')

  await expectSignedIn(page)
  await expectActiveSpace(page, 'Project Beta')
})

test('a space that cannot be reached falls back to one that can', async ({ page }) => {
  await page.goto('/spaces/00000000-0000-4000-8000-000000000fff')

  await expectSignedIn(page)
  await expectActiveSpace(page, personalSpaceName)
  await expect(page.getByText('That space is no longer available.')).toBeVisible()
})

/**
 * A shared link is most often opened by someone who is not signed in. The
 * link has to survive the trip through the login page and the provider, or
 * it lands on whatever Space was last remembered instead of the one it named.
 */
test('a space link opened while signed out lands on that space after signing in', async ({
  page,
}) => {
  const betaId = await createSpaceThroughUi(page, 'Project Beta')
  await switchToSpace(page, personalSpaceName)
  await signOut(page)

  await page.goto(`/spaces/${betaId}`)
  await expect(page).toHaveURL(/\/login$/)

  // Not the shared sign-in helper: that one starts from `/login` itself, and
  // the point here is arriving there from the link.
  await page.getByRole('button', { name: 'Sign in' }).click()
  const usernameField = page.locator('#username')
  const asksForCredentials = await usernameField
    .waitFor({ state: 'visible', timeout: 10_000 })
    .then(() => true)
    .catch(() => false)
  if (asksForCredentials) {
    await usernameField.fill(testUsers.alice.username)
    await page.locator('#password').fill(testUsers.alice.password)
    await page.locator('#kc-login').click()
  }

  await expectSignedIn(page)
  await expect(page).toHaveURL(new RegExp(`/spaces/${betaId}$`, 'i'))
  await expectActiveSpace(page, 'Project Beta')
})

test('the create dialog refuses an empty name without asking the server', async ({
  page,
}) => {
  await page.getByTestId('create-space').click()
  await page.getByTestId('create-space-submit').click()

  await expect(page.getByTestId('create-space-dialog')).toBeVisible()
  await expect(page.getByTestId('create-space-dialog'))
    .toContainText('A space name is required.')

  await page.getByRole('button', { name: 'Cancel' }).click()
  await expect(page.getByTestId('create-space-dialog')).toHaveCount(0)
  await expectActiveSpace(page, personalSpaceName)
})

test('every TODO request is nested under the space on screen', async ({ page }) => {
  const spaceId = await currentSpaceId(page)
  const paths: string[] = []

  page.on('request', (request) => {
    const { pathname } = new URL(request.url())
    if (pathname.includes('/todos')) paths.push(pathname)
  })

  await createTodo(page, 'Nested route TODO')

  expect(paths.length).toBeGreaterThan(0)
  for (const path of paths) {
    expect(path.startsWith(`/api/spaces/${spaceId}/todos`)).toBeTruthy()
  }
})

test('the selector lists every space the user is a member of', async ({ page }) => {
  await createSpaceThroughUi(page, 'Project Gamma')

  await expect(spaceSelector(page).locator('option')).toHaveText([
    personalSpaceName,
    'Project Gamma',
  ])
})
