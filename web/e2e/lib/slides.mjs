// Full-screen slides a walkthrough lays over the page between scenes: a title, and rows, steps, points, a note or a
// person's portrait card. Colours and fonts come from a tone (below), never from the stylesheet.
const fonts = 'https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600&display=swap'

const css = `
#wt-slide { position: fixed; inset: 0; z-index: 2147483647; display: grid; place-items: center; padding: 64px;
  background: radial-gradient(1200px 700px at 85% 0%, color-mix(in oklab, var(--wt-accent) 22%, transparent), transparent 60%),
    radial-gradient(900px 600px at 0% 100%, var(--wt-glow), transparent 65%),
    linear-gradient(160deg, var(--wt-bg-1), var(--wt-bg-2) 60%, var(--wt-bg-3));
  opacity: 0; transition: opacity 450ms ease; font-family: var(--wt-font); color: #ffffff; }
#wt-slide::before { content: ''; position: absolute; inset: 0; pointer-events: none;
  background-image: linear-gradient(#ffffff08 1px, transparent 1px), linear-gradient(90deg, #ffffff08 1px, transparent 1px);
  background-size: 48px 48px; }
#wt-slide::after { content: var(--wt-footer); position: absolute; left: 64px; bottom: 40px; font: 500 12px var(--wt-font);
  letter-spacing: 0.2em; text-transform: uppercase; color: var(--wt-muted); }
#wt-slide.shown { opacity: 1; }
#wt-slide .wt-card { position: relative; width: min(1040px, 100%); transform: translateY(10px); transition: transform 450ms ease; }
#wt-slide.shown .wt-card { transform: none; }
#wt-slide .kicker { display: inline-block; font: 500 13px var(--wt-font); letter-spacing: 0.16em;
  text-transform: uppercase; color: var(--wt-on-accent); background: var(--wt-accent); padding: 6px 10px; border-radius: 4px; }
#wt-slide h1 { font-family: var(--wt-font); font-weight: 600; font-size: 54px; line-height: 1.05;
  margin: 20px 0 34px; letter-spacing: -0.02em; color: #ffffff; }
#wt-slide .rows { display: grid; border-top: 2px solid var(--wt-accent); }
#wt-slide .row { display: grid; grid-template-columns: 1fr auto; gap: 24px; padding: 16px 0;
  border-bottom: 1px solid #ffffff1f; align-items: baseline; }
#wt-slide .row .label { font-size: 21px; font-weight: 500; }
#wt-slide .row .hint { display: block; font-size: 15px; font-weight: 400; color: var(--wt-muted); margin-top: 4px; }
#wt-slide .row .value { font-family: var(--wt-font); font-size: 26px; color: var(--wt-accent); }
#wt-slide .steps { display: grid; grid-auto-flow: column; grid-auto-columns: 1fr; gap: 14px; }
#wt-slide .step { border-top: 3px solid var(--wt-accent); background: #ffffff0d; border-radius: 0 0 10px 10px; padding: 18px 18px 22px; }
#wt-slide .step .n { font: 500 13px var(--wt-font); color: var(--wt-accent); }
#wt-slide .step .name { font-family: var(--wt-font); font-weight: 600; font-size: 24px; margin: 6px 0 8px; color: #fff; }
#wt-slide .step .detail { font-size: 15px; line-height: 1.45; color: var(--wt-muted); }
#wt-slide ul { list-style: none; padding: 0; margin: 0; display: grid; gap: 16px; }
#wt-slide li { display: grid; grid-template-columns: 34px 1fr; font-size: 22px; line-height: 1.4; }
#wt-slide li::before { content: '→'; font-family: var(--wt-font); color: var(--wt-accent); }
#wt-slide .note { margin-top: 30px; font-size: 16px; line-height: 1.45; color: #ffffff;
  padding: 12px 16px; border-left: 3px solid var(--wt-accent); background: #ffffff0d; }
#wt-slide .wt-person { display: grid; grid-template-columns: 320px minmax(0, 1fr); gap: 56px; align-items: center; }
#wt-slide .wt-person figure { margin: 0; }
#wt-slide .wt-person img { width: 320px; height: 420px; object-fit: cover; border-radius: 16px; }
#wt-slide .wt-person figcaption { margin-top: 12px; color: var(--wt-muted); font-size: 13px; line-height: 1.5; }
#wt-slide .wt-person .row { grid-template-columns: 1fr; gap: 6px; }
#wt-slide .wt-person .value { font-family: var(--wt-font); font-size: 22px; }
`

// A tone is everything about a slide that belongs to a brand: its background, accent, the text on the accent, the
// muted text, the font and the footer label. A product sets its own here, in one place; a slide names its tone
// (`tone: 'ai'`), so a product with two brands gives each its own (one for staff, one for the people they serve) and
// a portrait card picks the tone of the person shown.
const base = {
  bg: ['#1c1714', '#241c17', '#1a1512'],
  glow: '#b4532a40',
  muted: '#b9aa9c',
  onAccent: '#1c1714',
  font: "'Space Grotesk', system-ui, sans-serif",
  footer: "'Walkthrough'",
}

export const tones = {
  plain: { ...base, accent: '#e3b25a' },
  ai: { ...base, accent: '#c4b5fd' },
}

// The tone as the CSS variables the slide reads (--wt-accent, --wt-bg-1, ...).
export const toneVariables = (t) => ({
  accent: t.accent,
  'on-accent': t.onAccent,
  glow: t.glow,
  muted: t.muted,
  font: t.font,
  footer: t.footer,
  'bg-1': t.bg[0],
  'bg-2': t.bg[1],
  'bg-3': t.bg[2],
})

const escape = (text) =>
  String(text).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c])

export const render = ({ kicker, title, rows, steps, points, note, portrait }) => {
  const body = [
    rows &&
      `<div class="rows">${rows
        .map(
          ([label, value, hint]) =>
            `<div class="row"><span class="label">${escape(label)}${hint ? `<span class="hint">${escape(hint)}</span>` : ''}</span><span class="value">${escape(value)}</span></div>`,
        )
        .join('')}</div>`,
    steps &&
      `<div class="steps">${steps
        .map(
          ([name, detail], i) =>
            `<div class="step"><div class="n">${String(i + 1).padStart(2, '0')}</div><div class="name">${escape(name)}</div><div class="detail">${escape(detail)}</div></div>`,
        )
        .join('')}</div>`,
    points && `<ul>${points.map((p) => `<li>${escape(p)}</li>`).join('')}</ul>`,
    note && `<div class="note">${escape(note)}</div>`,
  ]
    .filter(Boolean)
    .join('')
  const content = `<div><div class="kicker">${escape(kicker)}</div><h1>${escape(title)}</h1>${body}</div>`
  return portrait
    ? `<div class="wt-card wt-person"><figure><img src="${escape(portrait.src)}" alt="${escape(portrait.alt)}" style="object-position: ${escape(portrait.position ?? 'center')}"><figcaption>${escape(portrait.credit)}</figcaption></figure>${content}</div>`
    : `<div class="wt-card">${content}</div>`
}

// Lays the slide over the page and waits for it to fade in.
export async function showSlide(page, slide) {
  await page.evaluate(
    ({ css, fonts, html, tone }) => {
      document.getElementById('wt-slide')?.remove()
      if (!document.getElementById('wt-slide-fonts')) {
        const link = Object.assign(document.createElement('link'), {
          id: 'wt-slide-fonts',
          rel: 'stylesheet',
          href: fonts,
        })
        document.head.append(link)
      }
      const style = Object.assign(document.createElement('style'), { textContent: css })
      const overlay = Object.assign(document.createElement('div'), { id: 'wt-slide', innerHTML: html })
      for (const [name, value] of Object.entries(tone)) overlay.style.setProperty(`--wt-${name}`, value)
      overlay.prepend(style)
      document.body.append(overlay)
      requestAnimationFrame(() => requestAnimationFrame(() => overlay.classList.add('shown')))
    },
    { css, fonts, html: render(slide), tone: toneVariables(tones[slide.tone ?? 'plain'] ?? tones.plain) },
  )
  await page.evaluate(() => document.fonts.ready)
  await page.locator('#wt-slide img').evaluateAll((images) => Promise.all(images.map((image) => image.decode())))
  await page.waitForTimeout(500)
}

// Fades the slide out and removes it.
export async function hideSlide(page) {
  await page.evaluate(() => document.getElementById('wt-slide')?.classList.remove('shown'))
  await page.waitForTimeout(500)
  await page.evaluate(() => document.getElementById('wt-slide')?.remove())
}
