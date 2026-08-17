import { Plus, TriangleAlert } from 'lucide-react'
import CoinTimings from './CoinTimings'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { LoadedSkill } from './fs'
import { MOTION_NAMES, MotionEntry } from './motions'
import { Coin, Skill } from './skill'
import { Marker } from './SkillTimeline'

interface Props {
  entry: MotionEntry
  coin: number
  /** The file as read from disk, or null when this motion has no `<Motion>.json`. */
  file: LoadedSkill | null
  /** The edited document, or null when the file exists but could not be parsed. */
  doc: Skill | null
  /** The animation length of this coin, for the timings ruler. null when it has no folder. */
  duration: number | null
  marker: Marker | null
  onSelectMarker: (m: Marker | null) => void
  onCreateFile: (name: string) => void
  onEditCoin: (patch: (c: Coin) => void) => void
  onAddCoin: () => void
  onRemoveCoin: () => void
}

/**
 * The skill half of the coin on screen, under the sprite timeline rather than behind a tab of its
 * own, on the axis Timeline and SkillTimeline now share: a phase reads against the frame it fires
 * on. Renders whichever of the four states the two files put it in - no file, an unreadable file,
 * a coin, or a coin the file does not have yet.
 */
export default function SkillSection({
  entry, coin, file, doc, duration, marker,
  onSelectMarker, onCreateFile, onEditCoin, onAddCoin, onRemoveCoin,
}: Props) {
  // Only for a name the plugin would actually open: it walks MOTION_DETAIL looking for
  // <name>.json (fs.ts:116-118), so offering to create MyMotion.json offers a file nothing reads.
  if (file === null) {
    if (!MOTION_NAMES.includes(entry.base)) return null
    return (
      <div className="mt-4 rounded-lg border p-4">
        <p className="text-sm font-medium">No <code>{entry.base}.json</code> yet.</p>
        <p className="mt-1 text-xs text-muted-foreground">
          That is where this motion's phases, hit checkers and camera work live. Without one
          the game builds a single coin that hands off at the end of the animation.
        </p>
        <Button size="sm" className="mt-3" onClick={() => onCreateFile(entry.base)}>
          <Plus className="size-3.5" />
          Create {entry.base}.json
        </Button>
      </div>
    )
  }

  // A file the editor cannot parse is shown, never rewritten. Offering an editor over a document
  // it failed to read would mean saving a guess over someone's file.
  if (doc === null) {
    return (
      <Alert variant="destructive" className="mt-4">
        <TriangleAlert />
        <AlertDescription>
          <p><strong>{file.name}</strong> could not be read: {file.error}</p>
          <p>
            It is left exactly as it is on disk, and its timings are not editable here. Fix it
            in a text editor and reopen the character. The sprite frames above are unaffected.
          </p>
        </AlertDescription>
      </Alert>
    )
  }

  if (doc.coins[coin]) {
    return (
      <CoinTimings
        coin={doc.coins[coin]}
        duration={duration}
        warning={file.warning}
        selected={marker}
        onSelect={onSelectMarker}
        onEdit={onEditCoin}
        onRemoveCoin={onRemoveCoin}
      />
    )
  }

  return (
    <div className="mt-4 rounded-lg border p-4">
      <p className="text-sm font-medium">
        No coin {coin + 1} in <code>{file.name}</code>.
      </p>
      <p className="mt-1 text-xs text-muted-foreground">
        {/* TimelineBuilder emits one timeline per coins[] entry, so art past the end of that
            array is never built into anything the game plays. */}
        The game builds one coin per entry in that file, so nothing here plays until it has
        a coin {coin + 1}.
        {coin > doc.coins.length && (
          ` Adding it also creates coins ${doc.coins.length + 1}-${coin}, since the list cannot have a gap.`
        )}
      </p>
      <Button size="sm" className="mt-3" onClick={onAddCoin}>
        <Plus className="size-3.5" />
        Add coin {coin + 1} to {file.name}
      </Button>
    </div>
  )
}
