import { describe, expect, it } from 'vitest'

import { spacePermission, subjectType, type SpaceAccessEntry } from '../types/space.ts'
import { isOwner, memberLabel, toMemberRows } from './members.ts'

function entry(
  subjectId: string,
  permission: SpaceAccessEntry['permission'],
  displayName: string | null,
): SpaceAccessEntry {
  return { subjectId, subjectType: subjectType.user, permission, displayName }
}

const alice = entry('11111111-2222-3333-4444-555555555555', spacePermission.owner, 'Alice')
const bob = entry('bbbbbbbb-2222-3333-4444-555555555555', spacePermission.write, 'Bob')
const carol = entry('cccccccc-2222-3333-4444-555555555555', spacePermission.read, 'Carol')

describe('toMemberRows', () => {
  it('lists the most privileged first', () => {
    const rows = toMemberRows([carol, bob, alice], null)

    expect(rows.map((row) => row.label)).toEqual(['Alice', 'Bob', 'Carol'])
  })

  it('orders members holding the same level by name', () => {
    const zoe = entry('99999999-2222-3333-4444-555555555555', spacePermission.write, 'Zoe')
    const adam = entry('aaaaaaaa-2222-3333-4444-555555555555', spacePermission.write, 'Adam')

    const rows = toMemberRows([zoe, bob, adam], null)

    expect(rows.map((row) => row.label)).toEqual(['Adam', 'Bob', 'Zoe'])
  })

  it('marks the row belonging to the reader', () => {
    const rows = toMemberRows([alice, bob], bob.subjectId)

    expect(rows.map((row) => row.isCurrentUser)).toEqual([false, true])
  })

  it('marks nobody when there is no signed-in identifier to compare', () => {
    const rows = toMemberRows([alice, bob], null)

    expect(rows.some((row) => row.isCurrentUser)).toBe(false)
  })

  it('carries the subject identifier and permission through untouched', () => {
    const [row] = toMemberRows([bob], null)

    expect(row.subjectId).toBe(bob.subjectId)
    expect(row.permission).toBe(spacePermission.write)
  })

  it('does not mutate the list it is given', () => {
    const access = [carol, bob, alice]
    const copy = [...access]

    toMemberRows(access, null)

    expect(access).toEqual(copy)
  })
})

describe('memberLabel', () => {
  it('uses the display name the directory holds', () => {
    expect(memberLabel(alice)).toBe('Alice')
  })

  it('falls back to the head of the identifier when there is no name', () => {
    expect(memberLabel(entry(bob.subjectId, spacePermission.write, null))).toBe('bbbbbbbb')
  })

  it('treats a blank name as no name', () => {
    expect(memberLabel(entry(bob.subjectId, spacePermission.write, '   '))).toBe('bbbbbbbb')
  })
})

describe('isOwner', () => {
  it('is true only at the top of the lattice', () => {
    expect(isOwner(spacePermission.owner)).toBe(true)
    expect(isOwner(spacePermission.write)).toBe(false)
    expect(isOwner(spacePermission.read)).toBe(false)
  })
})
