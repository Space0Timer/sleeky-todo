import { execFile } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

import { type Page } from '@playwright/test'

import { databaseName } from './database-name.ts'
import { currentUserId, listSpaces } from './todos.ts'

const execFileAsync = promisify(execFile)
const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))

/** The permission value an Owner entry carries, stored as its numeric order. */
const ownerPermission = 3

/** Runs a mongosh script against the suite's database. */
export async function evaluate(script: string): Promise<void> {
  await execFileAsync(
    'docker',
    ['compose', 'exec', '-T', 'mongodb', 'mongosh', '--quiet', '--eval', script],
    { cwd: repositoryRoot },
  )
}

/**
 * Clears everything the signed-in user has accumulated, then lands the page on
 * a resolved Space again.
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
 * documents outright, which is what the seeding helpers already do for the
 * same reason.
 *
 * Three things go: the TODOs in every Space the user can reach, the Spaces
 * they created (anything but their personal one that they solely own — a Space
 * with a second Owner belongs to someone else as much as to them), and their
 * assistant settings. The personal Space itself stays, because the server
 * derives its identifier from the user and would only recreate it.
 *
 * Scoped to one user's Spaces rather than emptying the collections, because a
 * Space is the boundary the application itself isolates on and a blanket
 * delete would reach across into another test's data.
 */
export async function resetUserData(page: Page): Promise<void> {
  const userId = await currentUserId(page)
  const spaces = await listSpaces(page)

  // Oldest first, and the personal Space is ensured on the user's very first
  // list call, so it is the one the suite must leave behind.
  const personalSpaceId = spaces[0]?.id ?? null

  if (personalSpaceId === null) {
    throw new Error('The signed-in user has no spaces to reset.')
  }

  // Identifiers persist as BSON UUIDs rather than strings, so a filter built
  // from the string form silently matches nothing.
  await evaluate(`
    const database = db.getSiblingDB('${databaseName}');
    const user = UUID('${userId}');
    const personal = UUID('${personalSpaceId}');
    const spaceIds = ${JSON.stringify(spaces.map((space) => space.id))}
      .map((id) => UUID(id));

    database.todoItems.deleteMany({ spaceId: { $in: spaceIds } });

    // Matched by the server rather than compared in the shell: a UUID read back
    // from a document stringifies to its canonical text, while one built here
    // stringifies to its raw bytes, so comparing the two forms never matches
    // and every Space would survive the reset.
    database.spaces.deleteMany({
      $and: [
        { _id: { $in: spaceIds, $ne: personal } },
        { access: { $elemMatch: { subjectId: user, permission: ${ownerPermission} } } },
        {
          access: {
            $not: {
              $elemMatch: {
                subjectId: { $ne: user },
                permission: ${ownerPermission},
              },
            },
          },
        },
      ],
    });

    database.assistantSettings.deleteMany({ _id: user });
  `)

  // The root rather than a reload: the page may be sitting on a Space that has
  // just been deleted, and `/` resolves whatever the user still has.
  await page.goto('/')
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
