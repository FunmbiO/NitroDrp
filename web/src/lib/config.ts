/** Demo mode: the whole backend runs in the browser (see demoApi.ts). Built with `vite --mode demo`. */
export const DEMO = import.meta.env.VITE_DEMO === '1'

/**
 * Embed mode (?embed=1): for an <iframe> in another site. Read once at load so it survives in-app
 * navigation, which drops the query string.
 */
export const EMBED = typeof location !== 'undefined' && new URLSearchParams(location.search).has('embed')

if (typeof document !== 'undefined' && EMBED) document.documentElement.dataset.embed = 'true'

/**
 * Whose app this is, and where the demo lives. Strangers who find the site see a welcome page with these, not the app.
 * Set `VITE_OWNER_NAME` and `VITE_DEMO_URL` when building to use it for your own.
 */
export const OWNER_NAME: string = import.meta.env.VITE_OWNER_NAME ?? 'Funmbi'
export const DEMO_URL: string = import.meta.env.VITE_DEMO_URL ?? 'https://demo.jadedmedia.ca'
