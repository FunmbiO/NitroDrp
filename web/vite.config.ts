/// <reference types="vitest/config" />
import { build, defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'

/**
 * In a production build, bundles the small loading-screen script and puts it directly inside index.html. Left as a
 * normal script, Vite would fold it into the main bundle, and the animation couldn't start until all of that had
 * downloaded: the blank screen it exists to replace. Inline, it runs the moment the page is parsed.
 */
function inlineBoot(): Plugin {
  const tag = '<script type="module" src="/src/loader/boot.ts"></script>'
  return {
    name: 'inline-boot',
    apply: 'build',
    transformIndexHtml: {
      order: 'pre',
      async handler(html) {
        if (!html.includes(tag)) return html
        const out = await build({
          configFile: false,
          logLevel: 'silent',
          build: { write: false, minify: true, lib: { entry: 'src/loader/boot.ts', formats: ['iife'], name: 'nitrodrpBoot' } },
        })
        const bundle = Array.isArray(out) ? out[0] : out
        const code = 'output' in bundle ? bundle.output[0].code : ''
        if (!code) throw new Error('inline-boot: the loading-screen script did not build')
        // a function, not a string: the minified code is full of `$`, which `replace` would read as patterns
        return html.replace(tag, () => `<script>${code.replace(/<\/script/gi, '<\\/script')}</script>`)
      },
    },
  }
}

export default defineConfig({
  plugins: [react(), inlineBoot()],
  server: {
    port: 5173,
    // `npm run dev` talks to the API started with `dotnet run` (see docs/DEVELOPMENT.md)
    proxy: { '/api': 'http://localhost:8080', '/mock-storage': 'http://localhost:8080' },
    host: true,
    allowedHosts: true, // dev server only: lets a cloud/remote preview hostname load the page
  },
  test: { environment: 'node' },
})
