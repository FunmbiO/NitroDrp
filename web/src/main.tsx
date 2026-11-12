import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { UploadPage } from './pages/UploadPage'
import { CreatedPage } from './pages/CreatedPage'
import { RecentPage } from './pages/RecentPage'
import { DownloadPage } from './pages/DownloadPage'
import { EMBED } from './lib/config'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<UploadPage />} />
          <Route path="/created/:token" element={<CreatedPage />} />
          <Route path="/recent" element={<RecentPage />} />
          <Route path="/d/:token" element={<DownloadPage />} />
          <Route path="*" element={<UploadPage />} />
        </Route>
      </Routes>
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
