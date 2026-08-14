/**
 * The suite's own database, deliberately not the one a developer runs against
 * (`sleekyTodo`, from the API's appsettings).
 *
 * Everything the suite does to MongoDB deletes, so this name is the only thing
 * standing between it and real data. It lives alone, with no imports, because
 * both the Playwright config and the cleanup helpers read it: were the API
 * pointed at one database while the cleanup emptied another, the tests would
 * run against — and trample — whatever was in the first.
 */
export const databaseName = 'sleekyTodoPlaywright'
