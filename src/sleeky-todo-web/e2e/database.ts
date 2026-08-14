import { execFile } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

import { type Page } from '@playwright/test'

import { databaseName } from './database-name.ts'
import { currentUserId } from './todos.ts'

const execFileAsync = promisify(execFile)
const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))

/** Runs a mongosh script against the suite's database. */
export async function evaluate(script: string): Promise<void> {
  await execFileAsync(
    'docker',
    ['compose', 'exec', '-T', 'mongodb', 'mongosh', '--quiet', '--eval', script],
    { cwd: repositoryRoot },
  )
}

/**
 * Clears everything the signed-in user owns, then reloads the page onto the
 * empty list.
 *
 * The whole suite shares one database and one page of the list holds twelve
 * TODOs sorted by due date, so without this a test that creates a TODO and
 * expects to see it depends on how many earlier tests happened to leave
 * something behind. That is not a property any test should have to reason
 * about, and dating records early to dodge it only moves the problem to
 * whoever writes the next test.
 *
 * Deleting through the API would not do: a soft-deleted TODO leaves Active and
 * Archived but fills the trash, which paginates the same way. This removes the
 * documents outright, which is what the seeding in `todo-crud.spec.ts` already
 * does for the same reason.
 *
 * Scoped to one owner rather than emptying the collections, because ownership
 * is the boundary the application itself isolates on, and a blanket delete
 * would reach across into another test's data.
 */
export async function resetOwnedData(page: Page): Promise<void> {
  const ownerId = await currentUserId(page)

  // Identifiers persist as BSON UUIDs rather than strings, so a filter built
  // from the string form silently matches nothing.
  await evaluate(`
    const database = db.getSiblingDB('${databaseName}');
    const owner = UUID('${ownerId}');
    database.todoItems.deleteMany({ ownerId: owner });
    database.assistantSettings.deleteMany({ _id: owner });
  `)

  await page.reload()
}

export function dropDatabase(): Promise<void> {
  return evaluate(`db.getSiblingDB('${databaseName}').dropDatabase()`)
}

/**
 * Empties every collection without dropping them, which is the difference that
 * matters at the start of a run: Playwright launches the API before global
 * setup, so the API has already created its indexes by the time this runs.
 * Dropping the database would take those indexes with it, and the list query
 * pins itself to the search index by name — a hint that names a missing index
 * fails the query outright rather than falling back to a scan. Only search
 * would break, and only inside the browser suite.
 */
export function emptyDatabase(): Promise<void> {
  return evaluate(`
    const database = db.getSiblingDB('${databaseName}');
    database.getCollectionNames().forEach((name) => {
      database.getCollection(name).deleteMany({});
    });
  `)
}
