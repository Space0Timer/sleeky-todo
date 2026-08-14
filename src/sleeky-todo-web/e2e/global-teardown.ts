import { dropDatabase } from './database.ts'

/**
 * Also the global setup, which re-exports this: the run starts and ends on an
 * empty database. Isolation between individual tests is `resetOwnedData`'s
 * job, because a run-level drop cannot help a suite that shares one database
 * across every spec.
 */
export default async function globalTeardown() {
  await dropDatabase()
}
