import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { UploadPage } from './pages/UploadPage'
import { CreatedPage } from './pages/CreatedPage'
import { RecentPage } from './pages/RecentPage'
import { DownloadPage } from './pages/DownloadPage'
import { DevicesPage } from './pages/DevicesPage'
import { AuthProvider } from './lib/auth'
import { RequireSignIn } from './components/RequireSignIn'
import { EMBED } from './lib/config'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <Routes>
          <Route element={<Layout />}>
            {/* Recipients open a link without signing in */}
            <Route path="/d/:token" element={<DownloadPage />} />
            {/* The sender's pages */}
            <Route element={<RequireSignIn />}>
              <Route path="/" element={<UploadPage />} />
              <Route path="/created/:token" element={<CreatedPage />} />
              <Route path="/recent" element={<RecentPage />} />
              <Route path="/devices" element={<DevicesPage />} />
              <Route path="*" element={<UploadPage />} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)

// Lets the installed app open offline and load fast. Production builds only, and never inside an embed.
if ('serviceWorker' in navigator && import.meta.env.PROD && !EMBED) {
  addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      /* no service worker (private mode, blocked): the site works exactly as before */
    })
  })
}
