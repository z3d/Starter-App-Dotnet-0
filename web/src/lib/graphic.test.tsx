import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { readSvg } from './graphic'

const draw = (svg: string) => render(<div>{readSvg(svg)}</div>).container

describe('readSvg', () => {
  it('keeps shapes and text in the palette, sized to the page', () => {
    const page = draw(
      '<svg viewBox="0 0 640 200" width="640" height="200"><rect x="10" y="20" width="300" height="20" rx="4" fill="var(--chart-1)"/><text x="320" y="35" font-size="13" fill="var(--ink)">$1,200</text></svg>',
    )

    const svg = page.querySelector('svg')!
    expect(svg.getAttribute('viewBox')).toBe('0 0 640 200')
    expect(svg.getAttribute('width')).toBeNull()
    expect(svg.getAttribute('role')).toBe('img')
    expect(page.querySelector('rect')!.getAttribute('fill')).toBe('var(--chart-1)')
    expect(page.querySelector('text')!.textContent).toBe('$1,200')
  })

  it('drops scripts, links, images, styles, handlers and foreign content, and paints an off-palette colour in ink', () => {
    const page = draw(
      `<svg viewBox="0 0 10 10" onload="alert(1)"><script>alert(1)</script><style>rect{fill:red}</style>
        <a href="https://example.com"><rect width="5" height="5" fill="#ff0000" onclick="alert(2)" style="fill:red"/></a>
        <image href="https://example.com/x.png"/><foreignObject><div>hi</div></foreignObject>
        <rect width="2" height="2" fill="url(#g)" stroke="javascript:alert(3)"/>
        <circle cx="5" cy="5" r="2" fill="var(--accent)"/></svg>`,
    )

    expect(page.querySelector('script, style, a, image, foreignObject, svg div')).toBeNull()
    expect(page.querySelector('svg')!.getAttribute('onload')).toBeNull()
    const circle = page.querySelector('circle')!
    expect(circle.getAttribute('fill')).toBe('var(--accent)')
    for (const rect of page.querySelectorAll('rect')) {
      expect(rect.getAttribute('onclick')).toBeNull()
      expect(rect.getAttribute('style')).toBeNull()
      expect(['var(--ink)', null]).toContain(rect.getAttribute('fill'))
      expect(rect.getAttribute('stroke')).toBeNull()
    }
  })

  it('is nothing for text that is not one well-formed drawing with a viewBox', () => {
    expect(readSvg('<div>not a drawing</div>')).toBeNull()
    expect(readSvg('<svg><rect/></svg>')).toBeNull()
    expect(readSvg('<svg viewBox="0 0 1 1"><rect></svg>')).toBeNull()
    expect(readSvg(`<svg viewBox="0 0 1 1">${'<g/>'.repeat(10_000)}</svg>`)).toBeNull()
  })
})
