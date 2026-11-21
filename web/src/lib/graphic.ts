import { createElement, type ReactNode } from 'react'

// What the assistant may draw with: shapes and text. Anything else in its SVG is dropped before it reaches the page.
const ELEMENTS = new Set([
  'svg',
  'g',
  'path',
  'rect',
  'circle',
  'ellipse',
  'line',
  'polyline',
  'polygon',
  'text',
  'tspan',
  'title',
  'desc',
])
const ATTRIBUTES = new Set([
  'viewBox',
  'x',
  'y',
  'x1',
  'y1',
  'x2',
  'y2',
  'cx',
  'cy',
  'r',
  'rx',
  'ry',
  'dx',
  'dy',
  'width',
  'height',
  'd',
  'points',
  'transform',
  'fill',
  'fill-opacity',
  'stroke',
  'stroke-width',
  'stroke-linecap',
  'stroke-linejoin',
  'stroke-dasharray',
  'stroke-opacity',
  'opacity',
  'font-size',
  'font-weight',
  'text-anchor',
  'dominant-baseline',
])
// The palette, as the prompt gives it; a colour outside it is replaced by the ink, so a drawing cannot drift off the design.
const PAINTS =
  /^(none|currentColor|var\(--(chart-[123]|accent|ink|ink-secondary|ink-tertiary|line|line-strong|surface)\))$/
const MAX_NODES = 800
export const MAX_SVG_LENGTH = 30_000

const camel = (name: string) => name.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase())

// Reads the assistant's SVG into React elements, keeping only the shapes, text and attributes above. Null when it is not a drawing.
export function readSvg(source: string): ReactNode | null {
  if (source.length > MAX_SVG_LENGTH) return null
  const parsed = new DOMParser().parseFromString(source, 'image/svg+xml')
  const root = parsed.documentElement
  if (root.nodeName !== 'svg' || parsed.getElementsByTagName('parsererror').length > 0) return null
  if (!root.getAttribute('viewBox')) return null

  let nodes = 0
  const build = (element: Element, key: number): ReactNode => {
    if (!ELEMENTS.has(element.nodeName) || ++nodes > MAX_NODES) return null
    const props: Record<string, string | number> = { key }
    for (const { name, value } of Array.from(element.attributes)) {
      if (!ATTRIBUTES.has(name) || /[<>]|url\(|javascript:/i.test(value)) continue
      if (name === 'fill' || name === 'stroke') props[name] = PAINTS.test(value.trim()) ? value.trim() : 'var(--ink)'
      else props[camel(name)] = value
    }
    if (element.nodeName === 'svg') {
      delete props.width
      delete props.height
      props.role = 'img'
      props.className = 'h-auto w-full max-w-full'
    }
    const children = Array.from(element.childNodes).map((child, index) =>
      child.nodeType === Node.TEXT_NODE
        ? child.textContent
        : child.nodeType === Node.ELEMENT_NODE
          ? build(child as Element, index)
          : null,
    )
    return createElement(element.nodeName, props, ...children)
  }
  return build(root, 0)
}
