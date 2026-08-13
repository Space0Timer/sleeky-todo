import { expect, test, type Page } from '@playwright/test'

import { expectSignedIn, signIn, signOut, testUsers } from './auth.ts'

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

  await expect(page).toHaveURL(/127\.0\.0\.1:5173\/$/)
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

test('a mutation carries an antiforgery header', async ({ page }) => {
  await signIn(page, 'alice')

  const createRequest = page.waitForRequest(
    request =>
      request.method() === 'POST' && request.url().endsWith('/api/todos'),
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

test('two users see separate TODO lists', async ({ browser }) => {
  const aliceContext = await browser.newContext()
  const bobContext = await browser.newContext()

  try {
    const alicePage = await aliceContext.newPage()
    const bobPage = await bobContext.newPage()

    await signIn(alicePage, 'alice')
    await createTodo(alicePage, 'Alice private TODO')

    await signIn(bobPage, 'bob')
    await expect(bobPage.getByTestId('current-user')).toHaveText(
      testUsers.bob.displayName,
    )

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
  } finally {
    await aliceContext.close()
    await bobContext.close()
  }
})
