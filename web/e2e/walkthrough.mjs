// Records a narrated walkthrough of the sample: sign in, create a product and a customer, place and confirm an
// order, and see what the API answers. A derived project replaces the story with its own (AGENTS.md).
//
// Each scene is narrated (e2e/lib/narration.mjs): one line per scene, spoken by an Australian neural voice through
// edge-tts and mixed onto the video where the scene began; NARRATOR=none records silently. Output is
// docs/media/starterapp-walkthrough.mp4 (ffmpeg on the path; without it, the silent .webm). Run against a live stack:
//   BASE_URL=http://localhost:5173 node e2e/walkthrough.mjs
import { chromium } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { clearStale, cueing, finish, synthesise } from './lib/narration.mjs'

const narration = {
  'sign-in':
    'This is the starter: the template every product in the flock begins from. The sample inside it is a tiny shop, just enough to show the moving parts. I sign in with a password and a second factor, and the page shows what the API learned about me from the token it checked itself.',
  writes:
    'Two straightforward writes. I create a product, then a customer. Each one needs the right permission and that second factor, and the API hands back the record it made.',
  order:
    'Now an order. Placing it is one commit that also leaves an event in the outbox, and confirming it does the same. From the outbox the events travel over the message bus to the functions that react to them. A product built on this template replaces the shop with its own story, and this video with its own.',
}

const base = process.env.BASE_URL ?? 'http://localhost:5173'
const out = process.env.OUT_DIR ?? '../docs/media'
mkdirSync(out, { recursive: true })
clearStale(out)
const { clips, work } = synthesise(narration, 'starterapp')

const size = { width: 1440, height: 900 }
const browser = await chromium.launch()
const context = await browser.newContext({ viewport: size, deviceScaleFactor: 1, recordVideo: { dir: out, size } })
const page = await context.newPage()
const { started, cues, scene, settle } = cueing(page, clips)
const pause = (ms = 1200) => page.waitForTimeout(ms)
const type = async (selector, text) => {
  await page.click(selector)
  await page.fill(selector, '')
  await page.type(selector, text, { delay: 35 })
}

// Sign in
await scene('sign-in')
await page.goto(base)
await page.waitForURL(/realms\/starterapp/)
await pause(800)
await type('#username', 'dev-user')
await type('#password', 'dev-password')
await pause(400)
await page.click('#kc-login')
await page.waitForURL((u) => u.origin === new URL(base).origin && !u.pathname.startsWith('/auth'))
await pause(2200)

// A product and a customer
await scene('writes')
await page.getByRole('button', { name: 'Create the product' }).click()
await pause(1800)
await page.getByRole('button', { name: 'Create the customer' }).scrollIntoViewIfNeeded()
await page.getByRole('button', { name: 'Create the customer' }).click()
await pause(1800)
// An order, placed and confirmed
await scene('order')
await page.getByRole('button', { name: 'Place the order' }).scrollIntoViewIfNeeded()
await page.getByRole('button', { name: 'Place the order' }).click()
await pause(1800)
await page.getByRole('button', { name: 'Confirm the order' }).click()
await pause(2500)

await settle()
await context.close()
await browser.close()

console.log(finish({ out, name: 'starterapp', started, cues, clips, work }))
