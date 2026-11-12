import { chromium } from '../web/node_modules/playwright/index.mjs'
const b = await chromium.launch()
const p = await b.newPage()
await p.goto('file://' + process.argv[2])
await p.pdf({
  path: process.argv[3], format: 'Letter', printBackground: true, displayHeaderFooter: true,
  headerTemplate: '<span></span>',
  footerTemplate: '<div style="font-size:8px;width:100%;text-align:center;color:#888;font-family:sans-serif">NitroDrp Handbook &middot; <span class="pageNumber"></span> / <span class="totalPages"></span></div>',
  margin: { top: '18mm', bottom: '18mm', left: '16mm', right: '16mm' },
})
await b.close()
