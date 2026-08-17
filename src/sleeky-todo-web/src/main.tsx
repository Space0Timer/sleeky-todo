import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router'

import './index.scss'
import { AuthProvider } from './auth/AuthProvider.tsx'
import { ProtectedRoute } from './auth/ProtectedRoute.tsx'
import { LoginPage } from './pages/LoginPage.tsx'
import { SpaceProvider } from './spaces/SpaceProvider.tsx'
import { SpaceRedirect } from './spaces/SpaceRedirect.tsx'
import { SpaceRoute } from './spaces/SpaceRoute.tsx'

/**
 * The open Space lives in the URL. `/` only picks one and moves there, and
 * anything unrecognised goes back to `/` to be picked for. The Space list is
 * loaded once for every protected route, above both, so the redirect and the
 * page decide from the same list.
 */
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route
            element={
              <ProtectedRoute>
                <SpaceProvider>
                  <Outlet />
                </SpaceProvider>
              </ProtectedRoute>
            }
          >
            <Route path="/" element={<SpaceRedirect />} />
            <Route path="/spaces/:spaceId" element={<SpaceRoute />} />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
