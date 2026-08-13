import { expect, type Page } from '@playwright/test'

export const testUsers = {
  alice: {
    displayName: 'Alice Anderson',
    password: 'alice-password',
    username: 'alice',
  },
  bob: {
    displayName: 'Bob Baxter',
    password: 'bob-password',
    username: 'bob',
  },
} as const

export type TestUserName = keyof typeof testUsers

const appHeading = 'Keep today clear.'

/**
 * Drives the real provider login form. Logout is application-only, so the
 * provider session can outlive the application session and the credential form
 * is skipped on a second sign-in within the same browser context.
 */
export async function signIn(
  page: Page,
  user: TestUserName = 'alice',
): Promise<void> {
  const { password, username } = testUsers[user]

  await page.goto('/login')
  await page.getByRole('button', { name: 'Sign in' }).click()

  const usernameField = page.locator('#username')
  const hasCredentialForm = await usernameField
    .waitFor({ state: 'visible', timeout: 10_000 })
    .then(() => true)
    .catch(() => false)

  if (hasCredentialForm) {
    await usernameField.fill(username)
    await page.locator('#password').fill(password)
    await page.locator('#kc-login').click()
  }

  await expectSignedIn(page)
}

export async function expectSignedIn(page: Page): Promise<void> {
  await expect(page.getByRole('heading', { name: appHeading })).toBeVisible()
  await expect(page.getByText('Loading TODOs…')).toHaveCount(0)
}

export async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/\/login$/)
}
