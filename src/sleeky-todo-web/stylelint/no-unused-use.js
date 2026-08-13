import stylelint from 'stylelint'

const { createPlugin, utils } = stylelint

const ruleName = 'sleeky/no-unused-use'

const messages = utils.ruleMessages(ruleName, {
  /**
   * @param {string} namespace
   * @param {string} url
   */
  rejected: (namespace, url) =>
    `Unexpected unused namespace "${namespace}" loaded from "${url}"`,
})

const meta = { url: 'https://github.com/Space0Timer/sleeky-todo/blob/develop/docs/coding-standards.md' }

/**
 * Resolves the namespace a `@use` binds. `@use 'styles/tokens'` binds `tokens`;
 * an explicit `as` wins; `sass:map` binds `map`. A partial's leading underscore
 * and its extension are not part of the name. `as *` returns null because an
 * unnamespaced load leaves nothing to look for.
 *
 * @param {string} params
 * @returns {{ namespace: string, url: string } | null}
 */
function resolveNamespace(params) {
  const match = /^\s*["']([^"']+)["']\s*(?:as\s+(\S+))?/.exec(params)
  if (!match) return null

  const [, url, alias] = match
  if (alias === '*') return null
  if (alias) return { namespace: alias, url }

  const base = url.split('/').pop() ?? url
  const withoutScheme = base.startsWith('sass:') ? base.slice('sass:'.length) : base

  return {
    namespace: withoutScheme.replace(/^_/, '').replace(/\.s[ac]ss$/, ''),
    url,
  }
}

/**
 * Sass compiles a `@use` that nothing reads without complaint, so an import
 * outlives the declaration that needed it and the next reader cannot tell which
 * loads a file actually depends on. This reports the ones nothing references.
 */
const ruleFunction = (primary) => (root, result) => {
  const validOptions = utils.validateOptions(result, ruleName, {
    actual: primary,
    possible: [true],
  })

  if (!validOptions) return

  const loaded = []

  root.walkAtRules('use', (atRule) => {
    const resolved = resolveNamespace(atRule.params)
    if (resolved) loaded.push({ ...resolved, atRule })
  })

  if (loaded.length === 0) return

  // Every place a namespace can appear: property names and values, selectors,
  // and the parameters of any other at-rule, which is where `@include` and
  // `@if` reference the members they use.
  const references = []

  root.walkDecls((declaration) => references.push(declaration.prop, declaration.value))
  root.walkRules((rule) => references.push(rule.selector))
  root.walkAtRules((atRule) => {
    if (atRule.name !== 'use') references.push(atRule.params)
  })

  const haystack = references.join('\n')

  for (const { namespace, url, atRule } of loaded) {
    const escaped = namespace.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

    // A leading boundary keeps `t.` from matching the tail of `$want.`
    if (new RegExp(`(^|[^\\w$-])${escaped}\\.`, 'm').test(haystack)) continue

    utils.report({
      message: messages.rejected(namespace, url),
      node: atRule,
      result,
      ruleName,
    })
  }
}

ruleFunction.ruleName = ruleName
ruleFunction.messages = messages
ruleFunction.meta = meta

export default createPlugin(ruleName, ruleFunction)
