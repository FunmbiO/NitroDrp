# The demo, and embedding it

The real app is private: it needs a server and AWS. For a portfolio, the project also builds as a **demo mode**:
the same UI with the whole backend running in the visitor's browser. Nothing is uploaded (files are never even
read), "downloads" are small placeholder files, and links only work in that browser. A banner says so. Every rule
still applies (expiry, passwords, download limits, resume, a real .zip), so it behaves like the product.

Live: [demo.jadedmedia.ca](https://demo.jadedmedia.ca)

```bash
cd web
npm run dev:demo      # try it locally
npm run build:demo    # a static site in web/dist
```

The demo code is tree-shaken out of normal builds, and `web/e2e/demo.mjs` tests the demo in a real browser,
including that it never sends a request to any server. See [DEVELOPMENT.md](DEVELOPMENT.md).

## Deploy it on Vercel

It's a static Vite site, so it deploys on Vercel's free tier with no server.

1. In Vercel, import the repository and set **Root Directory** to `web`. Everything else comes from
   `web/vercel.json`: the demo build, a fallback so `/d/<token>` links load, immutable asset caching, and
   permission to be framed.
2. To use your own domain, add it to the project and create the DNS record Vercel shows (a CNAME for a
   subdomain). Vercel then issues the HTTPS certificate by itself.
3. Switch off Vercel's deployment protection for the project, or visitors (and iframes) hit a Vercel login page.
   The demo holds no data.

Only the demo goes on Vercel. The real API is an ASP.NET Core service with multi-GB streaming, which belongs on a
server: see [DEPLOY.md](DEPLOY.md).

## Embed it in your portfolio

```html
<iframe id="nitrodrp" src="https://YOUR-DEMO-DOMAIN/?embed=1" title="NitroDrp demo"
        style="width:100%;border:0;height:700px"></iframe>
<script>
  // Optional: the app reports its content height so the frame never shows an inner scrollbar.
  addEventListener('message', (e) => {
    if (e.origin !== 'https://YOUR-DEMO-DOMAIN') return
    if (e.data?.type === 'nitrodrp:height') document.getElementById('nitrodrp').style.height = e.data.height + 'px'
  })
</script>
```

`?embed=1` hides the site header and tightens the spacing. The page posts only its height to the parent, and the
frame grows and shrinks with the content. The demo's `vercel.json` allows framing from any site; tighten
`frame-ancestors` there to your own domain if you prefer.

## Embedding the real app

Possible, but **only by origins you allow**: set `FRAME_ANCESTORS="'self' https://portfolio.example.com"` in `.env`
(the default is none). That's a CSP header from nginx, and it replaces `X-Frame-Options`, which can't allow a
specific site. You'd normally embed the demo rather than your private instance.
