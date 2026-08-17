import { databaseName } from './database-name.ts'
import { evaluate } from './database.ts'

const todoCollection = `db.getSiblingDB('${databaseName}').todoItems`

/** The identifier of the nth seeded TODO, which the seed derives from its index. */
export function seededId(index: number): string {
  return `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`
}

/**
 * Mirrors the domain tokenizer as far as these fixed names need: lowercase,
 * split on non-alphanumeric runs. Search reads the stored tokens rather than
 * the text, and nothing backfills them — the API writes the field only on its
 * own inserts and replaces — so a seed without them is a document no search
 * can reach.
 */
export function searchTokensFor(name: string): string[] {
  return [
    ...new Set(`${name} Cursor acceptance record`
      .toLowerCase()
      .split(/[^\p{L}\p{N}]+/u)
      .filter((token) => token.length > 0)),
  ]
}

/**
 * Seeds a page and a bit of TODOs straight into MongoDB. More than the create
 * form should have to type, and the ordering has to be exact.
 *
 * A TODO belongs to a Space and records who created it, so a seed needs both:
 * the Space is what every route and every repository filter matches on, and a
 * document seeded into another Space is invisible to the page under test.
 *
 * A retry runs the calling test again while the previous attempt's documents
 * are still present, because global teardown only drops the database once the
 * whole run has finished. These identifiers are fixed, so insertMany would fail
 * on a duplicate key and the retry would report that rather than whatever
 * actually went wrong. Clearing the same identifiers first makes the seed
 * idempotent.
 *
 * The delete has to follow the UUID conversion: identifiers persist as BSON
 * UUIDs rather than strings, so matching on the string form silently removes
 * nothing. It is scoped to these documents so that another spec's data is left
 * alone.
 */
export async function seedTodos(
  spaceId: string,
  createdByUserId: string,
  names: string[],
): Promise<void> {
  const documents = names.map((name, index) => ({
    _id: seededId(index),
    spaceId,
    createdByUserId,
    name,
    nameNormalized: name.toLowerCase(),
    description: 'Cursor acceptance record',
    searchTokens: searchTokensFor(name),
    dueDate: '2027-04-19',
    // Status and priority persist as their numeric business order, so a
    // seeded document must match that representation to be queryable.
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
      doc.spaceId = UUID(doc.spaceId);
      doc.createdByUserId = UUID(doc.createdByUserId);
      doc.createdAt = new Date(doc.createdAt);
      doc.updatedAt = new Date(doc.updatedAt);
    });
    ${todoCollection}.deleteMany({ _id: { $in: docs.map((doc) => doc._id) } });
    ${todoCollection}.insertMany(docs);
  `)
}

/** Rewrites a seeded TODO behind the running page, as a concurrent writer would. */
export function renameSeeded(index: number, name: string): Promise<void> {
  return evaluate(`
    ${todoCollection}.updateOne(
      { _id: UUID('${seededId(index)}') },
      {
        $set: {
          name: '${name}',
          nameNormalized: '${name.toLowerCase()}',
          // Written alongside the name, as every repository write does. A
          // partial rename would leave the document searchable only by the
          // name it no longer has.
          searchTokens: ${JSON.stringify(searchTokensFor(name))},
        },
        $inc: { version: 1 },
      },
    );
  `)
}
