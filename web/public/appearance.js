// A file rather than an inline script: the image's CSP allows scripts from 'self' only.
try {
  var root = document.documentElement
  var mode = window.localStorage.getItem('appearance.mode')
  if (mode === 'light' || mode === 'dark') root.setAttribute('data-theme', mode)
  var palette = window.localStorage.getItem('appearance.palette')
  if (palette && /^[a-z][a-z0-9-]{0,31}$/.test(palette)) root.setAttribute('data-palette', palette)
} catch (e) {
  // Storage blocked: the app applies the tenant's theme when it loads.
}
