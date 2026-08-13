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
  server: {
    proxy: {
      '/api': apiProxy,
      '/health': apiProxy,
      '/signin-oidc': apiProxy,
      '/signout-callback-oidc': apiProxy,
    },
  },
})
