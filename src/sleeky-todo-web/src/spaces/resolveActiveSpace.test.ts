import { describe, expect, it } from 'vitest'

import { spacePermission, type SpaceSummary } from '../types/space.ts'
import { resolveActiveSpace } from './resolveActiveSpace.ts'

const personal: SpaceSummary = {
  id: 'personal',
  name: 'My Space',
  permission: spacePermission.owner,
}

const shared: SpaceSummary = {
  id: 'shared',
  name: 'Project Alpha',
  permission: spacePermission.write,
}

const readOnly: SpaceSummary = {
  id: 'read-only',
  name: 'Roadmap',
  permission: spacePermission.read,
}

const spaces = [personal, shared, readOnly]

describe('resolveActiveSpace', () => {
  it('honours the requested Space when the user can reach it', () => {
    expect(resolveActiveSpace(spaces, 'shared', 'personal')).toBe(shared)
  })

  it('does not care what permission the requested Space grants', () => {
    expect(resolveActiveSpace(spaces, 'read-only', null)).toBe(readOnly)
  })

  it('falls back to the remembered Space when the request is not accessible', () => {
    expect(resolveActiveSpace(spaces, 'revoked', 'shared')).toBe(shared)
  })

  it('falls back to the first Space when neither is accessible', () => {
    expect(resolveActiveSpace(spaces, 'revoked', 'also-gone')).toBe(personal)
  })

  it('lands on the first Space when nothing was requested or remembered', () => {
    expect(resolveActiveSpace(spaces, null, null)).toBe(personal)
  })

  it('treats an empty identifier as no request', () => {
    expect(resolveActiveSpace(spaces, '', '')).toBe(personal)
  })

  it('resolves to nothing when the user has no Spaces', () => {
    expect(resolveActiveSpace([], 'shared', 'personal')).toBeNull()
  })

  it('does not mutate the list it is given', () => {
    const copy = [...spaces]

    resolveActiveSpace(copy, 'revoked', null)

    expect(copy).toEqual(spaces)
  })
})
