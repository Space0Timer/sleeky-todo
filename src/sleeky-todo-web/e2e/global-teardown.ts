import { execFile } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'

const execFileAsync = promisify(execFile)
const repositoryRoot = fileURLToPath(new URL('../../..', import.meta.url))

export default async function globalTeardown() {
  await execFileAsync(
    'docker',
    [
      'compose',
      'exec',
      '-T',
      'mongodb',
      'mongosh',
      '--quiet',
      '--eval',
      'db.getSiblingDB("sleekyTodoPlaywright").dropDatabase()',
    ],
    { cwd: repositoryRoot },
  )
}
