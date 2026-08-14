import { emptyDatabase } from './database.ts'

/**
 * Starts the run on an empty database. It empties the collections rather than
 * dropping the database, because the API is already running by this point and
 * dropping would discard the indexes it created at startup. See
 * `emptyDatabase`. Isolation between individual tests is `resetOwnedData`'s
 * job, because a run-level clear cannot help a suite that shares one database
 * across every spec.
 */
export default async function globalSetup() {
  await emptyDatabase()
}
