import { startLoader } from './start'

// Runs the moment this tiny script arrives, long before the app's main bundle has loaded, so the first thing anyone
// sees is the animation and not a blank page. The app removes #boot once it has drawn its own first screen.
const canvas = document.querySelector<HTMLCanvasElement>('#boot canvas')
if (canvas) startLoader(canvas)
