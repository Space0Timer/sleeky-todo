import { expect, test, type Page } from '@playwright/test'

import { signIn } from './auth.ts'
import { databaseName } from './database-name.ts'
import { evaluate, resetOwnedData } from './database.ts'
import { createTodo, currentUserId, todoCard } from './todos.ts'

const todoCollection = `db.getSiblingDB('${databaseName}').todoItems`

function seededId(index: number): string {
  return `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`
}

/**
 * Search reads the stored `searchTokens` rather than the name, so a seed that
 * omits them is invisible to every assertion here. Seeding runs after the API
 * has started, which means the startup backfill has already been and gone and
 * will not repair these documents.
 *
 * The tokenizer's rules are mirrored here only as far as the fixed names below
 * need: lowercase, split on non-alphanumeric runs. Anything more elaborate
 * belongs in a TODO created through the UI instead.
 */
async function seedTodos(ownerId: string, names: string[]): Promise<void> {
  const documents = names.map((name, index) => ({
    _id: seededId(index),
    ownerId,
    name,
    nameNormalized: name.toLowerCase(),
    description: 'Cursor acceptance record',
    searchTokens: [
      ...new Set(`${name} Cursor acceptance record`
        .toLowerCase()
        .split(/[^\p{L}\p{N}]+/u)
        .filter((token) => token.length > 0)),
    ],
    dueDate: '2027-04-19',
    status: 0,
    priority: 0,
    dependencyIds: [],
    recurrence: null,
    seriesId: null,
    occurrenceNumber: null,
    version: 1,
    createdAt: '2026-08-12T00:00:00.000Z',
    updatedAt: '2026-08-12T00:00:00.000Z',
    deletedAt: null,
    purgeAt: null,
  }))

  await evaluate(`
    const docs = ${JSON.stringify(documents)};
    docs.forEach((doc) => {
      doc._id = UUID(doc._id);
      doc.ownerId = UUID(doc.ownerId);
      doc.createdAt = new Date(doc.createdAt);
      doc.updatedAt = new Date(doc.updatedAt);
    });
    ${todoCollection}.deleteMany({ _id: { $in: docs.map((doc) => doc._id) } });
    ${todoCollection}.insertMany(docs);
  `)
}

function activeCards(page: Page) {
  return page.getByRole('region', { name: 'Active' }).locator('[data-testid^="todo-"]')
}

test.beforeEach(async ({ page }) => {
  await signIn(page)
  await resetOwnedData(page)
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
    (url) => url.pathname === '/api/todos',
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
  const ownerId = await currentUserId(page)
  const names = Array.from({ length: 13 }, (_, index) => (
    `Grocery run ${String(index).padStart(2, '0')}`
  ))
  // Sorts ahead of every grocery name, so the first page before the search
  // lands differs from the first page after it. Without that the two are
  // identical and the assertions below pass against the unsearched list.
  await seedTodos(ownerId, [...names, 'Alpha errand'])

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
  const ownerId = await currentUserId(page)
  await seedTodos(
    ownerId,
    Array.from({ length: 13 }, (_, index) => `Grocery run ${String(index).padStart(2, '0')}`),
  )

  await page.reload()
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible()

  // Holds the searched first page open, which is exactly the window the stale
  // cursor used to survive in.
  let release = (): void => {}
  const held = new Promise<void>((resolve) => { release = resolve })
  await page.route(
    (url) => url.pathname === '/api/todos' && url.searchParams.has('search'),
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
