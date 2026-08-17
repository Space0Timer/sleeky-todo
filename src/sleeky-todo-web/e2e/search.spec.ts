import { expect, test, type Page } from '@playwright/test'

import { signIn } from './auth.ts'
import { resetUserData } from './database.ts'
import { seedTodos } from './seed.ts'
import { createTodo, currentSpaceId, currentUserId, todoCard } from './todos.ts'

function activeCards(page: Page) {
  return page.getByRole('region', { name: 'Active' }).locator('[data-testid^="todo-"]')
}

test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetUserData(page)
})

test('narrows the list to matching TODOs and restores it when cleared', async ({ page }) => {
  await createTodo(page, 'Renew passport')
  await createTodo(page, 'Book a haircut')

  await expect(activeCards(page)).toHaveCount(2)

  await page.getByLabel('Search filter').fill('pass')
  await expect(activeCards(page)).toHaveCount(1)
  await expect(todoCard(page, 'Renew passport')).toBeVisible()

  await page.getByLabel('Search filter').fill('zzz')
  await expect(page.getByText('No TODOs match your search.')).toBeVisible()

  // Counted from here, because Clear is where an unguarded merge shows: it
  // resets the filters at once and the box's own reset drains through the
  // debounce afterwards, so a second identical request would follow ~300 ms
  // later and race the first. Invisible on screen, and the reason the merge
  // returns the same object when nothing changed.
  let listRequests = 0
  await page.route(
    (url) => url.pathname.endsWith('/todos'),
    async (route) => {
      listRequests += 1
      await route.continue()
    },
  )

  await page.getByRole('button', { name: 'Clear filters' }).click()

  // The box empties as well as the list: leaving the text on screen would show
  // an unfiltered list under a query that still reads as applied.
  await expect(page.getByLabel('Search filter')).toHaveValue('')
  await expect(activeCards(page)).toHaveCount(2)

  await page.waitForTimeout(800)
  expect(listRequests).toBe(1)
})

test('matches the start of a word rather than any part of one', async ({ page }) => {
  await createTodo(page, 'Renew passport')

  await page.getByLabel('Search filter').fill('renew')
  await expect(activeCards(page)).toHaveCount(1)

  await page.getByLabel('Search filter').fill('enew')
  await expect(page.getByText('No TODOs match your search.')).toBeVisible()
})

test('loads a second page under a search without duplicates', async ({ page }) => {
  const spaceId = await currentSpaceId(page)
  const createdByUserId = await currentUserId(page)
  const names = Array.from({ length: 13 }, (_, index) => (
    `Grocery run ${String(index).padStart(2, '0')}`
  ))
  // Sorts ahead of every grocery name, so the first page before the search
  // lands differs from the first page after it. Without that the two are
  // identical and the assertions below pass against the unsearched list.
  await seedTodos(spaceId, createdByUserId, [...names, 'Alpha errand'])

  await page.reload()
  await page.getByLabel('Sort field').selectOption({ label: 'Name' })
  await page.getByLabel('Search filter').fill('grocery')

  // Retries until it matches, which is what waits out the debounce and the
  // request rather than sampling whatever is on screen first.
  await expect(activeCards(page).locator('h3')).toHaveText(names.slice(0, 12))

  await page.getByRole('button', { name: 'Load more' }).click()

  await expect(activeCards(page).locator('h3')).toHaveText(names)
  const ids = await activeCards(page).evaluateAll(
    (cards) => cards.map((card) => card.getAttribute('data-testid')),
  )
  expect(new Set(ids).size).toBe(ids.length)
})

/**
 * A cursor is bound to the filters that produced it, so sending one minted
 * under the previous search alongside new search text is refused with a 400.
 * The window in which that was reachable is the one between the debounce
 * committing and the fresh first page arriving, when the old cursor was still
 * on screen behind a live Load more button.
 *
 * The fix removes the button for that window, so what is asserted is its
 * absence: the bad request can no longer be made, and no error toast follows.
 */
test('offers no stale cursor while a searched page is still loading', async ({ page }) => {
  const spaceId = await currentSpaceId(page)
  const createdByUserId = await currentUserId(page)
  await seedTodos(
    spaceId,
    createdByUserId,
    Array.from({ length: 13 }, (_, index) => `Grocery run ${String(index).padStart(2, '0')}`),
  )

  await page.reload()
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible()

  // Holds the searched first page open, which is exactly the window the stale
  // cursor used to survive in.
  let release = (): void => {}
  const held = new Promise<void>((resolve) => { release = resolve })
  await page.route(
    (url) => url.pathname.endsWith('/todos') && url.searchParams.has('search'),
    async (route) => {
      await held
      await route.continue()
    },
  )

  await page.getByLabel('Search filter').fill('grocery')
  await expect(page.getByRole('button', { name: 'Load more' })).toHaveCount(0)

  release()
  await expect(activeCards(page)).toHaveCount(12)
  await expect(page.getByRole('alert')).toHaveCount(0)
})

test('searches prerequisites on the server rather than within a loaded page', async ({ page }) => {
  await createTodo(page, 'Renew passport')
  await createTodo(page, 'Book a haircut')
  const dependent = await createTodo(page, 'Plan the trip')

  await dependent.getByRole('button', { name: 'Manage' }).click()
  const picker = dependent.getByLabel('Dependency for Plan the trip')

  // Every candidate but the card itself, before anything is typed.
  await expect(picker.locator('option')).toHaveCount(3)

  await dependent.getByLabel('Search TODOs').fill('pass')
  await expect(picker.locator('option')).toHaveCount(2)
  await expect(picker.locator('option').last()).toHaveText('Renew passport')

  await picker.selectOption({ label: 'Renew passport' })
  await dependent.getByRole('button', { name: 'Add', exact: true }).click()

  await expect(todoCard(page, 'Plan the trip').getByText('Blocked', { exact: true }))
    .toBeVisible()
  await expect(todoCard(page, 'Plan the trip')).toContainText('Renew passport')
})
