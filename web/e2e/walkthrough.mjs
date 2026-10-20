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
    'This is the starter app. A person signs in through the identity provider with the authorization code flow; the API verifies every token itself, and this page shows the claims it reads.',
  writes:
    'A product and a customer are two synchronous writes. Each needs its scope and a second factor; the API answers with the record it created.',
  order:
    'An order is one commit that also writes its event to the outbox, and confirming it does the same. The Service Bus topic carries the events to the Functions.',
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
