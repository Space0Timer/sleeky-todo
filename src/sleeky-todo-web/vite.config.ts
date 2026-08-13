import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

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
  server: {
    proxy: {
      '/api': apiProxy,
      '/health': apiProxy,
      '/signin-oidc': apiProxy,
      '/signout-callback-oidc': apiProxy,
    },
  },
})
