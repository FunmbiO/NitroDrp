import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { parseLink } from '../lib/link'

/** Paste a link someone sent (or one you made) and go straight to its download page. */
export function OpenLinkPage() {
  const nav = useNavigate()
  const [text, setText] = useState('')
  const [error, setError] = useState('')
  const canPaste = typeof navigator !== 'undefined' && typeof navigator.clipboard?.readText === 'function'

  const open = (value: string) => {
    const token = parseLink(value)
    if (!token) {
      setError("That doesn't look like a NitroDrp link. Paste the whole link, like https://…/d/…")
      return false
    }
    nav(`/d/${token}`)
    return true
  }

  const pasteFromClipboard = async () => {
    setError('')
    try {
      const clip = await navigator.clipboard.readText()
      setText(clip)
      open(clip)
    } catch {
      setError("Couldn't read the clipboard. Long-press the box below and choose Paste.")
    }
  }

  return (
    <main className="page narrow">
      <div className="card roomy stack">
        <h1 className="sm">Open a <mark>link</mark></h1>
        <p className="muted" style={{ margin: 0 }}>Paste a link and it takes you to the download page.</p>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            open(text)
          }}
        >
          <div className="field">
            <label htmlFor="open-link">Link</label>
            <input
              id="open-link"
              className="input big"
              type="text"
              inputMode="url"
              autoFocus
              autoCapitalize="off"
              autoCorrect="off"
              autoComplete="off"
              spellCheck={false}
              placeholder="https://drop.jadedmedia.ca/d/…"
              value={text}
              onChange={(e) => {
                setText(e.target.value)
                setError('')
              }}
              // Pasting a whole link needs no extra tap: if it's a link, go.
              onPaste={(e) => {
                const pasted = e.clipboardData.getData('text')
                if (parseLink(pasted)) {
                  e.preventDefault()
                  setText(pasted)
                  open(pasted)
                }
              }}
            />
          </div>
          {error && <p className="error" role="alert">{error}</p>}
          <button type="submit" className="btn big" disabled={text.trim().length === 0}>Open link</button>
          {canPaste && (
            <button type="button" className="btn outline big" onClick={() => void pasteFromClipboard()}>Paste from clipboard</button>
          )}
        </form>
      </div>
    </main>
  )
}
