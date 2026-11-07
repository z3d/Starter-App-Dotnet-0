// PRESENTER=1 runs the walkthrough live for an audience: no voice or recording, a visible browser on the largest
// other display, and a console at http://localhost:4173 with each scene's line and Next, Pause, Auto, Quit and jump.
// Going back exits with 75 after writing the scene to PRESENTER_JUMP_FILE; the caller resets the database and reruns
// with PRESENTER_JUMP (run-e2e.sh --present).
import { chromium } from '@playwright/test'
import { execFileSync, spawn } from 'node:child_process'
import { writeFileSync } from 'node:fs'
import { createServer } from 'node:http'
import { emitKeypressEvents } from 'node:readline'
import { tmpdir } from 'node:os'
import { refusal } from './console-gate.mjs'

export const presenting = process.env.PRESENTER === '1'
export const jumpExitCode = 75
const jumpFile = process.env.PRESENTER_JUMP_FILE ?? `${tmpdir()}/walkthrough-presenter-jump`
const say = (s) => console.log(s)

export async function openBrowser({ out, size, context: options = {} }) {
  if (!presenting) {
    const browser = await chromium.launch()
    const context = await browser.newContext({
      ...options,
      viewport: size,
      deviceScaleFactor: 1,
      recordVideo: { dir: out, size },
    })
    return { browser, context, page: await context.newPage() }
  }
  const browser = await chromium.launch({
    headless: process.env.HEADLESS === '1',
    args: [...screenArgs(), '--start-maximized'],
  })
  const context = await browser.newContext({ ...options, viewport: null })
  const page = await context.newPage()
  // A cold dev server can take longer than the first scene's waits to serve the app; warm it before scene 1.
  await page.goto(process.env.BASE_URL ?? 'http://localhost:5173', { timeout: 120_000 }).catch(() => {})
  console_.closeBrowser = browser.close.bind(browser)
  console_.attach(page)
  context.close = () => console_.end()
  browser.close = () => console_.end()
  return { browser, context, page }
}

export function presenter(page, narration) {
  console_.steps = Object.entries(narration).map(([key, notes]) => ({ key, title: title(key), notes }))
  const original = page.waitForTimeout.bind(page)
  page.waitForTimeout = async (ms) => {
    if (!console_.jumpTo) await original(ms)
    await console_.hold()
  }
  const scene = async (key) => {
    const n = console_.steps.findIndex((s) => s.key === key) + 1
    console_.current = n
    say(`\n── Scene ${n}/${console_.steps.length}: ${title(key)} ──\n    ${narration[key] ?? ''}`)
    if (console_.jumpTo && n < console_.jumpTo) return console_.publish()
    console_.jumpTo = 0
    await console_.next('Next runs this scene')
  }
  return {
    started: Date.now(),
    cues: [],
    scene,
    settle: async () => {},
    current: () => console_.steps[console_.current - 1]?.key,
  }
}

const title = (key) => key.charAt(0).toUpperCase() + key.slice(1).replace(/-/g, ' ')

function screenArgs() {
  try {
    const js =
      'ObjC.import("AppKit"); const s = $.NSScreen.screens; const o = []; for (let i = 0; i < s.count; i++) { const f = s.objectAtIndex(i).frame; o.push([f.origin.x, f.origin.y, f.size.width, f.size.height]) } JSON.stringify(o)'
    const screens = JSON.parse(execFileSync('osascript', ['-l', 'JavaScript', '-e', js], { encoding: 'utf8' }))
    const others = screens.slice(1).sort((a, b) => b[2] * b[3] - a[2] * a[3])
    const [x, y, w, h] = process.env.PRESENTER_SCREEN === 'main' || others.length === 0 ? screens[0] : others[0]
    return [
      `--window-position=${Math.round(x)},${Math.round(screens[0][3] - (y + h))}`,
      `--window-size=${Math.round(w)},${Math.round(h)}`,
    ]
  } catch {
    return []
  }
}

const console_ = {
  steps: [],
  current: 0,
  jumpTo: Number(process.env.PRESENTER_JUMP ?? '0') || 0,
  auto: process.env.PRESENTER_AUTO === '1',
  paused: false,
  waiting: null,
  failure: null,
  finished: false,
  release: null,
  page: null,
  scratch: null,
  opening: null,
  clients: new Set(),
  run: Date.now().toString(36),

  attach(page) {
    this.page = page
    if (!presenting) return
    const port = Number(process.env.PRESENTER_PORT ?? '4173')
    createServer((req, res) => this.serve(req, res, port)).listen(port, '127.0.0.1', () => {
      say(`Presenter console: http://localhost:${port}`)
      if (process.env.PRESENTER_CONSOLE !== '0' && !process.env.PRESENTER_JUMP) openConsole(`http://localhost:${port}`)
    })
    // Reading a stdin that is not a terminal can stop the process; keys work only from a terminal.
    if (process.stdin.isTTY) {
      emitKeypressEvents(process.stdin)
      process.stdin.setRawMode(true)
      process.stdin.on('keypress', (_, key = {}) => {
        if (key.ctrl && key.name === 'c') return this.act('quit')
        const map = { q: 'quit', p: 'pause', a: 'auto', return: 'next', space: 'next', right: 'next' }
        if (map[key.name]) this.act(map[key.name])
      })
    }
    const fail = async (error) => {
      this.failure = String(error?.message ?? error).split('\n')[0]
      this.publish()
      console.error(`A step failed: ${this.failure}`)
      await this.page?.screenshot({ path: 'test-results/presenter-failure.png', fullPage: true }).catch(() => {})
      say('The browser stays where it stopped; carry on by hand. q quits.')
    }
    process.on('uncaughtException', fail)
    process.on('unhandledRejection', fail)
    process.on('SIGTERM', () => this.quit())
  },

  publish() {
    const state = {
      run: this.run,
      current: this.current,
      total: this.steps.length,
      waiting: this.waiting,
      paused: this.paused,
      auto: this.auto,
      failure: this.failure,
      finished: this.finished,
      jumpTo: this.jumpTo,
    }
    for (const res of this.clients) res.write(`data: ${JSON.stringify(state)}\n\n`)
  },

  serve(req, res, port) {
    const refused = refusal(req, port)
    if (refused) {
      res.writeHead(403, { 'Content-Type': 'text/plain' })
      return res.end(`Refused: ${refused}`)
    }
    if (req.url === '/events') {
      res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' })
      this.clients.add(res)
      req.on('close', () => this.clients.delete(res))
      return this.publish()
    }
    if (req.method === 'POST' && req.url.startsWith('/act/')) {
      this.act(decodeURIComponent(req.url.slice(5)))
      res.writeHead(204)
      return res.end()
    }
    if (req.url === '/steps') {
      res.writeHead(200, { 'Content-Type': 'application/json' })
      return res.end(JSON.stringify(this.steps))
    }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' })
    res.end(consolePage)
  },

  act(name) {
    const [verb, arg] = name.split('/')
    if (verb === 'next') {
      this.paused = false
      this.go()
    } else if (verb === 'pause') {
      this.paused = !this.paused
      if (this.paused) this.openScratch()
      else this.go()
    } else if (verb === 'auto') {
      this.auto = !this.auto
      if (this.auto) this.go()
    } else if (verb === 'quit') {
      this.quit()
    } else if (verb === 'jump') {
      const n = Number(arg)
      if (!(n >= 1 && n <= this.steps.length) || n === this.current) return
      if (n > this.current) {
        this.jumpTo = n
        this.paused = false
        this.go()
      } else {
        writeFileSync(jumpFile, String(n))
        say(`Going back to scene ${n}: the database is reset and the scenes before it replay.`)
        setTimeout(() => this.quit(jumpExitCode), 300)
      }
    }
    this.publish()
  },

  go() {
    if (this.release) {
      const r = this.release
      this.release = null
      r()
    }
  },

  async next(prompt) {
    await this.hold()
    if (this.auto && !this.paused) return this.publish()
    this.waiting = prompt
    this.publish()
    await new Promise((resolve) => (this.release = resolve))
    this.waiting = null
    await this.hold()
    this.publish()
  },

  async hold() {
    while (this.paused) await new Promise((r) => setTimeout(r, 200))
    if (this.opening) await this.opening
    if (!this.scratch) return
    const s = this.scratch
    this.scratch = null
    await s.close().catch(() => {})
    await this.page?.bringToFront().catch(() => {})
  },

  // The app may keep its tokens in sessionStorage, which a new tab does not share, so the scratch tab gets a copy.
  openScratch() {
    if (!this.page || this.scratch || this.opening) return
    this.opening = (async () => {
      const url = this.page.url()
      const store = await this.page.evaluate(() => JSON.stringify(Object.entries(sessionStorage))).catch(() => '[]')
      this.scratch = await this.page.context().newPage()
      await this.scratch.addInitScript(
        ([origin, entries]) => {
          if (location.origin === origin && sessionStorage.length === 0)
            for (const [k, v] of JSON.parse(entries)) sessionStorage.setItem(k, v)
        },
        [new URL(url).origin, store],
      )
      await this.scratch.goto(url).catch(() => {})
      await this.scratch.evaluate(() => (window.name = 'presenter-scratch')).catch(() => {})
      await this.scratch.bringToFront().catch(() => {})
    })()
      .catch(() => {})
      .finally(() => (this.opening = null))
  },

  async end() {
    this.current = this.steps.length
    await this.next('Next ends the demo')
    this.finished = true
    this.publish()
    say('The demo is over. The browser stays open; q quits.')
    await new Promise(() => {})
  },

  quit(code = 0) {
    if (process.stdin.isTTY) process.stdin.setRawMode(false)
    this.closeBrowser?.().catch(() => {})
    setTimeout(() => process.exit(code), 200)
  },
}

function openConsole(url) {
  try {
    const js =
      'ObjC.import("AppKit"); const f = $.NSScreen.screens.objectAtIndex(0).frame; f.size.width + " " + f.size.height'
    const [w, h] = execFileSync('osascript', ['-l', 'JavaScript', '-e', js], { encoding: 'utf8' })
      .trim()
      .split(' ')
      .map(Number)
    const script = [
      'tell application "Google Chrome"',
      '  set cw to make new window',
      `  set URL of active tab of cw to "${url}"`,
      `  set bounds of cw to {0, 25, ${Math.round(w)}, ${Math.round(h)}}`,
      '  activate',
      'end tell',
    ].join('\n')
    execFileSync('osascript', ['-e', script], { stdio: 'ignore' })
  } catch {
    spawn('open', [url], { stdio: 'ignore', detached: true }).unref()
  }
}

const consolePage = `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Presenter console</title>
<style>
:root{--bg:#f7f7f5;--fg:#1d1d1b;--muted:#6b6b66;--card:#fff;--line:#e3e2dd;--accent:#2563eb;--warn:#b45309;--bad:#b91c1c}
@media (prefers-color-scheme: dark){:root{--bg:#161615;--fg:#ededea;--muted:#a3a39d;--card:#21211f;--line:#34342f;--accent:#60a5fa;--warn:#fbbf24;--bad:#f87171}}
*{box-sizing:border-box}body{margin:0;font:16px/1.5 -apple-system,system-ui,sans-serif;background:var(--bg);color:var(--fg);display:grid;grid-template-columns:minmax(0,1fr) 340px;height:100vh}
main{padding:28px 36px;overflow:auto}aside{border-left:1px solid var(--line);overflow:auto;padding:16px}
.kicker{color:var(--muted);font-size:14px;letter-spacing:.04em;text-transform:uppercase}h1{font-size:30px;margin:4px 0 16px}
.say{font-size:13px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:var(--accent);margin-bottom:6px}
.notes{font-size:24px;line-height:1.5;background:var(--card);border:1px solid var(--line);border-radius:12px;padding:20px 24px}
.status{margin:18px 0;font-weight:600}.status.wait{color:var(--accent)}.status.pause{color:var(--warn)}.status.bad{color:var(--bad)}
.bar{display:flex;gap:10px;flex-wrap:wrap}button{font:inherit;font-weight:600;border:1px solid var(--line);background:var(--card);color:var(--fg);border-radius:10px;padding:12px 18px;cursor:pointer}
button.primary{background:var(--accent);border-color:var(--accent);color:#fff;font-size:20px;padding:14px 28px}button.on{outline:2px solid var(--accent)}
.keys,.next{color:var(--muted);font-size:13px;margin-top:10px}
ol{margin:0;padding-left:28px}li{padding:8px 6px;border-radius:8px;color:var(--muted);cursor:pointer}li:hover{background:var(--card)}li.done{opacity:.55}li.now{background:var(--card);color:var(--fg);border:1px solid var(--accent)}li b{display:block;color:var(--fg)}li span{font-size:13px}
</style></head><body>
<main><div class="kicker" id="kicker">Starting</div><h1 id="title">Getting ready</h1>
<div class="say">Say this</div><div class="notes" id="notes">The browser opens on the other screen.</div>
<div class="status" id="status"></div>
<div class="bar"><button class="primary" id="next">Next ▶</button><button id="pause">Pause</button><button id="auto">Auto</button><button id="quit">Quit</button></div>
<div class="keys">Keys: Enter or → next · P pause · A auto · click a scene on the right to jump to it</div>
<div class="next" id="upnext"></div></main>
<aside><ol id="list"></ol></aside>
<script>
var steps=[],s={},run=null;
function post(a){fetch('/act/'+a,{method:'POST'})}
['next','pause','auto'].forEach(function(a){document.getElementById(a).onclick=function(){post(a)}});
document.getElementById('quit').onclick=function(){if(confirm('Quit the demo and close its browser?'))post('quit')};
document.addEventListener('keydown',function(e){if(e.metaKey||e.ctrlKey)return;var m={enter:'next',' ':'next',arrowright:'next',p:'pause',a:'auto'}[e.key.toLowerCase()];if(m){e.preventDefault();post(m)}});
function render(){var i=s.current||0,st=steps[i-1];
document.getElementById('kicker').textContent=i?('Scene '+i+' of '+steps.length):'Starting';
document.getElementById('title').textContent=s.finished?'The demo is over':(st?st.title:'Getting ready');
document.getElementById('notes').textContent=st?st.notes:'The browser opens on the other screen.';
var el=document.getElementById('status');el.className='status';
if(s.failure){el.className+=' bad';el.textContent='A step failed: '+s.failure+' The browser stays where it stopped; carry on by hand.'}
else if(s.paused){el.className+=' pause';el.textContent='Paused: a scratch tab is open on the other screen, signed in as the same person. Next or P closes it and the demo carries on.'}
else if(s.jumpTo){el.className+=' wait';el.textContent='Fast-forwarding to scene '+s.jumpTo+'…'}
else if(s.waiting){el.className+=' wait';el.textContent=s.waiting+'.'}
else el.textContent=s.finished?'Quit closes the browser.':(i?'Running on the other screen…':'Starting…');
var nx=steps[i];document.getElementById('upnext').textContent=nx&&!s.finished?('Up next: '+nx.title):'';
document.getElementById('pause').textContent=s.paused?'Resume':'Pause';document.getElementById('auto').className=s.auto?'on':'';
var list=document.getElementById('list');list.innerHTML='';steps.forEach(function(x,j){var li=document.createElement('li');li.className=j+1<i?'done':(j+1===i?'now':'');
li.onclick=function(){var n=j+1;if(n===i)return;if(confirm(n>i?('Fast-forward to scene '+n+'?'):('Go back to scene '+n+'? The database is reset and the scenes before it replay.')))post('jump/'+n)};
var b=document.createElement('b');b.textContent=x.title;var sp=document.createElement('span');sp.textContent=x.notes;li.appendChild(b);li.appendChild(sp);list.appendChild(li)})}
function load(){fetch('/steps').then(function(r){return r.json()}).then(function(x){steps=x;render()})}
load();var es=new EventSource('/events');es.onmessage=function(e){s=JSON.parse(e.data);if(run&&s.run!==run){location.reload();return}run=s.run;if(steps.length!==s.total)load();else render()};
</script></body></html>`
