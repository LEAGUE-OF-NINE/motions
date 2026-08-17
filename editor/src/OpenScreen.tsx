import { FolderOpen, Plus, RotateCcw, TriangleAlert } from 'lucide-react'
import NewCharacter from './NewCharacter'
import { Alert, AlertAction, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Candidate, Mode, known } from './fs'

/** A picked folder that turned out to hold no character, and whether it is the mods root. */
export interface EmptyFolder {
  handle: FileSystemDirectoryHandle
  modsRoot: boolean
}

interface Props {
  /** The folder remembered from last session, offered as a one-click reopen. */
  recalled: { handle: FileSystemDirectoryHandle; mode: Mode } | null
  /** Everything the last pick turned up. More than one means the author has to choose. */
  choices: Candidate[]
  /** A folder whose kind the path could not settle. The only time the two kinds are ever asked about. */
  asking: Candidate | null
  /** The folder a new character is being created in, and whether a mod folder is needed first. */
  creating: EmptyFolder | null
  /** A picked folder that held no character, which is normal for a mod folder just made, and for mods/. */
  empty: EmptyFolder | null
  problem: string | null
  onPick: () => void
  onStartNew: () => void
  onOpen: (handle: FileSystemDirectoryHandle, mode: Mode) => void
  onCreate: (
    picked: FileSystemDirectoryHandle, name: string, mode: Mode, modFolder: string | null,
  ) => void
  setAsking: (c: Candidate | null) => void
  setCreating: (f: EmptyFolder | null) => void
  setEmpty: (f: EmptyFolder | null) => void
}

/** The screen shown until a character is loaded: pick a mod folder, or scaffold a new one. */
export default function OpenScreen({
  recalled, choices, asking, creating, empty, problem,
  onPick, onStartNew, onOpen, onCreate, setAsking, setCreating, setEmpty,
}: Props) {
  return (
    <main className="mx-auto max-w-2xl p-8">
      <h1 className="text-xl font-semibold">Motions: sprite motion editor</h1>
      <p className="mt-2 text-sm text-muted-foreground">
        Open your mod folder. The editor finds the characters in it the same way the plugin
        does. Nothing is written until you save.
      </p>

      <div className="mt-6 flex flex-wrap items-center gap-2">
        <Button size="lg" onClick={onPick}>
          <FolderOpen className="size-4" />
          Open mod folder
        </Button>
        <Button size="lg" variant="outline" onClick={onStartNew}>
          <Plus className="size-4" />
          Start a new mod
        </Button>
      </div>

      <p className="mt-3 text-xs text-muted-foreground">
        A mod folder, a <code>motion_appearances/</code> or <code>custom_motions/</code> folder,
        or a single character folder all work. Starting a new one asks for an empty
        folder; the file picker can make you one.
      </p>

      {creating && (
        <NewCharacter
          modName={creating.handle.name}
          insideModsRoot={creating.modsRoot}
          onCancel={() => setCreating(null)}
          onCreate={(name, mode, modFolder) => onCreate(creating.handle, name, mode, modFolder)}
        />
      )}

      {/* The mods folder is refused rather than worked with: a character created in it would
          land at mods/motion_appearances/<Name>/, which the plugin reads as a mod named
          "motion_appearances" holding nothing. It loads, finds no character, and says nothing.
          A mod folder has to exist first, so that is what the button offers to make. */}
      {empty?.modsRoot && !creating && (
        <Alert className="mt-6">
          <TriangleAlert />
          <AlertTitle>{empty.handle.name} is the mods folder, not a mod</AlertTitle>
          <AlertDescription>
            <p>
              Every folder inside it is a separate mod, and the plugin loads them one by one.
              A character put directly in here would sit outside all of them and never load,
              so this is as far as it goes.
            </p>
            <p>Name a mod and it gets made inside, with the character in it.</p>
          </AlertDescription>
          <AlertAction>
            <Button size="sm" onClick={() => { setEmpty(null); setCreating(empty) }}>
              <Plus className="size-3.5" />
              New mod here
            </Button>
            <Button size="sm" variant="ghost" onClick={() => { setEmpty(null); onPick() }}>
              Pick a mod instead
            </Button>
          </AlertAction>
        </Alert>
      )}

      {empty && !empty.modsRoot && !creating && (
        <div className="mt-6 rounded-lg border p-4">
          <p className="text-sm font-medium">{empty.handle.name} holds no character yet.</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Nothing in it that the plugin would load: no <code>motion_appearances/</code> or{' '}
            <code>custom_motions/</code>, and no <code>motions/</code>, <code>.bundle</code> or
            skill JSON of its own. That is exactly what a new mod folder looks like, or you
            picked the wrong folder.
          </p>
          <div className="mt-3 flex gap-2">
            <Button size="sm" onClick={() => { setEmpty(null); setCreating(empty) }}>
              <Plus className="size-3.5" />
              Create a character here
            </Button>
            <Button size="sm" variant="ghost" onClick={() => { setEmpty(null); onPick() }}>
              Pick a different folder
            </Button>
          </div>
        </div>
      )}

      {/* The only place the two kinds are ever put to the author, and only for a folder with
          nothing above or inside it to settle the question. */}
      {asking && (
        <div className="mt-6 rounded-lg border p-4">
          <p className="text-sm font-medium">Is {asking.path} a new character, or an override?</p>
          <p className="mt-1 text-xs text-muted-foreground">
            Opened on its own, so there is no <code>motion_appearances/</code> or{' '}
            <code>custom_motions/</code> above it to say, and no <code>appearance.json</code> in
            it yet. This only changes what the editor tells you. Pick either and reopen from
            the mod folder if it turns out wrong.
          </p>
          <div className="mt-3 flex gap-2">
            <Button variant="outline" size="sm"
                    onClick={() => { setAsking(null); onOpen(asking.handle, 'appearance') }}>
              A character of my own
            </Button>
            <Button variant="outline" size="sm"
                    onClick={() => { setAsking(null); onOpen(asking.handle, 'override') }}>
              An override of an existing one
            </Button>
          </div>
        </div>
      )}

      {/* Only when the pick was ambiguous. A single hit opened straight away and never lands
          here; the list is still kept, for the switcher in the header. */}
      {known(choices).length > 1 && (
        <div className="mt-6 rounded-lg border p-4">
          <p className="text-sm font-medium">
            That mod holds {known(choices).length} characters. Which one?
          </p>
          <div className="mt-3 flex flex-col items-start gap-1">
            {known(choices).map((c) => (
              <Button key={c.path} variant="ghost" size="sm" className="font-mono text-xs"
                      onClick={() => onOpen(c.handle, c.mode)}>
                <FolderOpen className="size-3.5 opacity-60" />
                {c.path}
              </Button>
            ))}
          </div>
        </div>
      )}

      {recalled && (
        <Button variant="ghost" size="sm" className="mt-4"
                onClick={() => onOpen(recalled.handle, recalled.mode)}>
          <RotateCcw className="size-3.5 opacity-60" />
          Reopen {recalled.handle.name}
        </Button>
      )}

      {problem && (
        <Alert variant="destructive" className="mt-4">
          <TriangleAlert />
          <AlertDescription className="whitespace-pre-line">{problem}</AlertDescription>
        </Alert>
      )}
    </main>
  )
}

/** Firefox and Safari have no showDirectoryPicker, and the editor writes straight to disk. */
export function UnsupportedScreen() {
  return (
    <main className="mx-auto max-w-xl p-8">
      <h1 className="text-xl font-semibold">Motions: sprite motion editor</h1>
      <Alert className="mt-4">
        <TriangleAlert />
        <AlertTitle>This editor needs Chrome or Edge</AlertTitle>
        <AlertDescription>
          It writes straight into your mod folder, and Firefox and Safari cannot do that yet.
        </AlertDescription>
      </Alert>
    </main>
  )
}
