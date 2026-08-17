import { useEffect, useRef, useState } from 'react'
import {
  ChevronDown, FolderOpen, Play, Plus, Save, Square, TriangleAlert,
} from 'lucide-react'
import AssetPalette from './AssetPalette'
import Canvas, { ZOOM_MAX, ZOOM_MIN } from './Canvas'
import Inspector from './Inspector'
import MotionPicker from './MotionPicker'
import OpenScreen, { UnsupportedScreen } from './OpenScreen'
import SkillSection from './SkillSection'
import Timeline from './Timeline'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from '@/components/ui/dialog'
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { Slider } from '@/components/ui/slider'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Candidate, DEFAULT_BASE, Known, LoadedCharacter, Mode, createCharacter, ensurePermission,
  findCharacters, importAssets, isModsRoot, known, loadCharacter, nameRejection, pickFolder,
  recallFolder, rememberFolder, revokeAssets, writeFile,
} from './fs'
import { MotionEntry, mergeMotions, slotFor, spriteFor } from './motions'
import { boundsOf } from './png'
import { AnimationSpec, DEFAULT_FPS, Frame, frameIndexAt, serialiseSpec } from './spec'
import { Coin, Skill, newCoin, serialiseSkill, withCoin } from './skill'
import { Marker, removeMarker } from './SkillTimeline'
import {
  addFrameAt, addSfx, alignFrame, carryOver, carryOverNamed, clampFrameIndex, duplicateFrame,
  nudgeAllFrames,
  planSave, remapAfterRemoval, remapFrameIndex, removeFrame, removeSfx, SavePlan, sfxIn,
  sortFramesByTime,
  spaceEvenlyFrames,
} from './editing'

const SUPPORTED = typeof window !== 'undefined' && 'showDirectoryPicker' in window

/**
 * Focused things that consume arrow keys, Delete and Backspace themselves. Matched by ARIA role
 * as well as tag: the component library builds its tabs, menus and selects out of buttons and
 * divs, so a tag-only test says a tab is safe to type over when it is not.
 */
const INTERACTIVE = [
  'input', 'select', 'textarea', '[contenteditable=""]', '[contenteditable="true"]',
  '[role="tab"]', '[role="menu"]', '[role="menuitem"]', '[role="menuitemcheckbox"]',
  '[role="menuitemradio"]', '[role="listbox"]', '[role="option"]', '[role="combobox"]',
  '[role="slider"]', '[role="spinbutton"]', '[role="textbox"]', '[role="searchbox"]',
  '[role="radiogroup"]', '[role="radio"]', '[aria-haspopup]',
].join(',')

/** What to show the user when a filesystem call rejects. DOMException.message is the useful part. */
function why(e: unknown): string {
  return e instanceof Error ? e.message : String(e)
}

export default function App() {
  const [character, setCharacter] = useState<LoadedCharacter | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const [recalled, setRecalled] = useState<{ handle: FileSystemDirectoryHandle; mode: Mode } | null>(null)
  /** Non-empty only when one mod folder held several characters and the author has to say which. */
  const [choices, setChoices] = useState<Candidate[]>([])
  /** A folder whose kind the path could not settle. The only time the two kinds are ever asked about. */
  const [asking, setAsking] = useState<Candidate | null>(null)
  /** The folder a new character is being created in, and whether a mod folder is needed first. */
  const [creating, setCreating] = useState<{ handle: FileSystemDirectoryHandle; modsRoot: boolean } | null>(null)
  /** A picked folder that held no character, which is normal for a mod folder just made, and for mods/. */
  const [empty, setEmpty] = useState<{ handle: FileSystemDirectoryHandle; modsRoot: boolean } | null>(null)
  /**
   * Which motion and coin are on screen. By NAME rather than by index, because the two halves of
   * a coin are indexed differently - motions/S1_1/ is an index into character.motions, coins[1] is
   * an index into a skill document - and a coin can exist on one side and not the other. Empty
   * name means "whichever is first", so nothing has to seed it and a name that stops existing -
   * a folder deleted outside the editor, a character switched to - falls back rather than showing
   * an empty screen. Not `base`: that is taken, by the appearance donor below.
   */
  const [motionBase, setMotionBase] = useState('')
  const [coin, setCoin] = useState(0)
  const [frameIndex, setFrameIndex] = useState(0)
  const [onionSkin, setOnionSkin] = useState(true)
  const [zoom, setZoom] = useState(1)
  const [pan, setPan] = useState({ x: 0, y: 180 })

  // The loaded character is immutable once read; edits live in a separate specs array keyed by
  // tab index, so saving knows exactly what changed.
  const [specs, setSpecs] = useState<AnimationSpec[]>([])
  const [dirty, setDirty] = useState<Set<number>>(new Set())
  // The skill files, carried and tracked exactly like specs/dirty above: an edited copy per file,
  // and which of them differ from disk. Kept in their own pair rather than folded into `dirty`,
  // whose -1 already means the donor base and whose other values are motion indices.
  const [skillDocs, setSkillDocs] = useState<(Skill | null)[]>([])
  const [dirtySkills, setDirtySkills] = useState<Set<number>>(new Set())
  /** The selected marker on the coin timeline, or null when nothing is selected. */
  const [marker, setMarker] = useState<Marker | null>(null)
  const [selected, setSelected] = useState<number | null>(null)
  /** The selected sound on the timeline. Non-null takes the inspector over from the frame. */
  const [sfxIndex, setSfxIndex] = useState<number | null>(null)
  // Preview time in seconds while playing, null when stopped.
  const [playhead, setPlayhead] = useState<number | null>(null)

  const [base, setBase] = useState(DEFAULT_BASE)
  const [pending, setPending] = useState<SavePlan | null>(null)   // frozen confirmation plan
  /** Where an author asked to go while edits were still unsaved. null `to` means the open screen. */
  const [leaving, setLeaving] = useState<{ to: Known | null } | null>(null)
  const [saved, setSaved] = useState<string | null>(null)

  useEffect(() => {
    void recallFolder().then(setRecalled)
  }, [])

  // Same class as the drag and the specs-wipe: state read after the render that produced it.
  // importAssets and createMotion both re-read the folder, replacing `character`, which fires the
  // effect below. `base` was the one piece of state not keyed by folder name, so the sync used to
  // overwrite an unsaved edit with the on-disk value while `dirty` still held -1 - Save stayed
  // enabled, the dialog still listed appearance.json, and the write put the old base back.
  const dirtyRef = useRef(dirty)
  dirtyRef.current = dirty

  /**
   * Which character the editor state below belongs to. Everything that survives a re-read is
   * carried over by folder NAME, which is only sound while it is the same character being re-read:
   * every character has an Idle, so switching from one to another would otherwise hand the new
   * character the old one's unsaved Idle spec, dirty flags and donor base. Compared during render
   * so both effects agree within a commit.
   */
  const characterRef = useRef<LoadedCharacter | null>(null)
  const switched = character !== null && characterRef.current?.handle !== character.handle

  useEffect(() => {
    if (character && (switched || !dirtyRef.current.has(-1))) setBase(character.appearanceBase)
  }, [character, switched])

  // One entry per motion, over the union of the motion folders and the <Motion>.json files - so a
  // skill with a file and no folders, or folders and no file, is reachable either way. The coin
  // count comes from the EDITED documents, not the disk copies, or adding a coin would not add a
  // tab; the disk copy is the fallback for the single render before skillDocs is populated.
  const merged = character
    ? mergeMotions(
        character.motions.map((m) => m.folder),
        character.skills.map((sk, i) => ({
          name: sk.name,
          coins: (skillDocs[i] ?? sk.skill)?.coins.length ?? 0,
        })),
      )
    : []
  const entry: MotionEntry | undefined = merged.find((e) => e.base === motionBase) ?? merged[0]
  const slot = entry ? slotFor(entry, coin) : null
  /** Index into character.motions, or null when this coin has no folder of its own yet. */
  const tab = slot?.motion ?? null
  /** The motion on screen, or null when this coin has no folder. */
  const here = tab === null || !character ? null : character.motions[tab]
  /** Index into character.skills, or null when there is no `${motionBase}.json`. */
  const skillIndex = entry?.skill ?? null
  /**
   * The skill document on screen: the edited copy, or the one read from disk for the single
   * render before the carryOver effect populates skillDocs. null means the file could not be
   * parsed, which is the only absence the render below has to tell apart - undefined is not a
   * state anything downstream should have to think about, and treating it as "parsed fine" is
   * what crashed this on every character with a skill file.
   */
  const doc = skillIndex === null
    ? null
    : skillDocs[skillIndex] ?? character?.skills[skillIndex].skill ?? null

  const spec = tab === null ? undefined : specs[tab]

  // Folders loaded so far, in the order specs/dirty currently index by. Updated at the end of the
  // effect below, so it always holds the order from BEFORE the character that effect is reacting to.
  const prevMotionsRef = useRef<string[]>([])
  // Read inside the effect, which runs after the render that changed them - the same
  // state-after-its-render trap as dirtyRef above.
  const specsRef = useRef(specs)
  specsRef.current = specs
  const tabRef = useRef<number | null>(tab)
  tabRef.current = tab
  const skillDocsRef = useRef(skillDocs)
  skillDocsRef.current = skillDocs
  const dirtySkillsRef = useRef(dirtySkills)
  dirtySkillsRef.current = dirtySkills
  const prevSkillNamesRef = useRef<string[]>([])

  useEffect(() => {
    if (!character) return
    // importAssets and createMotion both re-read the folder with loadCharacter, which replaces
    // `character`; a switch to a different character replaces it too, and the two must not be
    // treated the same. carryOver is where that distinction lives, and where it is tested.
    const next = carryOver(
      prevMotionsRef.current, specsRef.current, dirtyRef.current, 0,
      character.motions, switched,
    )
    setSpecs(next.specs)
    setDirty(next.dirty)
    // next.tab is ignored: selection is by name now, so a re-read that re-sorts the folders keeps
    // the same coin on screen without remapping anything. carryOver keeps returning it for its
    // own tests; it is not this screen's source of truth any more.

    // Same rule, same reason: two characters both have an S1.json, so a switch must carry none
    // of it. structuredClone so editing never writes through to character.skills, which stays the
    // immutable on-disk copy.
    const nextSkills = carryOverNamed(
      prevSkillNamesRef.current, skillDocsRef.current, dirtySkillsRef.current,
      character.skills.map((sk) => sk.name),
      (i) => (character.skills[i].skill ? structuredClone(character.skills[i].skill) : null),
      switched,
    )
    setSkillDocs(nextSkills.items)
    setDirtySkills(nextSkills.dirty)
    prevSkillNamesRef.current = character.skills.map((sk) => sk.name)

    if (switched) {
      setMotionBase('')
      setCoin(0)
      setMarker(null)
      // Both index into the outgoing character's frames, and the new one may have fewer.
      setFrameIndex(0)
      setSelected(null)
      setSfxIndex(null)
      setSaved(null)
    }

    prevMotionsRef.current = character.motions.map((m) => m.folder)
    characterRef.current = character
  }, [character, switched])

  /**
   * The only way `character` is set. Every load mints a fresh blob URL per PNG and an import
   * re-reads the whole character, so the URLs of the character being replaced are released here -
   * nothing renders from them once a newer load is on screen.
   */
  function replaceCharacter(next: LoadedCharacter) {
    if (character && character !== next) revokeAssets(character)
    setCharacter(next)
  }

  // Timeline's pointerup handler is bound once, at the start of a drag, via a raw
  // window.addEventListener - it does not pick up new closures from the re-renders that happen
  // mid-drag as onFrameTime updates state. Kept current on every render (not in an effect, which
  // would lag a tick behind) so onFrameDragEnd below always sees the frame's true final position.
  const specRef = useRef(spec)
  specRef.current = spec

  function editSpec(patch: (s: AnimationSpec) => AnimationSpec) {
    // A coin with no folder of its own has no spec to edit; the panel offers to create one.
    if (tab === null) return
    setSpecs((prev) => prev.map((s, i) => (i === tab ? patch(structuredClone(s)) : s)))
    setDirty((prev) => new Set(prev).add(tab))
  }

  /**
   * The only way a skill document changes. Clones before patching for the same reason editSpec
   * does: the document is handed to a timeline that compares by identity, and mutating in place
   * leaves it drawing the previous render's numbers.
   */
  function editSkill(index: number, patch: (s: Skill) => Skill) {
    // Nothing to patch means nothing to flag. The map below already skips a document that is
    // null (unparseable) or not carried over yet, and marking it dirty anyway would leave Save
    // counting and offering to write a file it has no edited copy of.
    if (!skillDocs[index]) return
    setSkillDocs((prev) => prev.map((doc, i) => (i === index && doc ? patch(structuredClone(doc)) : doc)))
    setDirtySkills((prev) => new Set(prev).add(index))
  }

  /** Edits one coin of one skill document. Built on editSkill so the dirty tracking is the same. */
  function editCoin(index: number, at: number, patch: (c: Coin) => void) {
    editSkill(index, (s) => {
      const c = s.coins[at]
      if (c) patch(c)
      return s
    })
  }

  /**
   * The seconds a coin actually runs for, or null when its file's totalDuration is what runs.
   * TimelineBuilder.cs:385 replaces totalDuration with the sprite motion's duration wherever one
   * resolves, so this is what decides whether that field is a setting or a readout.
   */
  function durationOf(e: MotionEntry, at: number): number | null {
    const i = spriteFor(e, at)
    if (i === null) return null
    // The edited spec, not the disk copy: retiming the animation retimes the coin, and the
    // fractions drawn against it have to move with it rather than a save later.
    return (specs[i] ?? character!.motions[i].spec).duration
  }

  function updateFrame(i: number, patch: Partial<Frame>) {
    editSpec((s) => {
      s.frames[i] = { ...s.frames[i], ...patch }
      return s
    })
  }

  /**
   * Adds a delta to one frame's offset, reading the offset it is added to inside the state
   * updater rather than from the render's `spec`. The canvas drag binds `move` on window once, at
   * pointerdown, so every event in the drag sees the props (and therefore the `spec`) of that one
   * render, while the deltas it reports are incremental, so computing the new offset out here would
   * make each event write preDragOffset + oneIncrement and overwrite the drag so far instead of
   * accumulating it. Key repeat on the arrow keys can outrun a render the same way. Same class of
   * bug as the one specRef exists for: state read through a closure that outlives its render.
   */
  function nudgeFrame(i: number, dx: number, dy: number) {
    editSpec((s) => {
      s.frames[i] = {
        ...s.frames[i],
        offset: [s.frames[i].offset[0] + dx, s.frames[i].offset[1] + dy],
      }
      return s
    })
  }

  /**
   * The one way to change which frame is being viewed. `selected` used to be seeded only by the
   * "arrows move:" button and then left behind by prev/next and by clicking a timeline marker, so
   * an arrow key nudged a frame that was not on screen. Non-null selected follows the viewed
   * frame; null keeps meaning "arrows move ALL frames".
   */
  /** Resets what a frame index means when the motion under it changes. */
  function goToFrameless() {
    setFrameIndex(0)
    setSelected(null)
    setSfxIndex(null)
  }

  function goToFrame(i: number) {
    setFrameIndex(i)
    setSelected((s) => (s === null ? null : i))
  }

  function nudgeAll(dx: number, dy: number) {
    editSpec((s) => {
      s.frames = nudgeAllFrames(s.frames, dx, dy)
      return s
    })
  }

  /**
   * Removes frame `i` and never the PNG - see removeFrame's doc. Refuses (silently, same as
   * removeFrame itself) on a motion's last frame, so it never writes a spec.frames = [] that
   * parseSpec would reject on reload. frameIndex and selected are both remapped by position, not
   * left to drift: this project has already shipped a crash (Task 6) and a silent mis-edit
   * (Task 7) from an index surviving a frames-array change unremapped.
   */
  function removeSelectedFrame(i: number) {
    if (!spec) return
    const before = spec.frames.length
    if (before <= 1) return
    editSpec((s) => {
      s.frames = removeFrame(s.frames, i)
      return s
    })
    setFrameIndex((fi) => remapAfterRemoval(fi, i, before - 1))
    setSelected((sel) => remapAfterRemoval(sel, i, before - 1))
  }

  function spaceEvenly(fps: number) {
    editSpec((s) => {
      const spaced = spaceEvenlyFrames(s.frames, fps)
      s.frames = spaced.frames
      s.duration = spaced.duration
      return s
    })
  }

  /**
   * Called once a frame drag ends (not during it; see the comment on sortFramesByTime). Reads
   * specRef rather than the `spec` closure because this function is called from a window listener
   * bound at the start of the drag, before the moves that actually change frame i's time.
   * `selected` is remapped the same way as frameIndex, by identity via remapFrameIndex, because
   * the drag can reorder a different, selected frame past the one being dragged.
   */
  function onFrameDragEnd(i: number) {
    const current = specRef.current?.frames
    if (!current) return
    const sorted = sortFramesByTime(current)
    editSpec((s) => {
      s.frames = sortFramesByTime(s.frames)
      return s
    })
    setFrameIndex(remapFrameIndex(current, sorted, i) ?? i)
    setSelected((sel) => remapFrameIndex(current, sorted, sel))
  }

  async function alignAll(axis: 'xy' | 'x') {
    // Both are on screen whenever the buttons are, but a coin with no folder has neither.
    if (!spec || !here) return
    const motion = here
    // Each frame's decode is caught individually, so one corrupt or missing PNG leaves that
    // frame unchanged instead of rejecting the whole Promise.all and silently no-oping the
    // entire align: partial alignment is more useful than none.
    const failed: string[] = []
    const next = await Promise.all(
      spec.frames.map(async (f) => {
        const asset = motion.assets.get(f.sprite)
        if (!asset) return f
        try {
          const b = await boundsOf(asset.url, asset.width, asset.height)
          if (!b) return f
          return alignFrame(f, b, asset.width, asset.height, spec.ppu, axis)
        } catch {
          failed.push(f.sprite)
          return f
        }
      }),
    )
    editSpec((s) => {
      s.frames = next
      return s
    })
    if (failed.length > 0) {
      setProblem(`Could not read ${failed.join(', ')} to align it/them, left unchanged. Re-export the file and try again.`)
    }
  }

  /**
   * Writes precisely what `plan` listed - never re-reads live `dirty`. The dialog is the promise;
   * if a tab was edited after it opened, that edit simply stays dirty and unsaved rather than
   * sneaking into a write it was never shown in. Only the indices (and appearance.json, if part
   * of the plan) that were actually written come out of `dirty` afterward, so a since-dirtied tab
   * keeps its flag and its own Save button count.
   */
  async function save(plan: SavePlan) {
    setProblem(null)
    // A write can fail halfway (permission revoked, drive removed, file locked). Files already
    // written stay written, so the only honest thing to do is stop, say which ones made it, and
    // leave the rest dirty - a partial write nobody is told about is exactly what the
    // confirmation dialog exists to prevent.
    const written: string[] = []
    const skillsWritten: string[] = []
    let appearanceWritten = false
    let failure: string | null = null
    try {
      // Resolved by name at write time, not by the index the plan was built from. A folder
      // re-read landing between the dialog opening and Write can re-sort character.motions, and
      // a frozen index would then address a different motion than the one that was listed.
      for (const folder of plan.motions) {
        const i = character!.motions.findIndex((m) => m.folder === folder)
        // Gone from disk since the dialog opened. Skipped rather than guessed at; it shows up as
        // a shortfall against plan.files.length in the count below.
        if (i < 0 || !specs[i]) continue
        await writeFile(character!.motions[i].handle, 'animation.json', serialiseSpec(specs[i]))
        written.push(folder)
      }
      if (plan.appearance) {
        await writeFile(character!.handle, 'appearance.json', JSON.stringify({ base }, null, 2) + '\n')
        appearanceWritten = true
      }
      for (const name of plan.skills) {
        const i = character!.skills.findIndex((sk) => sk.name === name)
        // A file that failed to parse has no document, so there is nothing to write back and its
        // original bytes stay untouched. Skipped rather than refused: the rest still save.
        const doc = i < 0 ? null : skillDocs[i]
        if (!doc) continue
        await writeFile(character!.handle, name, serialiseSkill(doc))
        skillsWritten.push(name)
      }
    } catch (e) {
      failure = why(e)
    }

    const count = written.length + (appearanceWritten ? 1 : 0) + skillsWritten.length
    setSaved(count > 0 ? `Wrote ${count} file(s).` : null)
    if (failure !== null) {
      const done = written.map((folder) => `motions/${folder}/animation.json`)
      if (appearanceWritten) done.push('appearance.json')
      done.push(...skillsWritten)
      setProblem(
        `Save stopped after ${count} of ${plan.files.length} file(s): ${failure}\n` +
        (done.length > 0 ? `Written: ${done.join(', ')}\n` : '') +
        'Everything else is unchanged on disk and still marked unsaved.',
      )
    }
    // Back to indices to clear the dirty sets, resolved against the same live arrays the writes
    // themselves were resolved against.
    setDirty((prev) => {
      const next = new Set(prev)
      for (const folder of written) {
        const i = character!.motions.findIndex((m) => m.folder === folder)
        if (i >= 0) next.delete(i)
      }
      if (appearanceWritten) next.delete(-1)
      return next
    })
    setDirtySkills((prev) => {
      const next = new Set(prev)
      for (const name of skillsWritten) {
        const i = character!.skills.findIndex((sk) => sk.name === name)
        if (i >= 0) next.delete(i)
      }
      return next
    })
    setPending(null)
  }

  // The only action that creates a directory, so it gets the same care as a write: the name is
  // typed deliberately (no default), and it only ever creates inside motions/ of the picked folder.
  async function createMotion(typed: string) {
    // Trimmed: a trailing space yields a folder the plugin cannot match to a MOTION_DETAIL, and
    // a trailing-space directory is awkward to remove again on Windows.
    const name = typed.trim()
    if (!name) return
    setProblem(null)
    try {
      const root = await character!.handle.getDirectoryHandle('motions', { create: true })
      await root.getDirectoryHandle(name, { create: true })
      replaceCharacter(await loadCharacter(character!.handle, character!.mode))
    } catch (e) {
      setProblem(`Could not create motions/${name}: ${why(e)}`)
    }
  }

  // Creates a file rather than a folder, and otherwise the same deal as createMotion: written
  // straight away because its existence is the act, then edited and saved like any other. Only
  // ever <Motion>.json at the character folder, which is the one place the plugin reads them.
  async function createSkillFile(name: string) {
    setProblem(null)
    try {
      await writeFile(character!.handle, `${name}.json`, serialiseSkill({ coins: [newCoin()] }))
      replaceCharacter(await loadCharacter(character!.handle, character!.mode))
    } catch (e) {
      setProblem(`Could not create ${name}.json: ${why(e)}`)
    }
  }

  // Selection decides the target: a frame selected moves that frame, nothing selected moves
  // every frame at once.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      // Belt-and-braces alongside `inert` on the main content below: the confirmation dialog is
      // meant to be an honest, frozen snapshot of what Save will write, so nothing - including an
      // arrow-key nudge of a clean tab sitting behind the dialog - may change editor state while
      // it's open. The discard prompt counts too: it names a number of unsaved changes, and a
      // nudge behind it would make that number wrong while it is on screen.
      if (pending || leaving) return
      // Anything focused that owns its own arrow keys handles them itself. This used to be a
      // tagName list of INPUT/SELECT/TEXTAREA, which was true of the native controls it was
      // written against and quietly false afterwards: the component library renders a tab as
      // <button role="tab"> and a menu item as <div role="menuitem">, so arrowing between tabs
      // also nudged every frame in the motion and marked it dirty, with nothing on screen saying
      // so. Two independent checks now, because either alone has been wrong before:
      //   - the widget already handled the key, which is the generic signal and needs no list
      //   - the focused element is a widget by role or tag, for any that does not preventDefault
      if (e.defaultPrevented) return
      const focused = document.activeElement as HTMLElement | null
      if (focused && focused !== document.body && focused.closest(INTERACTIVE)) return

      // Removes whatever the panels are showing, innermost selection first: a skill marker, else
      // the sound, else the frame being viewed. Each step was added when its selection became
      // possible - targeting the frame unconditionally deleted a frame while the panel showed a
      // sound, and both halves of a coin are now on screen at once, so a selected phase would go
      // the same way. frameIndex, not `selected`: the latter only steers arrow-key nudges.
      if (e.key === 'Delete' || e.key === 'Backspace') {
        e.preventDefault()
        if (marker !== null && skillIndex !== null && doc?.coins[coin]) {
          editCoin(skillIndex, coin, (c) => removeMarker(c, marker))
          setMarker(null)
        } else if (sfxIndex !== null) {
          editSpec((s) => ({ ...s, sfx: removeSfx(s.sfx, sfxIndex) }))
          setSfxIndex(null)
        } else if (spec) {
          removeSelectedFrame(frameIndex)
        }
        return
      }

      // Arrows act on sprite frames only, so a coin with no folder of its own has nothing for
      // them to move. Skill markers stay drag-only.
      const step = e.shiftKey ? 0.1 : 0.01
      const dx = e.key === 'ArrowRight' ? step : e.key === 'ArrowLeft' ? -step : 0
      const dy = e.key === 'ArrowUp' ? step : e.key === 'ArrowDown' ? -step : 0
      if ((dx === 0 && dy === 0) || !spec) return
      e.preventDefault()

      if (selected === null) nudgeAll(dx, dy)
      else nudgeFrame(selected, dx, dy)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selected, spec, tab, pending, leaving, frameIndex, sfxIndex, marker, skillIndex, coin, skillDocs])

  // Steps through frames with the same frameIndexAt lookup the runtime uses, rather than
  // tweening: a blended preview would have authors aligning frames against a lie.
  useEffect(() => {
    if (playhead === null || !spec) return
    let raf = 0
    const started = performance.now()
    /** End of the slice already played, per sfxIn. Below zero so a sound at 0 fires on lap one. */
    let last = -1
    const playing: HTMLAudioElement[] = []

    const fire = (from: number, to: number) => {
      for (const s of sfxIn(specRef.current?.sfx ?? [], from, to)) {
        // characterRef/tabRef for the same reason the frame lookup below uses specRef: this loop
        // outlives the render that started it, and an import mid-preview revokes the old URLs.
        const sound = tabRef.current === null
          ? undefined
          : characterRef.current?.motions[tabRef.current]?.sounds.get(s.file)
        // A sound named in animation.json with no file beside it is silent here, exactly as it is
        // in game. It is not an error to report from a render loop.
        if (!sound) continue
        const audio = new Audio(sound.url)
        // The same two fields the timeline draws the bar from, so the preview sounds like the bar
        // looks - and like the game, which seeks by clipIn and stops the channel after duration.
        if (s.clipIn) audio.currentTime = s.clipIn
        if (s.duration) setTimeout(() => audio.pause(), s.duration * 1000)
        playing.push(audio)
        audio.onended = () => { playing.splice(playing.indexOf(audio), 1) }
        // Rejects only when the tab has had no gesture, and the preview is started by a click.
        void audio.play().catch(() => {})
      }
    }

    const tick = (now: number) => {
      const t = ((now - started) / 1000) % spec.duration
      setPlayhead(t)
      // A wrap closes out the old lap before opening the next, or a sound in the tail of the
      // motion would be skipped on every loop.
      if (t < last) { fire(last, spec.duration); last = -1 }
      fire(last, t)
      last = t
      // specRef, not spec: dragging a marker while the preview is running must not restart this
      // effect (that would reset `started` and jump the clock to zero), but the preview still
      // needs to see the frame's live time, not the one it had when playback started.
      setFrameIndex(Math.max(0, frameIndexAt(specRef.current?.frames.map((f) => f.t) ?? [], t)))
      raf = requestAnimationFrame(tick)
    }
    raf = requestAnimationFrame(tick)
    return () => {
      cancelAnimationFrame(raf)
      // Stop means stop: a sound longer than the rest of the motion would otherwise outlive the
      // preview that started it, including when the tab is switched out from under it.
      for (const audio of playing) audio.pause()
    }
    // Restarting on every playhead change would reset the clock, so this deliberately
    // depends only on whether playback is on at all.
  }, [playhead === null, spec?.duration, tab])

  // Every path into a character goes through here, including the Reopen button, whose handle came
  // out of IndexedDB and may point at a folder that has since been moved or deleted. Catching here
  // rather than at each call site is what keeps that failure from being silent.
  async function open(handle: FileSystemDirectoryHandle, mode: Mode) {
    setProblem(null)
    try {
      if (!(await ensurePermission(handle))) {
        setProblem('Permission to read and write that folder was refused.')
        return
      }
      const rejection = nameRejection(handle.name, mode)
      if (rejection) {
        setProblem(rejection)
        return
      }
      const loaded = await loadCharacter(handle, mode)
      // Opening the folder is what the user asked for; remembering it for next time is bookkeeping
      // that must never gate that. rememberFolder no longer throws, but this order stands anyway.
      replaceCharacter(loaded)
      await rememberFolder(handle, mode)
    } catch (e) {
      setProblem(`Could not open ${handle.name}: ${why(e)}`)
    }
  }

  async function choose() {
    let handle: FileSystemDirectoryHandle
    try {
      handle = await pickFolder()
    } catch (e) {
      // AbortError is the user dismissing the picker - nothing to report. Anything else is a real
      // failure, and reporting it as a cancellation is how a broken editor looks like a working one.
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        setProblem(`Could not open the folder picker: ${why(e)}`)
      }
      return
    }

    setProblem(null)
    setChoices([])
    setAsking(null)
    setCreating(null)
    setEmpty(null)
    let found: Candidate[]
    try {
      found = await findCharacters(handle)
    } catch (e) {
      setProblem(`Could not read ${handle.name}: ${why(e)}`)
      return
    }

    // Not an error on its own: a mod folder made a minute ago holds no character yet, and this is
    // where saying so and offering to create one belongs. It stays a refusal rather than
    // scaffolding straight away, because the same state means "you picked the wrong folder".
    if (found.length === 0) {
      setEmpty({ handle, modsRoot: await isModsRoot(handle) })
      return
    }
    // Kept whatever the count: this is the list the "switch character" menu offers later, and
    // re-picking the folder just to reach a sibling is the thing that list exists to avoid.
    setChoices(found)
    // One character is the common case by a wide margin; making an author confirm it would be a
    // dialog that only ever has one button.
    if (found.length === 1) {
      // The one case the folder cannot answer for itself: picked on its own, no appearance.json.
      if (found[0].mode === null) setAsking(found[0])
      else await open(found[0].handle, found[0].mode)
    }
  }

  /** Picks the folder a new character goes in. The picker's own "new folder" button makes it. */
  async function startNewCharacter() {
    let handle: FileSystemDirectoryHandle
    try {
      handle = await pickFolder()
    } catch (e) {
      if (!(e instanceof DOMException && e.name === 'AbortError')) {
        setProblem(`Could not open the folder picker: ${why(e)}`)
      }
      return
    }
    setProblem(null)
    setEmpty(null)
    setCreating({ handle, modsRoot: await isModsRoot(handle) })
  }

  // Creates directories, so it gets the care createMotion gets: the name is typed deliberately,
  // both rejections have already run against it, and it only ever writes inside the picked folder.
  async function createCharacterIn(
    picked: FileSystemDirectoryHandle,
    name: string,
    mode: Mode,
    modFolder: string | null,
  ) {
    setProblem(null)
    try {
      // modFolder is set only when the picked folder is mods/ - the mod itself has to exist before
      // there is anywhere valid to put a character.
      const mod = modFolder
        ? await picked.getDirectoryHandle(modFolder, { create: true })
        : picked
      const made = await createCharacter(mod, name, mode)
      setCreating(null)
      setChoices([made])
      await open(made.handle, made.mode)
    } catch (e) {
      setProblem(`Could not create ${name} in ${modFolder ?? picked.name}: ${why(e)}`)
    }
  }

  /**
   * Leaves the character that is open, for a sibling in the same mod, or back to the opening
   * screen. Unsaved edits live only in `specs`, so anything still dirty is gone the moment the
   * next character loads; that is worth a confirmation, and this is the only path that can lose
   * an edit without writing anything.
   */
  function leave(to: Known | null) {
    if (dirty.size + dirtySkills.size > 0) {
      setLeaving({ to })
      return
    }
    commitLeave(to)
  }

  function commitLeave(to: Known | null) {
    setLeaving(null)
    setProblem(null)
    // Stopped on both paths: the preview is a running animation frame loop reading the outgoing
    // character's spec, and nothing below cancels it.
    setPlayhead(null)
    if (to) {
      void open(to.handle, to.mode)
      return
    }
    if (character) revokeAssets(character)
    setCharacter(null)
    characterRef.current = null
    prevMotionsRef.current = []
    prevSkillNamesRef.current = []
    setSpecs([])
    setDirty(new Set())
    setSkillDocs([])
    setDirtySkills(new Set())
    setMarker(null)
    setSaved(null)
    setPlayhead(null)
  }

  if (!SUPPORTED) return <UnsupportedScreen />

  if (!character) {
    return (
      <OpenScreen
        recalled={recalled}
        choices={choices}
        asking={asking}
        creating={creating}
        empty={empty}
        problem={problem}
        onPick={() => void choose()}
        onStartNew={() => void startNewCharacter()}
        onOpen={(handle, mode) => void open(handle, mode)}
        onCreate={(picked, name, mode, modFolder) =>
          void createCharacterIn(picked, name, mode, modFolder)}
        setAsking={setAsking}
        setCreating={setCreating}
        setEmpty={setEmpty}
      />
    )
  }

  // The EDITED spec's frame count - here.spec is the immutable disk copy, so
  // counting it left "/ 2" on screen after three frames were added and let `next` walk the index
  // off the end of the array the canvas actually draws. The disk copy stays as the fallback for
  // the single render before the effect above populates `specs`.
  const frameCount = (spec ?? here?.spec)?.frames.length ?? 0
  // Everything findCharacters turned up in the mod this character came from, minus this one.
  const siblings = known(choices).filter((c) => c.handle !== character.handle)
  // Files differing from disk, across both halves of the character.
  const unsaved = dirty.size + dirtySkills.size

  return (
    <main className="p-8">
      {/* inert, not just conditional styling: while the confirmation dialog is open, nothing in
          here should be clickable, focusable or reachable by keyboard - it is the native
          replacement for a hand-rolled focus trap, and the keydown gate above is the redundant
          backstop in case some interaction reaches state without going through focus at all. */}
      <div inert={pending !== null || leaving !== null}>
      <div className="flex items-center gap-3">
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button variant="ghost" className="-ml-2 gap-2 px-2 text-lg font-semibold">
                {character.name}
                <ChevronDown className="size-4 opacity-50" />
              </Button>
            }
          />
          <DropdownMenuContent align="start" className="w-64">
            {siblings.length > 0 && (
              <>
                <DropdownMenuLabel>Others in this mod</DropdownMenuLabel>
                {siblings.map((c) => (
                  <DropdownMenuItem key={c.path} onClick={() => leave(c)}>
                    <FolderOpen className="size-3.5 opacity-60" />
                    <span className="truncate font-mono text-xs">{c.path}</span>
                  </DropdownMenuItem>
                ))}
                <DropdownMenuSeparator />
              </>
            )}
            <DropdownMenuItem onClick={() => leave(null)}>
              <FolderOpen className="size-3.5 opacity-60" />
              Open another mod…
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <Button size="sm" onClick={() => setPending(planSave(character, dirty, dirtySkills))} disabled={dirty.size + dirtySkills.size === 0}>
          <Save className="size-3.5" />
          Save{unsaved > 0 && ` (${unsaved})`}
        </Button>
        {saved && <span className="text-xs text-emerald-600">{saved}</span>}
      </div>
      <p className="text-xs text-muted-foreground">
        {character.mode === 'appearance'
          ? `registers as !motions_${character.name}`
          : 'overrides an existing appearance'}
      </p>
      {character.mode === 'appearance' && (
        <div className="mt-3 flex items-center gap-2">
          <Label htmlFor="donor" className="text-xs">Built on</Label>
          <Input id="donor" className="h-8 w-72 text-xs" value={base}
                 onChange={(e) => { setBase(e.target.value); setDirty((p) => new Set(p).add(-1)) }} />
          <span className="text-xs text-muted-foreground">
            the vanilla appearance cloned as a donor rig
          </span>
        </div>
      )}

      {problem && (
        // Reused from the "pick a folder" screen: the one place a rejected import (or a failed
        // align, from Task 6) surfaces once a character is already loaded. whitespace-pre-line
        // because a multi-PNG drop joins its rejections with newlines.
        <Alert variant="destructive" className="mt-4">
          <TriangleAlert />
          <AlertDescription className="whitespace-pre-line">{problem}</AlertDescription>
        </Alert>
      )}

      <div className="mt-4 flex items-center gap-2">
        {/* One tab per motion, over the union of the motion folders and the <Motion>.json files.
            A skill that exists only as S1.json used to have no tab here at all, so its coins were
            reachable only from the other view. */}
        <Tabs value={entry?.base ?? ''}
              // selected is a frame index into the OLD coin's spec; carrying it into a motion with
              // fewer frames would crash the arrow-key handler on spec.frames[selected].offset.
              // null is the safe reset - it means "arrows move ALL frames", not "frame 0 of the
              // wrong motion".
              onValueChange={(v) => { setMotionBase(v); setCoin(0); setMarker(null); goToFrameless() }}>
          <TabsList>
            {merged.map((e) => (
              <TabsTrigger key={e.base} value={e.base}>
                {e.base}
                {e.coins.length > 1 && (
                  <span className="ml-1 text-[10px] opacity-60">{e.coins.length}</span>
                )}
                {/* The skill file's unsaved marker. It used to live on the "Skill timings" tab,
                    which this strip replaced; without it here an edited S1.json shows up only in
                    the Save count. Per motion, not per coin, because dirtiness is tracked per
                    file - the coin row's dot is the sprite half, which is per folder. */}
                {e.skill !== null && dirtySkills.has(e.skill) && ' •'}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>

        <MotionPicker
          existing={character.motions.map((m) => m.folder)}
          onCreate={(name) => void createMotion(name)}
        />
      </div>

      {/* Coin n is coins[n] AND motions/<base>_<n>/, with motions/<base>/ as coin 0 - the pairing
          MotionInjector.cs:46-58 and TimelineBuilder.cs:385 make between the two files. One
          numbering, so the strip can list a coin that only one side has. */}
      {entry && (entry.takesCoins || entry.coins.length > 1) && (
        <div className="mt-2 flex items-center gap-2">
          <span className="text-xs text-muted-foreground">Coins</span>
          <Tabs value={String(coin)}
                onValueChange={(v) => { setCoin(Number(v)); setMarker(null); goToFrameless() }}>
            <TabsList>
              {/* One past the end while a new coin is being set up, so the tab it is created from
                  is on screen and selected rather than appearing only after the first write. */}
              {Array.from({ length: Math.max(entry.coins.length, coin + 1) }, (_, i) => {
                const s = slotFor(entry, i)
                return (
                  <TabsTrigger key={i} value={String(i)}>
                    coin {i + 1}
                    {s.motion !== null && dirty.has(s.motion) && ' •'}
                  </TabsTrigger>
                )
              })}
            </TabsList>
          </Tabs>

          {/* Only for a skill: the loader refuses _N on anything else (SpriteMotionLoader.cs:34),
              so offering to add a coin to Idle would be offering a folder it skips with a warning. */}
          {entry.takesCoins && (
            <Button variant="outline" size="sm"
                    title={`Selects coin ${entry.coins.length + 1}, which neither file has yet`}
                    onClick={() => { setCoin(entry.coins.length); setMarker(null); goToFrameless() }}>
              <Plus className="size-3.5" />
              Add coin
            </Button>
          )}
        </div>
      )}

      {/* One line under the coin strip about the coin on screen, read from the two files
          together. Each is a plugin behaviour neither half could see on its own. */}
      {entry && slot && (
        <p className="mt-2 text-xs text-muted-foreground">
          {here !== null && skillIndex !== null && !slot.json
            // TimelineBuilder emits one timeline per coins[] entry, so a folder past the end of
            // that array is art the game never reaches. Silent until now.
            ? `motions/${slot.folder}/ has art, but ${character.skills[skillIndex].name} has no coin ${coin + 1}, so the game never builds it.`
            : here === null && spriteFor(entry, coin) !== null
              ? `Coin ${coin + 1} has no folder of its own, so it animates with motions/${entry.base}/.`
              : entry.takesCoins && slot.coin === 0 && here !== null
                ? `motions/${entry.base}/ is coin 1, and the fallback every coin without its own folder uses.`
                : ''}
        </p>
      )}

      {/* A coin selected from the strip that has no folder of its own - either a gap the skill
          file implies, or the one past the end that Add coin selects. */}
      {entry && slot && here === null && (
        <div className="mt-3 rounded-lg border p-4">
          <p className="text-sm font-medium">
            No <code>motions/{slot.folder}/</code> yet.
          </p>
          <p className="mt-1 text-xs text-muted-foreground">
            {spriteFor(entry, coin) !== null
              ? `Coin ${coin + 1} uses motions/${entry.base}/ until it has a folder of its own.`
              : `Nothing animates coin ${coin + 1}: there is no motions/${entry.base}/ to fall back on either.`}
          </p>
          <Button size="sm" className="mt-3" onClick={() => void createMotion(slot.folder)}>
            <Plus className="size-3.5" />
            Create motions/{slot.folder}/
          </Button>
        </div>
      )}

      {here && (
        <>
          <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs">
            <div className="flex items-center gap-2">
              <Checkbox id="onion" checked={onionSkin}
                        onCheckedChange={(v) => setOnionSkin(v === true)} />
              <Label htmlFor="onion" className="text-xs">Onion skin</Label>
            </div>

            <Separator orientation="vertical" className="h-5" />

            {/* Given room deliberately. At 128px, wedged between a checkbox and a row of buttons,
                this read as another button rather than something you drag. The end labels and the
                readout are what make it legible as a range at a glance.
                No htmlFor: the Slider's root is a div, which is not a labelable element, so the
                association would have been decorative. The control carries its own name instead. */}
            <div className="flex items-center gap-2">
              <span className="text-xs">Zoom</span>
              <span className="text-[10px] text-muted-foreground">{ZOOM_MIN}×</span>
              <Slider aria-label="Zoom" className="w-44" min={ZOOM_MIN} max={ZOOM_MAX} step={0.05}
                      value={[zoom]}
                      onValueChange={(v) => setZoom(Array.isArray(v) ? v[0] : v)} />
              <span className="text-[10px] text-muted-foreground">{ZOOM_MAX}×</span>
              <button onClick={() => setZoom(1)} title="Reset to 100%"
                      className="w-12 rounded tabular-nums text-muted-foreground hover:text-foreground focus-visible:ring-[3px] focus-visible:ring-ring/50 focus-visible:outline-none">
                {zoom.toFixed(2)}×
              </button>
            </div>

            <Separator orientation="vertical" className="h-5" />

            <div className="flex items-center gap-1">
              <Button variant="outline" size="sm"
                      onClick={() => goToFrame(clampFrameIndex(frameIndex - 1, frameCount))}>
                Prev
              </Button>
              <span className="w-20 text-center tabular-nums text-muted-foreground">
                {frameIndex + 1} / {frameCount}
              </span>
              <Button variant="outline" size="sm"
                      onClick={() => goToFrame(clampFrameIndex(frameIndex + 1, frameCount))}>
                Next
              </Button>
            </div>

            <Separator orientation="vertical" className="h-5" />

            <Button variant="outline" size="sm" onClick={() => void alignAll('xy')}>Align all</Button>
            <Button variant="outline" size="sm" onClick={() => void alignAll('x')}>Align X only</Button>
            {/* The label states what the arrows do right now, not what clicking would switch to -
                it is a status readout that happens to be clickable, and swapping those reads as
                the opposite setting. */}
            <Button variant={selected === null ? 'secondary' : 'default'} size="sm"
                    onClick={() => setSelected(selected === null ? frameIndex : null)}>
              {selected === null ? 'Arrows move: all frames' : `Arrows move: frame ${selected + 1}`}
            </Button>
          </div>
          {spec && (
            <>
              <div className="mt-2 flex h-[520px] rounded border">
                <div
                  className="flex-1"
                  onDragOver={(e) => e.preventDefault()}
                  onDrop={async (e) => {
                    e.preventDefault()
                    const files = [...e.dataTransfer.files]
                    setProblem(null)
                    // Dropping a *folder* hands over a File whose arrayBuffer() rejects, and an
                    // async handler that rejects takes the whole drop down without a word.
                    let result
                    try {
                      result = await importAssets(here.handle, files)
                    } catch (err) {
                      setProblem(
                        `Could not import ${files.map((f) => f.name).join(', ')}: ${why(err)}\n` +
                        'Drop individual .png, .wav or .ogg files - a folder cannot be read this way.',
                      )
                      return
                    }
                    if (result.rejected.length > 0) {
                      setProblem(result.rejected.map((r) => `${r.name}: ${r.why}`).join('\n'))
                    }
                    if (result.written.length > 0) {
                      // A re-imported file replaces its old bytes (writeFile always truncates) -
                      // not a delete, but still someone's art disappearing with no acknowledgement
                      // if "4 imported" doesn't say which of those 4 already existed.
                      const newCount = result.written.length - result.replaced.length
                      const parts = []
                      if (newCount > 0) parts.push(`${newCount} imported`)
                      if (result.replaced.length > 0) parts.push(`${result.replaced.length} replaced`)
                      setSaved(parts.join(', '))
                      // Re-read the folder so the new files appear as assets. They are not frames
                      // yet - see the "+ name" buttons below - loadCharacter only puts every PNG
                      // into spec.frames when there was no animation.json to begin with.
                      replaceCharacter(await loadCharacter(character.handle, character.mode))
                    }
                  }}
                >
                  {/* frameIndex is safe to capture in onDragFrame: a drag cannot change which
                      frame is being dragged. The offset it is added to must not be captured -
                      see nudgeFrame. */}
                  <Canvas
                    spec={spec}
                    assets={here.assets}
                    index={frameIndex}
                    onionSkin={onionSkin}
                    zoom={zoom}
                    pan={pan}
                    onPan={setPan}
                    onZoom={setZoom}
                    onDragFrame={(dx, dy) => nudgeFrame(frameIndex, dx, dy)}
                  />
                </div>
                <Inspector
                  spec={spec}
                  index={frameIndex}
                  sfxIndex={sfxIndex}
                  onFrame={(p) => updateFrame(frameIndex, p)}
                  onSfx={(p) => editSpec((s) => {
                    // Deleting rather than assigning undefined: both keys are optional in the
                    // file, and "absent" is what makes the game play the whole clip.
                    const next = { ...s.sfx[sfxIndex!], ...p }
                    for (const k of ['clipIn', 'duration'] as const) {
                      if (next[k] === undefined) delete next[k]
                    }
                    s.sfx[sfxIndex!] = next
                    return s
                  })}
                  onSpec={(p) => editSpec((s) => ({ ...s, ...p }))}
                  onRemove={() => removeSelectedFrame(frameIndex)}
                  onDuplicate={() => {
                    editSpec((s) => ({ ...s, ...duplicateFrame(s.frames, frameIndex, s.duration) }))
                    // The copy lands after the source, so following it is what someone expects
                    // from a duplicate: you adjust the new one, not the one you copied.
                    setSfxIndex(null)
                    setFrameIndex(frameIndex + 1)
                  }}
                  onRemoveSfx={() => {
                    editSpec((s) => ({ ...s, sfx: removeSfx(s.sfx, sfxIndex!) }))
                    setSfxIndex(null)
                  }}
                />
                <AssetPalette
                  assets={here.assets}
                  sounds={here.sounds}
                  spec={spec}
                  onAddSprite={(name) => editSpec((s) => {
                    s.frames = addFrameAt(s.frames, name, s.duration)
                    s.duration = s.duration + 1 / DEFAULT_FPS
                    return s
                  })}
                  onAddSound={(name) => {
                    // At the current frame's time, which is where someone looking at a hit frame
                    // wants the hit sound. 0 would need dragging every time.
                    const at = spec.frames[frameIndex]?.t ?? 0
                    editSpec((s) => ({ ...s, sfx: addSfx(s.sfx, name, at) }))
                    setSfxIndex(spec.sfx.length)
                  }}
                />
              </div>

              <Button variant="outline" size="sm" className="mt-3"
                      onClick={() => setPlayhead(playhead === null ? 0 : null)}>
                {playhead === null ? <Play className="size-3.5" /> : <Square className="size-3.5" />}
                {playhead === null ? 'Play' : 'Stop'}
              </Button>

              <Timeline
                spec={spec}
                index={frameIndex}
                playhead={playhead}
                // Picking a frame drops the sound selection, so the inspector only ever shows
                // whichever was clicked last.
                onPick={(i) => { setSfxIndex(null); goToFrame(i) }}
                onFrameTime={(i, t) => updateFrame(i, { t })}
                onFrameDragEnd={onFrameDragEnd}
                onSfxChange={(i, next) => editSpec((s) => { s.sfx[i] = next; return s })}
                sfxIndex={sfxIndex}
                sfxLength={(file) => here.sounds.get(file)?.seconds ?? 0}
                onPickSfx={setSfxIndex}
                onSpace={spaceEvenly}
                onDuration={(duration) => editSpec((s) => ({ ...s, duration }))}
              />
            </>
          )}
        </>
      )}
      {/* Reached by any character with no sprite motions yet - including a bundle-driven one,
          where there is nothing wrong and nothing missing. Add motion above is the way out of
          both, so this says where it is rather than reporting an absence. */}
      {character.motions.length === 0 && character.skills.length === 0 && (
        <p className="mt-4 text-sm text-muted-foreground">
          No sprite motions here yet. <strong>Add motion</strong> creates the first one: it makes
          a <code>motions/&lt;Name&gt;/</code> folder to drop PNGs into. Any bundle in this
          character keeps working either way.
        </p>
      )}

      {/* The three edit callbacks assert skillIndex: SkillSection only reaches them from the
          branches where `file` is non-null, which is exactly when skillIndex is. */}
      {entry && (
        <SkillSection
          entry={entry}
          coin={coin}
          file={skillIndex === null ? null : character.skills[skillIndex]}
          doc={doc}
          duration={durationOf(entry, coin)}
          marker={marker}
          onSelectMarker={setMarker}
          onCreateFile={(name) => void createSkillFile(name)}
          onEditCoin={(patch) => editCoin(skillIndex!, coin, patch)}
          onAddCoin={() => {
            editSkill(skillIndex!, (s) => ({ ...s, coins: withCoin(s.coins, coin) }))
            setMarker(null)
          }}
          onRemoveCoin={() => {
            editSkill(skillIndex!, (s) => { s.coins.splice(coin, 1); return s })
            setCoin(Math.max(0, coin - 1))
            setMarker(null)
          }}
        />
      )}
      </div>

      {/* Outside the inert wrapper on purpose - the dialog is the one thing that must stay
          interactive while it is up. It also traps focus and marks the rest of the page inert on
          its own, which is what the wrapper above was hand-rolling; the wrapper stays because the
          arrow-key listener is bound to window and never sees a focus trap at all. */}
      {/* Unsaved edits live only in `specs`. Loading the next character replaces them, so this is
          the one place an author can lose work without a write ever happening. */}
      <Dialog open={leaving !== null} onOpenChange={(o) => { if (!o) setLeaving(null) }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Discard unsaved changes?</DialogTitle>
            <DialogDescription>
              {/* `unsaved`, not dirty.size: leave() gates on both halves, so counting only the
                  sprite motions told an author with skill edits pending that they had 0 unsaved
                  changes and then discarded them. */}
              {unsaved} unsaved {unsaved === 1 ? 'change' : 'changes'} in {character.name}.
              Leaving now loses {unsaved === 1 ? 'it' : 'them'}. Nothing on disk changes.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setLeaving(null)}>Keep editing</Button>
            <Button variant="destructive" onClick={() => leaving && commitLeave(leaving.to)}>
              Discard and leave
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={pending !== null} onOpenChange={(o) => { if (!o) setPending(null) }}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Write into {character.name}?</DialogTitle>
            <DialogDescription>
              Nothing else is touched, and nothing is deleted.
            </DialogDescription>
          </DialogHeader>
          <ul className="flex flex-col gap-1 rounded-md border bg-muted/40 p-3 font-mono text-xs">
            {pending?.files.map((f) => <li key={f}>{f}</li>)}
          </ul>
          <DialogFooter>
            <Button variant="outline" onClick={() => setPending(null)}>Cancel</Button>
            {/* pending is captured, not re-read: the plan was frozen when Save was clicked. */}
            <Button onClick={() => pending && void save(pending)}>Write</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </main>
  )
}
