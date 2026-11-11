import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { UploadPage } from './pages/UploadPage'
import { CreatedPage } from './pages/CreatedPage'
import { RecentPage } from './pages/RecentPage'
import { DownloadPage } from './pages/DownloadPage'
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
