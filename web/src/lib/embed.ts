import { useEffect } from 'react'
import { EMBED } from './config'

export const HEIGHT_MESSAGE = 'nitrodrp:height'

/**
 * In embed mode, tell the parent page how tall the content is so it can size the iframe.
 * Only the height is sent (no URLs, tokens or file names), so posting to any origin is harmless.
 */
export function useEmbedAutoHeight() {
  useEffect(() => {
    if (!EMBED || window.parent === window) return
    // offsetHeight is the page's own content height. scrollHeight is never smaller than the viewport, which in an
    // iframe is the frame's current height, so using it the frame could grow but never shrink back.
    const post = () => window.parent.postMessage({ type: HEIGHT_MESSAGE, height: document.documentElement.offsetHeight }, '*')
    const observer = new ResizeObserver(post)
    observer.observe(document.body)
    post()
    return () => observer.disconnect()
  }, [])
}
