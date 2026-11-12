import { MAX_MESSAGE, parseRecipients } from '../lib/email'

export interface EmailDraft {
  recipients: string
  senderName: string
  senderEmail: string
  message: string
}
export const emptyDraft: EmailDraft = { recipients: '', senderName: '', senderEmail: '', message: '' }

/** Why this draft can't be sent yet, or '' when it can. Shown under the box as you type. */
export function draftProblem(d: EmailDraft, maxPerRequest: number): string {
  const { valid, invalid } = parseRecipients(d.recipients)
  if (invalid.length) return `"${invalid[0]}" isn't a valid email address.`
  if (valid.length > maxPerRequest) return `Send to at most ${maxPerRequest} people at a time (you have ${valid.length}).`
  if (d.senderEmail.trim() && parseRecipients(d.senderEmail).valid.length !== 1) return 'Your email address isn\'t valid.'
  return ''
}

interface Props {
  draft: EmailDraft
  onChange: (d: EmailDraft) => void
  idPrefix: string
  maxPerRequest: number
  disabled?: boolean
}

/** The shared inputs for "also send this by email". The sender's address only receives replies; mail is sent from the server's own address. */
export function EmailFields({ draft, onChange, idPrefix, maxPerRequest, disabled }: Props) {
  const set = (patch: Partial<EmailDraft>) => onChange({ ...draft, ...patch })
  const { valid } = parseRecipients(draft.recipients)
  const problem = draftProblem(draft, maxPerRequest)
  return (
    <div className="stack email-fields" style={{ gap: 14 }}>
      <div className="field">
        <label htmlFor={`${idPrefix}-to`}>Send to</label>
        <textarea
          id={`${idPrefix}-to`}
          className="input"
          rows={2}
          placeholder="name@example.com, another@example.com"
          autoComplete="off"
          spellCheck={false}
          disabled={disabled}
          value={draft.recipients}
          onChange={(e) => set({ recipients: e.target.value })}
          aria-describedby={`${idPrefix}-hint`}
        />
        <p id={`${idPrefix}-hint`} className={problem ? 'error' : 'hint'} role={problem ? 'alert' : undefined} style={{ margin: 0 }}>
          {problem || (valid.length ? `${valid.length} recipient${valid.length === 1 ? '' : 's'}. Separate addresses with commas or new lines.` : `Up to ${maxPerRequest} at a time. Separate addresses with commas or new lines.`)}
        </p>
      </div>
      <div className="two">
        <div className="field">
          <label htmlFor={`${idPrefix}-name`}>Your name</label>
          <input id={`${idPrefix}-name`} className="input" maxLength={60} autoComplete="name" disabled={disabled} value={draft.senderName} onChange={(e) => set({ senderName: e.target.value })} />
        </div>
        <div className="field">
          <label htmlFor={`${idPrefix}-from`}>Your email (for replies)</label>
          <input id={`${idPrefix}-from`} className="input" type="email" autoComplete="email" disabled={disabled} value={draft.senderEmail} onChange={(e) => set({ senderEmail: e.target.value })} />
        </div>
      </div>
      <div className="field">
        <label htmlFor={`${idPrefix}-msg`}>Message <span className="muted" style={{ fontWeight: 400 }}>(optional)</span></label>
        <textarea id={`${idPrefix}-msg`} className="input" rows={3} maxLength={MAX_MESSAGE} disabled={disabled} value={draft.message} onChange={(e) => set({ message: e.target.value })} />
      </div>
      <p className="hint" style={{ margin: 0 }}>The email contains the link, never the password. If you set a password, tell them separately.</p>
    </div>
  )
}
