/** Demo mode: the whole backend runs in the browser (see demoApi.ts). Built with `vite --mode demo`. */
export const DEMO = import.meta.env.VITE_DEMO === '1'

/**
 * Embed mode (?embed=1): for an <iframe> in another site. Read once at load so it survives in-app
 * navigation, which drops the query string.
 */
export const EMBED = typeof location !== 'undefined' && new URLSearchParams(location.search).has('embed')

if (typeof document !== 'undefined' && EMBED) document.documentElement.dataset.embed = 'true'
