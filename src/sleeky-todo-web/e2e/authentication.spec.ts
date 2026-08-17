import { expect, test, type Page } from '@playwright/test'

import { expectSignedIn, signIn, signOut, spaceUrlPattern, testUsers } from './auth.ts'
import { resetUserData } from './database.ts'
import { expectActiveSpace } from './spaces.ts'
import { currentSpaceId } from './todos.ts'

async function createTodo(page: Page, name: string): Promise<void> {
  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill(name)
  await form.getByLabel('Description').fill(`Details for ${name}`)
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByLabel('Priority').selectOption({ label: 'High' })
  await form.getByRole('button', { name: 'Add TODO' }).click()

  await expect(
    page.getByRole('heading', { name, exact: true }),
  ).toBeVisible()
}

test('an unauthenticated visitor is sent to the login page', async ({ page }) => {
  await page.goto('/')

  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

test('signing in lands on the TODO list and survives a reload', async ({ page }) => {
  await signIn(page, 'alice')

  // The list always belongs to a Space, so the landing URL names one: `/`
  // only resolves which and redirects.
  await expect(page).toHaveURL(spaceUrlPattern)
  await expect(page.getByTestId('current-user')).toHaveText(
    testUsers.alice.displayName,
  )

  await page.reload()

  await expectSignedIn(page)
  await expect(page.getByTestId('current-user')).toHaveText(
    testUsers.alice.displayName,
  )
})

test('signing out returns to the login page and protects the list', async ({
  page,
}) => {
  await signIn(page, 'alice')
  await signOut(page)

  await page.goto('/')

  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

test('signing out ends the provider session too', async ({ page }) => {
  await signIn(page, 'alice')
  await signOut(page)

  // The whole point of redirecting through the provider's end-session
  // endpoint. Without it the single sign-on session outlives the application
  // session and this second sign-in is carried through with no prompt at all,
  // which on a shared device hands the next person the account.
  const askedForCredentials = await signIn(page, 'alice')

  expect(askedForCredentials).toBe(true)
})

test('a mutation carries an antiforgery header', async ({ page }) => {
  await signIn(page, 'alice')
  await resetUserData(page)

  const createRequest = page.waitForRequest(
    request =>
      request.method() === 'POST' && request.url().endsWith('/todos'),
  )

  await createTodo(page, 'Antiforgery protected TODO')

  const headers = (await createRequest).headers()
  expect(headers['x-csrf-token']).toBeTruthy()
})

test('a mutation still succeeds after signing out and back in', async ({
  page,
}) => {
  await signIn(page, 'alice')
  await signOut(page)
  await signIn(page, 'alice')
  await resetUserData(page)

  // Antiforgery tokens are bound to the authenticated identity, so a token
  // kept from before the sign-out would be rejected. Creating a TODO only
  // succeeds if the client requested a new one for the new session.
  await createTodo(page, 'Created after re-authentication')
})

test('an expired session sends the next mutation to the login page', async ({
  context,
  page,
}) => {
  await signIn(page, 'alice')

  await context.clearCookies({ name: 'sleeky-session' })

  const form = page.getByRole('group', { name: 'Create a TODO' })
  await form.getByLabel('Name').fill('Expired session TODO')
  await form.getByLabel('Due date').fill('2026-08-31')
  await form.getByLabel('Priority').selectOption({ label: 'High' })
  await form.getByRole('button', { name: 'Add TODO' }).click()

  await expect(page).toHaveURL(/\/login$/)
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
})

/**
 * Two users, two personal Spaces, and no overlap between them. The Space is
 * what the isolation now rests on: each user lands in the one the server
 * ensures for them, and neither Space's TODOs appear in the other.
 */
test('two users have separate personal spaces', async ({ browser }) => {
  const aliceContext = await browser.newContext()
  const bobContext = await browser.newContext()

  try {
    const alicePage = await aliceContext.newPage()
    const bobPage = await bobContext.newPage()

    await signIn(alicePage, 'alice')
    await resetUserData(alicePage)
    await expectActiveSpace(alicePage, 'My Space')
    const aliceSpaceId = await currentSpaceId(alicePage)
    await createTodo(alicePage, 'Alice private TODO')

    await signIn(bobPage, 'bob')
    await resetUserData(bobPage)
    await expect(bobPage.getByTestId('current-user')).toHaveText(
      testUsers.bob.displayName,
    )

    await expectActiveSpace(bobPage, 'My Space')
    // Same name, different Space: the personal Space's identifier is derived
    // from the user, so Bob's is his own however alike they read.
    expect(await currentSpaceId(bobPage)).not.toBe(aliceSpaceId)

    await expect(
      bobPage.getByRole('heading', { name: 'Alice private TODO', exact: true }),
    ).toHaveCount(0)

    await createTodo(bobPage, 'Bob private TODO')

    await alicePage.reload()
    await expectSignedIn(alicePage)
    await expect(
      alicePage.getByRole('heading', { name: 'Bob private TODO', exact: true }),
    ).toHaveCount(0)
    await expect(
      alicePage.getByRole('heading', {
        name: 'Alice private TODO',
        exact: true,
      }),
    ).toBeVisible()

    // Alice's Space is not Bob's to visit, and the server does not confirm it
    // exists: he is returned to a Space he does have.
    await bobPage.goto(`/spaces/${aliceSpaceId}`)
    await expectSignedIn(bobPage)
    await expectActiveSpace(bobPage, 'My Space')
    await expect(
      bobPage.getByRole('heading', { name: 'Alice private TODO', exact: true }),
    ).toHaveCount(0)
  } finally {
    await aliceContext.close()
    await bobContext.close()
  }
})
