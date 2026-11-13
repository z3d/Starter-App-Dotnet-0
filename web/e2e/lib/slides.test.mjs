// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { render, toneVariables, tones } from './slides.mjs'

describe('the walkthrough slides', () => {
  it('give every tone the same set of variables, so a slide never falls back to a hard-coded colour', () => {
    const names = Object.keys(toneVariables(tones.plain))

    expect(names).toEqual(['accent', 'on-accent', 'glow', 'muted', 'font', 'footer', 'bg-1', 'bg-2', 'bg-3'])
    for (const tone of Object.values(tones)) {
      const variables = toneVariables(tone)
      expect(Object.keys(variables)).toEqual(names)
      expect(Object.values(variables).every((value) => typeof value === 'string' && value.length > 0)).toBe(true)
    }
  })

  it('escape what a story writes, and lay a portrait beside the text', () => {
    const plain = render({ kicker: 'Step 1', title: 'Orders & <refunds>', points: ['One "quoted" point'] })
    const person = render({
      kicker: 'Meet',
      title: 'Jordan',
      portrait: { src: '/people/jordan.jpg', alt: 'Jordan', credit: 'Photo credit' },
    })

    expect(plain).toContain('Orders &amp; &lt;refunds&gt;')
    expect(plain).toContain('One &quot;quoted&quot; point')
    expect(plain).not.toContain('wt-person')
    expect(person).toContain('class="wt-card wt-person"')
    expect(person).toContain('src="/people/jordan.jpg"')
  })
})
