// Any page in the presenter's browser can POST to 127.0.0.1, and a rebound DNS name looks same-origin.
export function refusal({ method, headers }, port) {
  const own = [`localhost:${port}`, `127.0.0.1:${port}`]
  if (!own.includes(headers.host ?? '')) return 'unknown host'
  if (method !== 'POST') return null
  const origin = headers.origin
  if (origin !== undefined) return own.some((h) => origin === `http://${h}`) ? null : 'foreign origin'
  const site = headers['sec-fetch-site']
  if (site !== undefined && site !== 'same-origin') return 'cross-site request'
  const referer = headers.referer
  if (referer !== undefined && !own.some((h) => referer.startsWith(`http://${h}/`))) return 'foreign referer'
  return null
}
