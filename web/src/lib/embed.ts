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
    const post = () => window.parent.postMessage({ type: HEIGHT_MESSAGE, height: document.documentElement.scrollHeight }, '*')
    const observer = new ResizeObserver(post)
    observer.observe(document.body)
    post()
    return () => observer.disconnect()
  }, [])
}
