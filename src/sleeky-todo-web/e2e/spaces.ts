import { expect, type Locator, type Page } from '@playwright/test'

import { spaceUrlPattern } from './auth.ts'
import { currentSpaceId } from './todos.ts'

export function spaceSelector(page: Page): Locator {
  return page.getByTestId('space-selector')
}

/** Asserts which Space the page is on, by the name the selector shows. */
export async function expectActiveSpace(page: Page, name: string): Promise<void> {
  await expect(page).toHaveURL(spaceUrlPattern)
  await expect(spaceSelector(page).locator('option:checked')).toHaveText(name)
}

/**
 * Switches Spaces the way a user does. The identifier is read first and waited
 * out afterwards, because the switch is a navigation: everything on the page
 * is remounted against the Space the URL now names.
 */
export async function switchToSpace(page: Page, name: string): Promise<void> {
  const before = await currentSpaceId(page)

  await spaceSelector(page).selectOption({ label: name })

  await expect(page).not.toHaveURL(new RegExp(`/spaces/${before}$`, 'i'))
  await expectActiveSpace(page, name)
  await expect(page.getByText('Loading TODOs…')).toHaveCount(0)
}

/**
 * Creates a Space through the dialog and lands on it. Returns its identifier,
 * which is what the URL and every nested route below it carry.
 */
export async function createSpaceThroughUi(page: Page, name: string): Promise<string> {
  await page.getByTestId('create-space').click()

  const dialog = page.getByTestId('create-space-dialog')
  await expect(dialog).toBeVisible()
  await page.getByTestId('create-space-name').fill(name)
  await page.getByTestId('create-space-submit').click()

  await expect(dialog).toHaveCount(0)
  await expectActiveSpace(page, name)

  return currentSpaceId(page)
}
