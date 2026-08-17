import { expect, type Locator, type Page } from '@playwright/test'

import { spaceUrlPattern } from './auth.ts'
import { currentSpaceId } from './todos.ts'

/**
 * Permissions travel over the wire as their numeric order, so a select bound
 * to one carries the number rather than the word the user picked.
 */
const permissionValues = {
  Read: '1',
  Write: '2',
} as const

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
 * Switches Spaces by going straight to the URL, the way a bookmark or the back
 * button does. Needed when something modal is on screen: a dialog covers the
 * selector, but it does not pin the browser to the page.
 */
export async function navigateToSpace(
  page: Page,
  spaceId: string,
  name: string,
): Promise<void> {
  await page.goto(`/spaces/${spaceId}`)

  await expectActiveSpace(page, name)
  await expect(page.getByText('Loading TODOs…')).toHaveCount(0)
}

/** The Space settings dialog, which any member can open from the selector. */
export async function openSpaceSettings(page: Page): Promise<Locator> {
  await page.getByTestId('manage-space').click()

  const dialog = page.getByTestId('space-settings-dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByTestId('member-list')).toBeVisible()

  return dialog
}

export async function closeSpaceSettings(page: Page): Promise<void> {
  await page.getByTestId('space-settings-close').click()
  await expect(page.getByTestId('space-settings-dialog')).toHaveCount(0)
}

/**
 * Shares the open Space the way an Owner does: type enough of a name for the
 * server to answer, pick the person, choose what they may do, and add them.
 *
 * The search is debounced and answers only from the user directory, so the
 * result being waited for here is also the assertion that the person is
 * findable at all.
 */
export async function addMemberThroughUi(
  page: Page,
  options: { term: string; subjectId: string; permission: 'Read' | 'Write' },
): Promise<void> {
  const dialog = page.getByTestId('space-settings-dialog')

  await dialog.getByTestId('member-search').fill(options.term)

  const result = dialog.getByTestId(`member-result-${options.subjectId}`)
  await expect(result).toBeVisible()
  await result.click()

  await dialog.getByTestId('add-member-permission').selectOption({ label: options.permission })
  await dialog.getByTestId('add-member-submit').click()

  await expect(memberRow(page, options.subjectId)).toBeVisible()
  await expect(dialog.getByTestId(`member-permission-${options.subjectId}`))
    .toHaveValue(permissionValues[options.permission])
}

export function memberRow(page: Page, subjectId: string): Locator {
  return page.getByTestId(`member-row-${subjectId}`)
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
