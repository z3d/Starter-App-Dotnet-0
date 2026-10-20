// Narration for a walkthrough recording, the way gumnut's is told: each scene has one line, spoken
// by an Australian neural voice through edge-tts (Microsoft's Edge read-aloud service; `pip install
// edge-tts`, it needs the network), synthesised before the browser opens so every scene is held at
// least as long as its line, then mixed onto the video at the moment each scene began. NARRATOR
// picks the voice (en-AU-NatashaNeural by default, en-AU-WilliamMultilingualNeural is the other
// Australian one); NARRATOR=none records silently.
import { execSync } from 'node:child_process'
import { existsSync, mkdtempSync, readdirSync, renameSync, rmSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'

// Synthesises one clip per line; { key: { file, ms } }, or {} when the voice is off or unavailable.
export function synthesise(narration, name) {
  const narrator = process.env.NARRATOR ?? 'en-AU-NatashaNeural'
  const clips = {}
  let work = null
  if (narrator === 'none') return { clips, work }
  try {
    execSync('python3 -m edge_tts --help', { stdio: 'ignore' })
    execSync('ffprobe -version', { stdio: 'ignore' })
    work = mkdtempSync(`${tmpdir()}/${name}-narration-`)
    for (const [key, text] of Object.entries(narration)) {
      const file = `${work}/${key}.mp3`
      execSync(`python3 -m edge_tts --voice ${narrator} --write-media "${file}" --text ${JSON.stringify(text)}`, {
        stdio: 'ignore',
      })
      const seconds = Number(
        execSync(`ffprobe -v error -show_entries format=duration -of csv=p=0 "${file}"`).toString(),
      )
      clips[key] = { file, ms: Math.round(seconds * 1000) }
    }
  } catch (error) {
    console.error(`Recording without narration: ${error.message.split('\n')[0]}`)
    work = null
  }
  return { clips, work }
}

// Cueing: a scene begins when its line starts, and never before the line ahead of it has been
// heard, so two voices cannot overlap however the scenes are placed on a page.
export function cueing(page, clips) {
  const started = Date.now()
  const cues = []
  let heardBy = 0
  let current = 'start'
  const settle = () => page.waitForTimeout(Math.max(0, heardBy - Date.now()))
  const scene = async (key) => {
    await settle()
    current = key
    cues.push({ key, at: Date.now() - started })
    heardBy = Date.now() + (clips[key]?.ms ?? 0) + 700
  }
  return { started, cues, scene, settle, current: () => current }
}

// Names the recording, mixes the lines onto it at their cues and converts to mp4 (ffmpeg on the
// path; without it the silent webm stays). Returns the file written.
export function finish({ out, name, started, cues, clips, work }) {
  // Playwright names the recording page@<hash>.webm; ignore an earlier run's output (including a
  // zero-byte file left by a crash) so a stale file can never be converted in place of the new one.
  const target = `${out}/${name}-walkthrough.webm`
  const webm = readdirSync(out)
    .filter((f) => f.startsWith('page@') && f.endsWith('.webm') && statSync(`${out}/${f}`).size > 0)
    .sort((a, b) => statSync(`${out}/${a}`).mtimeMs - statSync(`${out}/${b}`).mtimeMs)
    .pop()
  if (!webm) throw new Error(`No recording found in ${out}`)
  renameSync(`${out}/${webm}`, target)
  // What was said when, for a take that reads wrong: the cue times are the moments the lines start.
  console.error(
    `narration: ${Object.keys(clips).length} lines, ${cues.length} cues over ${Math.round((Date.now() - started) / 1000)} s: ${cues.map((c) => `${c.key}@${Math.round(c.at / 1000)}s`).join(' ')}`,
  )
  let final = target
  try {
    execSync('ffmpeg -version', { stdio: 'ignore' })
    final = `${out}/${name}-walkthrough.mp4`
    const spoken = cues.filter((cue) => clips[cue.key])
    if (spoken.length === 0) {
      execSync(`ffmpeg -y -loglevel error -i "${target}" -c:v libx264 -pix_fmt yuv420p -movflags +faststart "${final}"`)
    } else {
      // Playwright's clock and the recording's can drift by a few percent; place each line by its
      // share of the run so the last scene lands where it was seen.
      const seconds = Number(
        execSync(`ffprobe -v error -show_entries format=duration -of csv=p=0 "${target}"`).toString(),
      )
      const scale = (seconds * 1000) / (Date.now() - started)
      // The screencast paints about half a second behind the page, so the voice waits that long too.
      const lag = 500
      // The browser starting and the sign-in page loading are silent; the take begins a moment before the first line.
      const trim = Math.max(0, Math.round(spoken[0].at * scale) + lag - 800)
      const inputs = spoken.map((cue) => `-i "${clips[cue.key].file}"`).join(' ')
      const delays = spoken
        .map((cue, i) => `[${i + 1}]adelay=${Math.round(cue.at * scale) + lag - trim}:all=1[n${i}]`)
        .join(';')
      const mix = spoken.map((_, i) => `[n${i}]`).join('')
      const filter = `[0:v]trim=start=${trim / 1000},setpts=PTS-STARTPTS[picture];${delays};${mix}amix=inputs=${spoken.length}:normalize=0,apad[voice]`
      execSync(
        `ffmpeg -y -loglevel error -i "${target}" ${inputs} -filter_complex "${filter}" -map "[picture]" -map "[voice]" -c:v libx264 -pix_fmt yuv420p -c:a aac -b:a 128k -shortest -movflags +faststart "${final}"`,
      )
    }
    rmSync(target, { force: true })
  } catch {
    // no ffmpeg: the webm stays
  }
  if (work) rmSync(work, { recursive: true, force: true })
  return existsSync(final) ? final : target
}

// Clears a crashed run's page@<hash>.webm so only this run's recording is a candidate.
export function clearStale(out) {
  for (const stale of readdirSync(out).filter((f) => f.startsWith('page@') && f.endsWith('.webm')))
    rmSync(`${out}/${stale}`, { force: true })
}
