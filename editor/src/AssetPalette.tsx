import { Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { LoadedAsset, LoadedSound } from './fs'
import { AnimationSpec } from './spec'

interface Props {
  /** Every PNG in the motion folder, keyed by file name. */
  assets: Map<string, LoadedAsset>
  /** Every .wav/.ogg in the motion folder, keyed by file name. */
  sounds: Map<string, LoadedSound>
  /** The edited spec, read only to mark which sprites are not on the timeline yet. */
  spec: AnimationSpec
  onAddSprite: (name: string) => void
  onAddSound: (name: string) => void
}

/** The right-hand strip listing the folder's files, each a button that adds it to the timeline. */
export default function AssetPalette({ assets, sounds, spec, onAddSprite, onAddSound }: Props) {
  return (
    <div className="w-44 shrink-0 overflow-y-auto border-l p-3">
      <div className="text-xs font-medium">Sprites</div>
      <p className="mt-1 text-[10px] leading-tight text-muted-foreground">
        Every PNG in this folder. Adds a frame at the end. Drop PNGs onto the canvas
        to import more.
      </p>
      {/* Used sprites stay in the list. They were filtered out, which read as
          "unused assets" but meant a sprite could never be placed twice: a held pose
          and a there-and-back cycle both reuse the same PNG, and neither was possible
          without hand-editing the JSON. The tag says which are not on the timeline
          yet, which is the part that was actually worth knowing. */}
      <div className="mt-2 flex flex-col gap-1">
        {[...assets.keys()].map((name) => {
          const used = spec.frames.some((f) => f.sprite === name)
          return (
            <Button key={name} variant="outline" size="sm"
                    title={used ? `Adds another frame using ${name}` : `Adds ${name}`}
                    className="w-full justify-start font-mono text-[11px]"
                    onClick={() => onAddSprite(name)}>
              <Plus className="size-3 shrink-0 opacity-50" />
              <span className="truncate">{name}</span>
              {!used && (
                <span className="ml-auto shrink-0 text-[9px] font-sans text-muted-foreground">
                  unused
                </span>
              )}
            </Button>
          )
        })}
      </div>

      {/* Sounds were never filtered by "already used" for the same reason: one sound
          can fire several times in a motion. */}
      <div className="mt-4 text-xs font-medium">Sounds</div>
      <p className="mt-1 text-[10px] leading-tight text-muted-foreground">
        {sounds.size > 0
          ? 'Adds at the frame you are on. Drag it afterwards.'
          : 'Drop a .wav or .ogg onto the canvas to bring one in.'}
      </p>
      <div className="mt-2 flex flex-col gap-1">
        {[...sounds.keys()].map((name) => (
          <Button key={name} variant="outline" size="sm"
                  className="w-full justify-start font-mono text-[11px]"
                  onClick={() => onAddSound(name)}>
            <Plus className="size-3 shrink-0 opacity-50" />
            <span className="truncate">{name}</span>
          </Button>
        ))}
      </div>
    </div>
  )
}
