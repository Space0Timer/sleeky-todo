import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

/**
 * `changeOrigin` stays false and `xfwd` is enabled so the API builds its OpenID
 * Connect redirect URI from this development origin. With the API's own host
 * instead, the provider returns the browser to a origin where the correlation
 * cookie does not exist and login cannot complete.
 */
const apiProxy = {
  target: 'https://localhost:7238',
  changeOrigin: false,
  secure: false,
  xfwd: true,
}

export default defineConfig({
  plugins: [react()],
  build: {
    /*
     * The polyfill is emitted as an inline script, which the content security
     * policy the API serves does not allow — and the alternative, widening the
     * policy to `unsafe-inline`, gives up most of what having one is for. Every
     * browser that supports the module scripts this build emits also supports
     * `modulepreload`, so the polyfill has nothing left to do.
     */
    modulePreload: { polyfill: false },
  },
  css: {
    modules: {
      /*
       * Stylesheets keep kebab-case class names, which is the convention the
       * styling rules enforce, while components read them as `styles.todoCard`.
       * `camelCaseOnly` removes the original key rather than adding an alias,
       * so there is one spelling per class instead of two.
       */
      localsConvention: 'camelCaseOnly',
    },
  },
  // Unit tests cover the pieces Playwright cannot reach: the event-stream
  // parser's buffer handling, which only misbehaves at chunk boundaries a
  // browser test has no way to control. Node rather than a DOM, because
  // ReadableStream and TextDecoder are globals there.
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
  server: {
    proxy: {
      '/api': apiProxy,
      '/health': apiProxy,
      '/signin-oidc': apiProxy,
      '/signout-callback-oidc': apiProxy,
    },
  },
})
