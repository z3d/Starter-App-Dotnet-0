// The build this bundle came from: the commit the image was built at, or "dev" from a local server.
export const build: string = import.meta.env.VITE_BUILD || 'dev'
