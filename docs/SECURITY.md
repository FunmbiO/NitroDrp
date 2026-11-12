# Security and input handling

Every place a person can type, paste or send something to NitroDrp, what's checked, and where. The rule throughout:
**the server is the real gate**; the page checks the same things only so the message appears next to the box
before anything is sent.

## Every input

| Input | Limits and checks | Where |
|---|---|---|
| **Files** (name, size, type) | Names reduced to a plain name: path pieces, control characters and direction-flipping or zero-width characters removed (`photo‮gpj.exe` can't pose as an image). Empty, oversized, too many and blocked types (`.exe`, `.bat`, ...) are refused, and the page says so on the file itself, using the limits the server publishes at `/api/config`. The name is never part of the storage path: that's a random id. | page + server |
| **Expiry** | Only the server's fixed choices (1, 7, 30 days). | server |
| **Download limit** | A whole number from 1 to 10,000, or empty for no limit. Letters, signs, decimals, exponents and `0` are refused with a message. | page + server |
| **Link password** | Up to 256 characters, stored only as a salted PBKDF2 hash, never logged, never emailed. | page + server |
| **Unlock password** (recipient) | Up to 256 characters. Anything longer is refused without running the slow hash, so it can't be used to burn CPU. Wrong guesses are rate limited. | page + server |
| **Access key** | Up to 256 characters, compared in constant time. | page + server |
| **Email recipients** | Plain `name@domain.tld` only: no `Name <a@b>`, lists, quotes or line breaks (so no extra recipients or headers can be smuggled in). 10 per send, 20 per link. | page + server |
| **Your name, your email, message** | Name: 60 characters, one line. Reply-to: a plain address, 254 characters. Message: 1,000 characters; line breaks kept, other control and direction characters removed. In the email everything is HTML-encoded, and the subject is a single line. | page + server |
| **Device name** (passkey) | 40 characters, one line. | page + server |
| **Pasted link** | Only a link's code is extracted; the app then goes to its own `/d/<code>` page. It never navigates to a pasted address. | page |
| **Link code, file id, passkey id** (in the address) | Must be shaped like ours (letters and digits, 8 to 32 characters, and so on). Anything else gets "not found" before the database is touched. | server |
| **Request bodies** | At most 1 MB, strict JSON (a number must be a number, not `"7"`), nesting limited. Malformed input is a clean `400`, or `413` when too large, never a `500`. | server |
| **Headers** | Keys are hashed before comparing, so length can't be abused; the framework's header size limit applies. | server |

## Injection and abuse, one by one

| Attack | Why it doesn't work here |
|---|---|
| **XSS** (script in a name or message) | React escapes everything it shows; the code never uses `innerHTML`, `eval` or a link built from typed text. The email's HTML part encodes every dynamic value. Downloads are forced to `attachment`, from the storage or CDN domain, not this site. |
| **SQL / NoSQL injection** | There's no SQL. DynamoDB requests use fixed expression text with typed parameter values: typed text is never joined into an expression. |
| **Email header injection** | Recipients and reply-to must be plain addresses; subject and names are single-line; names are encoded for the headers. |
| **Path traversal / zip slip** | Names are reduced to a plain name, storage keys come from random ids, and zip entry names are made unique. |
| **CSRF** | The sign-in cookie is `SameSite=Strict` and anything that changes state also needs a custom header another site can't send. |
| **Brute force** | Per-visitor rate limits (stricter on passwords and sign-in), and 71-bit unguessable link codes. |
| **Denial of service by size** | 1 MB request cap, zip concurrency limit, bounded passwords, rate limits. |
| **Log injection / leaking personal data** | Logs never contain typed text or addresses, only counts and error types. |
| **SSRF / open redirect** | The server fetches only its own signed storage URLs; the app never redirects to a typed address. |
| **ReDoS** | The patterns that read pasted text are linear, and tested against 100,000-character input. |

## How this is checked

- `HostileInputTests` (API): about 45 hostile requests (broken JSON, wrong types, `null`s, 3 MB bodies, 5,000-level
  nesting, giant tokens and passwords, header injection, script and CRLF in every text field, bad passkey
  payloads) against the real HTTP pipeline. Each must get a clean 4xx, and mail must carry no injected content.
- `TextSafetyTests`: the cleaning rules, including that emoji built from several parts survive.
- `validate.test.ts` and `link.test.ts` (web): the page's rules, including `1e3`, `0x10`, `NaN` and 100,000-character input.
- `e2e/demo.mjs` (browser): every box refuses bad input with a visible reason, limits are enforced, and hostile
  names, messages and passwords are shown as text and run nothing.
- `dev/` scripts and CI run all of this on every change.

## Reporting a problem

Found something? Please open a private security advisory on the repository (Security tab, "Report a vulnerability")
rather than a public issue.
