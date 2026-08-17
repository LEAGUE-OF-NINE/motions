import { AnimationSpec, compareNatural, defaultSpec, parseSpec, stripJsoncExtras } from './spec'
import { Skill, parseSkill } from './skill'
import { MOTION_NAMES } from './motions'
import { pngRejection, readPngHeader } from './png'

export type Mode = 'appearance' | 'override'

export interface LoadedAsset {
  name: string
  /** Object URL for <img src>. Revoked when the folder is closed. */
  url: string
  width: number
  height: number
  /** Non-null means the game cannot decode this file. */
  rejection: string | null
}

export interface LoadedSound {
  /** Object URL for `new Audio(url)`. Revoked when the folder is closed. */
  url: string
  /** How long it runs, or 0 when the browser could not tell. Drawn as its bar on the timeline. */
  seconds: number
}

export interface LoadedMotion {
  folder: string
  handle: FileSystemDirectoryHandle
  spec: AnimationSpec
  /** False means the spec was reconstructed from the zero-config default. */
  hadJson: boolean
  assets: Map<string, LoadedAsset>
  /** File name -> the loaded sound, for the preview to play. Revoked with the assets above. */
  sounds: Map<string, LoadedSound>
  /** Why animation.json was rejected, if it was. The plugin falls back to the bundle here. */
  error: string | null
}

/** One skill file (S1.json and friends), which sits at the character folder, not in a motion. */
export interface LoadedSkill {
  name: string
  /** null when the file could not be parsed. The editor shows the error rather than the file. */
  skill: Skill | null
  error: string | null
  /**
   * The bytes as read. Kept so a file the editor cannot parse is never rewritten from a guess:
   * without it, "open, fail to parse, save" would replace someone's file with an empty one.
   */
  text: string
  /** The hitCheckers advice for this file, or null when every coin already has one. */
  warning: string | null
}

export interface LoadedCharacter {
  handle: FileSystemDirectoryHandle
  name: string
  mode: Mode
  motions: LoadedMotion[]
  skills: LoadedSkill[]
  appearanceBase: string
  hadAppearanceJson: boolean
  /**
   * False when an appearance.json exists but could not be read. The donor shown is then a
   * fallback, not the author's, so writing it back would replace their choice with a guess.
   */
  appearanceReadable: boolean
}

/** The plugin's fallback donor when appearance.json is missing (AppearanceRegistry.DefaultBase). */
export const DEFAULT_BASE = '10101_YiSang_BaseAppearance'

export function pickFolder(): Promise<FileSystemDirectoryHandle> {
  return window.showDirectoryPicker({ mode: 'readwrite' })
}

/** The two folders the plugin walks down from a mod root (Motions.cs:50,77), and what each means. */
const ROOTS = new Map<string, Mode>([
  ['motion_appearances', 'appearance'],
  ['custom_motions', 'override'],
])

export interface Candidate {
  handle: FileSystemDirectoryHandle
  /**
   * null means the kind could not be worked out and someone has to say: the folder was picked on
   * its own, so there is no motion_appearances/ or custom_motions/ above it to read it off, and no
   * appearance.json inside it either. Only ever null for a single directly-picked folder.
   */
  mode: Mode | null
  /** Shown in the picker when a mod holds more than one character. */
  path: string
}

/** A candidate the path already settled the kind of: the only kind that can be opened directly. */
export type Known = Candidate & { mode: Mode }

/**
 * Only a single directly-picked folder can have an unsettled kind, so anything found by descending
 * is already known. Filtering says that in a way the types carry, rather than asserting it.
 */
export function known(candidates: Candidate[]): Known[] {
  return candidates.filter((c): c is Known => c.mode !== null)
}

/**
 * The three names the plugin refuses to treat as characters under custom_motions: they hold
 * bundles for the dashboard, the screen border and buff effects (Motions.cs:82,97,113). Substring
 * matching, not equality, because that is how the plugin tests them.
 */
const RESERVED = ['DASHBOARD', 'CUSTOMSCREEN', 'MOTIONBUFF_']

/**
 * Only under custom_motions. The plugin's checks for these live inside that loop alone
 * (Motions.cs:82,97,113); the motion_appearances loop above them has none, so a folder called
 * MOTIONBUFF_Guy there is an ordinary appearance to the game and used to be invisible here.
 */
function reserved(name: string, mode: Mode): boolean {
  return mode === 'override' && RESERVED.some((r) => name.includes(r))
}

/**
 * Skill files the plugin will actually read. It walks the MOTION_DETAIL enum and looks for
 * "<name>.json" (Motions.cs:189-195), so a CharacterVFX.json - which it loads separately - or any
 * other stray JSON is not a skill file. Showing those as skill tabs meant a perfectly valid
 * CharacterVFX.json appeared under a red "could not be read" banner.
 */
function isSkillFile(name: string): boolean {
  return MOTION_NAMES.some((motion) => `${motion}.json` === name)
}

/**
 * Whether a folder picked on its own is plausibly a character rather than a mod root or some
 * unrelated directory. A character does not need motions/: RegisterCharacterFolder loads bundles
 * and CharacterVFX JSON from the same folder (Motions.cs:158), so a bundle-driven character is a
 * folder of .bundle and skill .json files and nothing else - and it is still worth opening, both
 * to read its S1.json hitCheckers and to add sprite motions to it.
 */
async function looksLikeCharacter(dir: FileSystemDirectoryHandle): Promise<boolean> {
  for await (const entry of dir.values()) {
    if (entry.kind === 'directory' && entry.name === 'motions') return true
    if (entry.kind === 'file' && /\.(bundle|json)$/i.test(entry.name)) return true
  }
  return false
}

/**
 * The kind of a folder picked on its own, where there is no path to read it off. appearance.json
 * is only ever read under motion_appearances (AppearanceRegistry.ReadBase, called at
 * Motions.cs:68), so a folder that has one is an appearance. Nothing distinguishes the other
 * direction - an override folder and an appearance folder that has not been given a donor yet look
 * identical - so that returns null rather than guessing, and someone is asked.
 */
async function modeOfPickedFolder(dir: FileSystemDirectoryHandle): Promise<Mode | null> {
  for await (const entry of dir.values()) {
    if (entry.kind === 'file' && entry.name === 'appearance.json') return 'appearance'
  }
  return null
}

/**
 * Finds the character folders under whatever the author picked. The mod folder is what they have
 * open in their file manager, so it is what they pick - one or two levels above the character
 * folder. Requiring the leaf made a correct mod folder open as an empty editor with nothing on
 * screen to say why.
 *
 * Below one of the two roots, EVERY folder is a character bar the reserved names - the same rule
 * the plugin applies, and deliberately not "has a motions/ folder". A bundle character has no
 * sprite motions yet; refusing to open it is refusing the folder you would add the first one to.
 *
 * A character found this way takes its mode from the root it was found under. The path already
 * says which kind it is, and asking the author to classify a folder the plugin classifies by
 * position is asking them to do arithmetic the program can do.
 */
export async function findCharacters(root: FileSystemDirectoryHandle): Promise<Candidate[]> {
  const found: Candidate[] = []

  async function scan(parent: FileSystemDirectoryHandle, mode: Mode, prefix: string): Promise<void> {
    for await (const entry of parent.values()) {
      if (entry.kind !== 'directory' || reserved(entry.name, mode)) continue
      found.push({ handle: entry as FileSystemDirectoryHandle, mode, path: prefix + entry.name })
    }
  }

  const own = ROOTS.get(root.name)
  if (own) {
    await scan(root, own, '')
  } else {
    for await (const entry of root.values()) {
      if (entry.kind !== 'directory') continue
      const mode = ROOTS.get(entry.name)
      if (mode) await scan(entry as FileSystemDirectoryHandle, mode, `${entry.name}/`)
    }
  }

  // Neither root is here, so this is either the character folder itself or a folder that holds no
  // character at all. The mods folder is excluded explicitly: a stray .json loose in it would
  // otherwise satisfy looksLikeCharacter and let it open as a character it cannot be.
  if (found.length === 0 && await looksLikeCharacter(root) && !(await isModsRoot(root))) {
    return [{ handle: root, mode: await modeOfPickedFolder(root), path: root.name }]
  }

  found.sort((a, b) => compareNatural(a.path, b.path))
  return found
}

/**
 * Lethe truncates any appearance ID containing "Appearance" at that substring
 * (Lethe/Patches/Skin.cs:243), so a folder called MyAppearance_v2 silently registers as
 * !motions_MyAppearance. Caught here, where there is someone to tell.
 */
export function nameRejection(name: string, mode: Mode): string | null {
  if (mode !== 'appearance') return null
  if (name.includes('Appearance')) {
    return `"${name}" contains "Appearance", which Lethe truncates the ID at. ` +
           `It would register as "!motions_${name.slice(0, name.indexOf('Appearance') + 'Appearance'.length)}". Rename the folder.`
  }
  return null
}

/**
 * Whether this is the mods folder itself rather than one mod: the folder the plugin enumerates
 * (Motions.cs:37), where every child is a separate mod. Told apart by evidence rather than by the
 * name "mods": a folder whose GRANDchildren are motion_appearances/custom_motions is one level too
 * high, whatever it is called.
 *
 * It matters because the two look similar and behave nothing alike. A character created directly
 * in here would land at mods/motion_appearances/<Name>/, which the plugin reads as a mod called
 * "motion_appearances" with no character in it, a folder that loads nothing, with no error.
 */
export async function isModsRoot(dir: FileSystemDirectoryHandle): Promise<boolean> {
  for await (const entry of dir.values()) {
    if (entry.kind !== 'directory') continue
    for await (const child of (entry as FileSystemDirectoryHandle).values()) {
      if (child.kind === 'directory' && ROOTS.has(child.name)) return true
    }
  }
  return false
}

/**
 * Why a name cannot be a mod folder. Adds the plugin's own skip rule to the filesystem's: a mod
 * prefixed DISABLED_ or FULLDISABLED_ is skipped outright (Motions.cs:40), so a mod created under
 * that name would be complete, correct and silently never loaded.
 */
export function modFolderRejection(name: string): string | null {
  const basic = folderNameRejection(name)
  if (basic) return basic
  const prefix = ['DISABLED_', 'FULLDISABLED_'].find((p) => name.startsWith(p))
  if (prefix) return `The plugin skips any mod starting with ${prefix}: it would never load.`
  return null
}

/**
 * Why a name cannot be a folder, before anything is created with it. getDirectoryHandle would
 * reject most of these on its own, but with a DOMException that says nothing about which
 * character of the name was the problem - and a name ending in a space or a dot is worse than
 * that, because Windows accepts it and then makes the folder awkward to open or delete again.
 */
export function folderNameRejection(name: string): string | null {
  if (name.trim() === '') return 'Needs a name.'
  if (name !== name.trim()) return 'No leading or trailing spaces.'
  const bad = name.match(/[\\/:*?"<>|]/)
  if (bad) return `A folder name cannot contain ${bad[0]}`
  if (name.endsWith('.')) return 'A folder name cannot end in a dot.'
  return null
}

/**
 * Creates an empty character inside a mod folder: the root the plugin looks under, the character
 * folder itself, and the motions/ folder that makes it recognisable as one. Nothing here needs to
 * exist beforehand, so this works on a folder made in the file picker a moment ago.
 *
 * appearance.json is written for a new appearance because the value it holds is the donor the
 * plugin would have fallen back to anyway (AppearanceRegistry.DefaultBase) - so it changes nothing
 * in game, and makes the folder say what kind it is when it is opened on its own later. An
 * existing one is never overwritten: it is the author's choice of donor, not ours.
 */
export async function createCharacter(
  mod: FileSystemDirectoryHandle,
  name: string,
  mode: Mode,
): Promise<Candidate & { mode: Mode }> {
  const rootName = mode === 'appearance' ? 'motion_appearances' : 'custom_motions'
  const root = await mod.getDirectoryHandle(rootName, { create: true })
  const handle = await root.getDirectoryHandle(name, { create: true })
  await handle.getDirectoryHandle('motions', { create: true })

  if (mode === 'appearance') {
    const already = await handle.getFileHandle('appearance.json').then(() => true, () => false)
    if (!already) await writeFile(handle, 'appearance.json', JSON.stringify({ base: DEFAULT_BASE }, null, 2))
  }

  return { handle, mode, path: `${rootName}/${name}` }
}

async function readText(dir: FileSystemDirectoryHandle, name: string): Promise<string | null> {
  try {
    const fh = await dir.getFileHandle(name)
    return await (await fh.getFile()).text()
  } catch {
    return null
  }
}

/**
 * How long a sound runs, by letting the browser read its metadata - there is no header parser here
 * to match readPngHeader, and .ogg alone would need one. Answers 0 rather than rejecting on a file
 * it cannot decode, and gives up after a second: this sits in the path that opens a character, and
 * a file that fires neither event would otherwise hang the open on a blank screen. A 0 costs the
 * bar on the timeline, nothing else.
 */
function soundSeconds(url: string): Promise<number> {
  return new Promise((resolve) => {
    const audio = new Audio()
    // Infinite for a stream, NaN before metadata arrives; neither is a length to draw.
    const done = (seconds: number) => resolve(Number.isFinite(seconds) ? seconds : 0)
    audio.onloadedmetadata = () => done(audio.duration)
    audio.onerror = () => done(0)
    setTimeout(() => done(0), 1000)
    audio.src = url
  })
}

async function loadAsset(dir: FileSystemDirectoryHandle, name: string): Promise<LoadedAsset> {
  const file = await (await dir.getFileHandle(name)).getFile()
  const bytes = new Uint8Array(await file.arrayBuffer())
  const info = readPngHeader(bytes)
  return {
    name,
    // Held until the character that owns it is replaced, then revoked by revokeAssets below -
    // each import re-reads the whole character and mints a fresh URL per PNG, so without that
    // every PNG's bytes stay pinned for the tab's lifetime.
    url: URL.createObjectURL(file),
    width: info?.width ?? 0,
    height: info?.height ?? 0,
    rejection: pngRejection(info),
  }
}

async function loadMotion(handle: FileSystemDirectoryHandle, folder: string): Promise<LoadedMotion> {
  const pngs: string[] = []
  const wavs: string[] = []

  for await (const entry of handle.values()) {
    if (entry.kind !== 'file') continue
    const lower = entry.name.toLowerCase()
    if (lower.endsWith('.png')) pngs.push(entry.name)
    else if (lower.endsWith('.wav') || lower.endsWith('.ogg')) wavs.push(entry.name)
  }
  pngs.sort(compareNatural)
  wavs.sort(compareNatural)

  const assets = new Map<string, LoadedAsset>()
  for (const name of pngs) assets.set(name, await loadAsset(handle, name))

  // No header check like loadAsset does for PNGs: the browser decodes these, not the game, and a
  // file it cannot play just stays silent in the preview.
  const sounds = new Map<string, LoadedSound>()
  for (const name of wavs) {
    const url = URL.createObjectURL(await (await handle.getFileHandle(name)).getFile())
    sounds.set(name, { url, seconds: await soundSeconds(url) })
  }

  const json = await readText(handle, 'animation.json')
  let spec: AnimationSpec
  let error: string | null = null
  let hadJson = false

  if (json !== null) {
    const parsed = parseSpec(json)
    if (parsed.spec) {
      spec = parsed.spec
      hadJson = true
    } else {
      // Show the folder anyway, as the default, so the author can see and fix it.
      error = parsed.error
      spec = defaultSpec(pngs)
    }
  } else {
    spec = defaultSpec(pngs)
  }

  return { folder, handle, spec, hadJson, assets, sounds, error }
}

/**
 * hitCheckers marks where a coin may hand off, and defaults to 15% of the coin when absent - so
 * a two second animation stops after 0.3s. It is the most common cause of "my attack gets cut
 * short", and the file is sitting right there, so it is worth reading. Read-only, always.
 *
 * The empty-coins case is deliberately not a warning: with no coins at all, the plugin
 * synthesises one and, for a sprite motion, hands off at the very end on its own
 * (TimelineBuilder.cs:345-374) - the 15% default only fires once a coin exists and that coin's
 * own hitCheckers is missing or empty (TimelineBuilder.cs:74-85). hitCheckers lives on each
 * coin, not on the skill root, so a coins-less file has nothing to check here.
 */
export function checkSkillJson(text: string | null, name: string): string | null {
  if (text === null) return null
  let data: any
  try {
    // Comments and trailing commas are fine here: the plugin allows both
    // (TimelineBuilder.cs:37-42), so a file holding them is not the author's problem.
    data = JSON.parse(stripJsoncExtras(text))
  } catch {
    return `${name} is not valid JSON, so the game will ignore it.`
  }
  const coins: any[] = Array.isArray(data?.coins) ? data.coins : []
  const badIndex = coins.findIndex((c) => !Array.isArray(c?.hitCheckers) || c.hitCheckers.length === 0)

  if (badIndex !== -1) {
    // 1-based, like the coin tabs and every parse error. Reporting the raw index sent people to
    // the wrong tab.
    return `${name} coin ${badIndex + 1} has no hitCheckers. That defaults to 15% of the coin, ` +
           `which cuts the animation off early. Add "hitCheckers": [{ "time": 1.0, "isNextMotionCoinDelay": 0.0 }] ` +
           `- unlike animation.json, time here is a fraction of totalDuration, so 1.0 means the end.`
  }
  return null
}

export async function loadCharacter(
  handle: FileSystemDirectoryHandle,
  mode: Mode,
): Promise<LoadedCharacter> {
  const motions: LoadedMotion[] = []
  const skills: LoadedSkill[] = []

  for await (const entry of handle.values()) {
    if (entry.kind === 'file' && isSkillFile(entry.name)) {
      const text = (await readText(handle, entry.name)) ?? ''
      const warning = checkSkillJson(text, entry.name)
      const { skill, error } = parseSkill(text)
      skills.push({ name: entry.name, skill, error, text, warning })
    }
    if (entry.kind !== 'directory' || entry.name !== 'motions') continue

    const root = entry as FileSystemDirectoryHandle
    for await (const sub of root.values()) {
      if (sub.kind !== 'directory') continue
      motions.push(await loadMotion(sub as FileSystemDirectoryHandle, sub.name))
    }
  }
  motions.sort((a, b) => compareNatural(a.folder, b.folder))
  // Natural order so S10.json follows S9.json, the same as the motion tabs.
  skills.sort((a, b) => compareNatural(a.name, b.name))

  const appearanceText = await readText(handle, 'appearance.json')
  let appearanceBase = DEFAULT_BASE
  // No file at all is readable in the sense that matters here: there is nothing of the author's
  // to lose, and Save creating one is not a silent overwrite.
  let appearanceReadable = true
  if (appearanceText !== null) {
    try {
      // The plugin reads this with AllowTrailingCommas and comments skipped
      // (AppearanceRegistry.cs:33-38), so a file with a comment in it works in game. Parsing it
      // strictly here made the editor fall back to the default donor and then, because
      // appearance.json was written on every save, put that default on disk over the author's
      // real donor. Same tolerance as the plugin, and a failure now blocks the write outright.
      const parsed = JSON.parse(stripJsoncExtras(appearanceText))
      if (typeof parsed?.base === 'string' && parsed.base) appearanceBase = parsed.base
    } catch {
      appearanceReadable = false
    }
  }

  return {
    handle,
    name: handle.name,
    mode,
    motions,
    skills,
    appearanceBase,
    hadAppearanceJson: appearanceText !== null,
    appearanceReadable,
  }
}

/**
 * Releases the blob URLs of a character that has been replaced. Safe precisely because a
 * LoadedCharacter is immutable and never comes back: nothing renders from its assets once a newer
 * load has taken its place.
 */
export function revokeAssets(character: LoadedCharacter): void {
  for (const motion of character.motions) {
    for (const asset of motion.assets.values()) URL.revokeObjectURL(asset.url)
    for (const sound of motion.sounds.values()) URL.revokeObjectURL(sound.url)
  }
}

const DB = 'motions-editor'
const STORE = 'handles'
const KEY = 'last'

function db(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1)
    req.onupgradeneeded = () => req.result.createObjectStore(STORE)
    req.onsuccess = () => resolve(req.result)
    req.onerror = () => reject(req.error)
  })
}

// Persisting "last opened folder" is a convenience for the Reopen button, not something the user
// is waiting on. If IndexedDB is unavailable or refuses the write (quota, permissions, enterprise
// policy), that must never block the folder from opening - so failures are swallowed here rather
// than thrown. Do not change this back to propagating errors.
export async function rememberFolder(handle: FileSystemDirectoryHandle, mode: Mode): Promise<void> {
  try {
    const d = await db()
    await new Promise<void>((resolve, reject) => {
      const tx = d.transaction(STORE, 'readwrite')
      tx.objectStore(STORE).put({ handle, mode }, KEY)
      tx.oncomplete = () => resolve()
      tx.onerror = () => reject(tx.error)
    })
  } catch {
    // ignored - see comment above
  }
}

export async function recallFolder(): Promise<{ handle: FileSystemDirectoryHandle; mode: Mode } | null> {
  try {
    const d = await db()
    return await new Promise((resolve) => {
      const tx = d.transaction(STORE, 'readonly')
      const req = tx.objectStore(STORE).get(KEY)
      req.onsuccess = () => resolve(req.result ?? null)
      req.onerror = () => resolve(null)
    })
  } catch {
    return null
  }
}

/** Handles lose permission across a browser restart. Regaining it needs a user gesture. */
export async function ensurePermission(handle: FileSystemDirectoryHandle): Promise<boolean> {
  const opts = { mode: 'readwrite' as const }
  if ((await handle.queryPermission(opts)) === 'granted') return true
  return (await handle.requestPermission(opts)) === 'granted'
}

export async function writeFile(
  dir: FileSystemDirectoryHandle,
  name: string,
  contents: string | BufferSource | Blob,
): Promise<void> {
  const handle = await dir.getFileHandle(name, { create: true })
  const stream = await handle.createWritable()
  await stream.write(contents)
  await stream.close()
}

/**
 * Copies dropped files into the motion folder, refusing PNGs the game cannot decode. Refusing
 * here rather than at load time is the whole point: in game the failure is a frame that silently
 * does not appear, with nothing to connect it back to the export settings that caused it.
 */
export async function importAssets(
  dir: FileSystemDirectoryHandle,
  files: File[],
): Promise<{ written: string[]; replaced: string[]; rejected: { name: string; why: string }[] }> {
  const written: string[] = []
  const replaced: string[] = []
  const rejected: { name: string; why: string }[] = []

  for (const file of files) {
    const lower = file.name.toLowerCase()
    const bytes = new Uint8Array(await file.arrayBuffer())

    if (lower.endsWith('.png')) {
      const why = pngRejection(readPngHeader(bytes))
      if (why) {
        rejected.push({ name: file.name, why })
        continue
      }
    } else if (!lower.endsWith('.wav') && !lower.endsWith('.ogg')) {
      rejected.push({ name: file.name, why: 'only .png, .wav and .ogg belong in a motion folder' })
      continue
    }

    // getFileHandle without `create` throws if the name doesn't exist yet - the cheapest way to
    // ask "would this write replace an author's existing art?" before doing it. Not a delete, but
    // still someone's file being silently clobbered if nobody says so.
    const existed = await dir.getFileHandle(file.name).then(() => true, () => false)
    await writeFile(dir, file.name, bytes)
    written.push(file.name)
    if (existed) replaced.push(file.name)
  }

  return { written, replaced, rejected }
}
