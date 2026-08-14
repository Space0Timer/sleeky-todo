import { dropDatabase } from './database.ts'

/**
 * Removes the suite's database once nothing is left to run. Global setup empties
 * the collections instead of calling this, because at that point the API is
 * already running on indexes a drop would take with it.
 */
export default async function globalTeardown() {
  await dropDatabase()
}
