import { browserStore, createDemoBackend } from './demoApi'

/**
 * The single demo backend, or undefined in a normal build. The literal comparison lets the bundler
 * drop all demo code from production builds.
 */
export const demo = import.meta.env.VITE_DEMO === '1' ? createDemoBackend(browserStore()) : undefined
