using System;
using System.Collections.Generic;
using System.Linq;
using Motions;

static class Program
{
    static int failures = 0;

    static void Check(bool condition, string label)
    {
        if (condition) { Console.WriteLine($"  PASS  {label}"); }
        else { failures++; Console.WriteLine($"  FAIL  {label}"); }
    }

    static void Near(double actual, double expected, string label)
        => Check(Math.Abs(actual - expected) < 1e-9, $"{label} (got {actual}, want {expected})");

    static void Main()
    {
        Console.WriteLine("ParseFolderName");
        Check(SpriteMotionSpec.ParseFolderName("S1") == ("S1", 0), "S1 -> (S1, 0)");
        Check(SpriteMotionSpec.ParseFolderName("S1_1") == ("S1", 1), "S1_1 -> (S1, 1)");
        Check(SpriteMotionSpec.ParseFolderName("S1_12") == ("S1", 12), "S1_12 -> (S1, 12)");
        Check(SpriteMotionSpec.ParseFolderName("Idle") == ("Idle", 0), "Idle -> (Idle, 0)");
        // A trailing segment that is not a number is part of the name, not an index.
        Check(SpriteMotionSpec.ParseFolderName("Parrying_Success") == ("Parrying_Success", 0),
              "Parrying_Success keeps its underscore");
        Check(SpriteMotionSpec.ParseFolderName("S1_") == ("S1_", 0), "trailing underscore is not an index");
        Check(SpriteMotionSpec.ParseFolderName("S1_-2") == ("S1_-2", 0), "negative suffix is not an index");
        // Damaged_2 and Damaged_3 are real MOTION_DETAIL values. Without the predicate the
        // _N rule eats them and the loader drops a legitimate motion folder on the floor.
        Func<string, bool> known = n => n == "Damaged" || n == "Damaged_2" || n == "S1";
        Check(SpriteMotionSpec.ParseFolderName("Damaged_2", known) == ("Damaged_2", 0),
              "a known whole name beats the _N rule");
        Check(SpriteMotionSpec.ParseFolderName("S1_1", known) == ("S1", 1),
              "an unknown whole name still splits into a coin index");
        Check(SpriteMotionSpec.ParseFolderName("S1_1") == ("S1", 1),
              "no predicate behaves exactly as before");

        Console.WriteLine("CompareNatural");
        var names = new List<string> { "frame_10.png", "frame_2.png", "frame_1.png" };
        names.Sort(SpriteMotionSpec.CompareNatural);
        Check(names.SequenceEqual(new[] { "frame_1.png", "frame_2.png", "frame_10.png" }),
              "2 sorts before 10");
        var padded = new List<string> { "a_002.png", "a_1.png" };
        padded.Sort(SpriteMotionSpec.CompareNatural);
        Check(padded.SequenceEqual(new[] { "a_1.png", "a_002.png" }), "zero padding does not change order");

        Console.WriteLine("EffectivePpu");
        Near(SpriteMotionSpec.EffectivePpu(100, 1.0), 100, "scale 1 leaves ppu alone");
        Near(SpriteMotionSpec.EffectivePpu(100, 2.0), 50, "scale 2 halves ppu (sprite renders twice as big)");

        Console.WriteLine("Pivot");
        Near(SpriteMotionSpec.PivotX(0, 100, 100), 0.5, "no offset is horizontally centred");
        // Vertical anchor is the bottom edge: the transform sits at the character's feet.
        Near(SpriteMotionSpec.PivotY(0, 200, 100), 0.0, "no offset stands on the origin");
        // 100px wide at 100 ppu is 1 world unit, so a 0.5 unit shift is half the sprite.
        Near(SpriteMotionSpec.PivotX(0.5, 100, 100), 0.0, "half-width shift lands the pivot on the edge");
        Near(SpriteMotionSpec.PivotX(1.0, 100, 100), -0.5, "pivot outside the rect is legal");
        // 200px tall at 100 ppu is 2 world units; lifting by 0.5 is a quarter of the height.
        Near(SpriteMotionSpec.PivotY(0.5, 200, 100), -0.25, "positive offset lifts the frame off the ground");
        Near(SpriteMotionSpec.PivotY(-0.5, 200, 100), 0.25, "negative offset sinks the frame");

        Console.WriteLine("FrameIndexAt");
        var times = new double[] { 0.0, 0.1, 0.2 };
        Check(SpriteMotionSpec.FrameIndexAt(times, 0.0) == 0, "t at first boundary");
        Check(SpriteMotionSpec.FrameIndexAt(times, 0.05) == 0, "t between frames holds the earlier one");
        Check(SpriteMotionSpec.FrameIndexAt(times, 0.1) == 1, "t exactly on a boundary takes the new frame");
        Check(SpriteMotionSpec.FrameIndexAt(times, 99.0) == 2, "past the end holds the last frame");
        Check(SpriteMotionSpec.FrameIndexAt(times, -1.0) == 0, "before the start clamps to the first frame");
        Check(SpriteMotionSpec.FrameIndexAt(new double[0], 0.5) == -1, "empty is -1");
        // A first frame that starts late must still show something, or the character is invisible.
        Check(SpriteMotionSpec.FrameIndexAt(new double[] { 0.5, 1.0 }, 0.0) == 0, "late first frame still clamps");

        Console.WriteLine("DefaultSpec");
        var def = SpriteMotionSpec.DefaultSpec(new[] { "b_10.png", "b_2.png", "b_1.png" });
        Check(def.frames.Count == 3, "one frame per png");
        Check(def.frames[0].sprite == "b_1.png", "natural order applied");
        Check(def.frames[2].sprite == "b_10.png", "10 comes last");
        Near(def.frames[1].t, 1.0 / SpriteMotionSpec.DefaultFps, "frames evenly spaced at DefaultFps");
        Near(def.duration, 3.0 / SpriteMotionSpec.DefaultFps, "duration covers every frame");
        Near(def.ppu, SpriteMotionSpec.DefaultPpu, "default ppu");
        Check(def.frames[0].scale == 1.0, "default scale is 1");

        Console.WriteLine("Parse");
        var ok = SpriteMotionSpec.Parse(
            "{\"duration\":1.2,\"ppu\":50,\"frames\":[{\"t\":0,\"sprite\":\"a.png\",\"offset\":[0.1,0.2]}]," +
            "\"sfx\":[{\"t\":0.25,\"file\":\"s.wav\"}]}", out string err);
        Check(ok != null && err == null, "valid json parses");
        Near(ok.duration, 1.2, "duration read");
        Near(ok.ppu, 50, "ppu read");
        Near(ok.frames[0].offset[0], 0.1, "offset x read");
        Check(ok.frames[0].scale == 1.0, "omitted scale defaults to 1");
        Check(ok.sfx[0].file == "s.wav", "sfx read");
        Near(ok.sfx[0].clipIn, 0.0, "omitted clipIn defaults to 0");

        // An omitted ppu must fall back to the measured default, not 0.
        var noPpu = SpriteMotionSpec.Parse("{\"duration\":1.0,\"frames\":[{\"t\":0,\"sprite\":\"a.png\"}]}", out _);
        Near(noPpu.ppu, SpriteMotionSpec.DefaultPpu, "omitted ppu defaults");
        Check(noPpu.frames[0].offset != null && noPpu.frames[0].offset.Length == 2, "omitted offset becomes [0,0]");
        Check(noPpu.sfx != null && noPpu.sfx.Count == 0, "omitted sfx becomes an empty list");

        // Trailing commas and comments are tolerated, matching TimelineBuilder.JsonOptions.
        Check(SpriteMotionSpec.Parse("{\"duration\":1.0,\"frames\":[{\"t\":0,\"sprite\":\"a.png\"},],}", out _) != null,
              "trailing commas tolerated");

        Check(SpriteMotionSpec.Parse("{ not json", out string e1) == null && e1 != null,
              "malformed json returns null and a reason");
        Check(SpriteMotionSpec.Parse("{\"duration\":1.0}", out string e2) == null && e2 != null,
              "no frames returns null and a reason");
        Check(SpriteMotionSpec.Parse("{\"duration\":1.0,\"frames\":[]}", out string e3) == null && e3 != null,
              "empty frames returns null and a reason");
        Check(SpriteMotionSpec.Parse("{\"duration\":0,\"frames\":[{\"t\":0,\"sprite\":\"a.png\"}]}", out string e4) == null && e4 != null,
              "zero duration returns null and a reason");
        Check(SpriteMotionSpec.Parse("{\"duration\":1.0,\"frames\":[{\"t\":0}]}", out string e5) == null && e5 != null,
              "frame with no sprite returns null and a reason");

        // Frames given out of order are sorted, not rejected.
        var unsorted = SpriteMotionSpec.Parse(
            "{\"duration\":1.0,\"frames\":[{\"t\":0.5,\"sprite\":\"b.png\"},{\"t\":0.0,\"sprite\":\"a.png\"}]}", out _);
        Check(unsorted.frames[0].sprite == "a.png", "frames sorted by t");

        Console.WriteLine("PropSpec.TargetCount");
        var fixedEntry = new PropEntry { count = 3 };
        Check(PropSpec.TargetCount(fixedEntry, false, 0) == 3, "no keyword ignores the gate");
        var gated = new PropEntry { keyword = "Bleed", stackThres = 1, maxCount = 5 };
        Check(PropSpec.TargetCount(gated, false, 9) == 0, "a failing gate shows nothing");
        Check(PropSpec.TargetCount(gated, true, 3) == 3, "one instance per stack");
        Check(PropSpec.TargetCount(gated, true, 99) == 5, "stack clamps to maxCount");
        // A typo'd maxCount must not be able to spawn hundreds of objects.
        Check(PropSpec.TargetCount(new PropEntry { count = 500 }, false, 0) == PropSpec.HardCountCeiling,
              "fixed count clamps to the hard ceiling");
        Check(PropSpec.TargetCount(new PropEntry { keyword = "Bleed", maxCount = 9999 }, true, 500)
              == PropSpec.HardCountCeiling, "maxCount cannot exceed the hard ceiling");

        Console.WriteLine("PropSpec.SlotAngleDegrees");
        Near(PropSpec.SlotAngleDegrees(0, 3, 0, 0, 0), 0, "slot 0 sits at phase");
        Near(PropSpec.SlotAngleDegrees(1, 3, 0, 0, 0), 120, "3 slots are 120 apart");
        Near(PropSpec.SlotAngleDegrees(2, 3, 0, 0, 0), 240, "third slot");
        Near(PropSpec.SlotAngleDegrees(0, 1, 45, 0, 0), 45, "a single slot sits exactly at phase");
        Near(PropSpec.SlotAngleDegrees(0, 3, 0, 90, 2), 180, "speed advances the whole ring");
        // Respacing: the same slot index moves when the count changes.
        Near(PropSpec.SlotAngleDegrees(1, 2, 0, 0, 0), 180, "slot 1 of 2 is opposite, not 120 over");
        Check(PropSpec.SlotAngleDegrees(0, 0, 30, 0, 0) == 30, "zero count does not divide by zero");

        Console.WriteLine("PropSpec.BobOffset");
        Near(PropSpec.BobOffset(0, 2, 0.5, 0, 1), 0, "zero amplitude does not bob");
        Near(PropSpec.BobOffset(1.0, 0, 0.5, 0, 1), 0, "zero period does not bob");
        Near(PropSpec.BobOffset(1.0, 4.0, 1.0, 0, 1), 1.0, "quarter period is full amplitude");
        Near(PropSpec.BobOffset(1.0, 4.0, 0.0, 0, 1), 0.0, "t zero starts at rest");

        Console.WriteLine("PropSpec.SlotPosition");
        var ring = new PropEntry { pos = new double[] { 1, 2, 3 }, radius = 2, count = 1 };
        PropSpec.SlotPosition(ring, 0, 1, 0, out double px, out double py, out double pz);
        Near(px, 3, "slot 0 sits radius along +x from the ring centre");
        Near(py, 2, "no bob leaves y at the ring centre");
        Near(pz, 3, "z passes through untouched");
        var noPos = new PropEntry { radius = 1, count = 1 };
        PropSpec.SlotPosition(noPos, 0, 1, 0, out double nx, out double ny, out double nz);
        Near(nx, 1, "an omitted pos is treated as the origin");
        Near(ny, 0, "an omitted pos is treated as the origin (y)");
        Near(nz, 0, "an omitted pos is treated as the origin (z)");

        Console.WriteLine("PropSpec.ActionProgress");
        Near(PropSpec.ActionProgress(0.1, 0.2, 0.6), 0.0, "before the start is 0");
        Near(PropSpec.ActionProgress(0.4, 0.2, 0.6), 0.5, "halfway is 0.5");
        Near(PropSpec.ActionProgress(0.9, 0.2, 0.6), 1.0, "after the end is 1");
        // An author writing arrive <= start must not divide by zero.
        Near(PropSpec.ActionProgress(0.5, 0.5, 0.5), 1.0, "zero-length action is complete at its time");
        Near(PropSpec.ActionProgress(0.4, 0.5, 0.5), 0.0, "zero-length action has not started before its time");
        Near(PropSpec.ActionProgress(0.7, 0.6, 0.2), 1.0, "end before start does not go negative");

        Console.WriteLine("PropSpec.Ease");
        Near(PropSpec.Ease(0.5, null), 0.5, "no name is linear");
        Near(PropSpec.Ease(0.5, "Nonsense"), 0.5, "an unknown name is linear");
        Near(PropSpec.Ease(0.5, "InQuad"), 0.25, "InQuad starts slow");
        Near(PropSpec.Ease(0.5, "outquad"), 0.75, "OutQuad is case-insensitive and ends slow");
        Near(PropSpec.Ease(0.0, "InOutQuad"), 0.0, "every ease starts at 0");
        Near(PropSpec.Ease(1.0, "InOutQuad"), 1.0, "every ease ends at 1");
        Near(PropSpec.Ease(1.5, "InQuad"), 1.0, "progress past 1 clamps");
        // Ease compares with OrdinalIgnoreCase instead of lowercasing the name (it ran once per
        // struck slot per frame and allocated a string each time). Every casing must still land.
        Near(PropSpec.Ease(0.5, "INQUAD"), 0.25, "InQuad in upper case");
        Near(PropSpec.Ease(0.5, "inquad"), 0.25, "InQuad in lower case");
        Near(PropSpec.Ease(0.5, "InQuAd"), 0.25, "InQuad in mixed case");
        Near(PropSpec.Ease(0.5, "OUTQUAD"), 0.75, "OutQuad in upper case");
        Near(PropSpec.Ease(0.25, "InOutQuad"), 0.125, "InOutQuad below the midpoint");
        Near(PropSpec.Ease(0.75, "inOUTquad"), 0.875, "InOutQuad above the midpoint, mixed case");
        Near(PropSpec.Ease(0.5, ""), 0.5, "an empty name is linear");
        // A prefix or a near-miss must not match: OrdinalIgnoreCase is a whole-string compare.
        Near(PropSpec.Ease(0.5, "in"), 0.5, "a prefix of a real name is linear");
        Near(PropSpec.Ease(0.5, "InQuad "), 0.5, "a trailing space is not the same name");
        Near(PropSpec.Ease(0.5, "InCubic"), 0.5, "a DOTween ease name props do not have is linear");

        Console.WriteLine("PropSpec rounds");
        Check(PropSpec.InitialRounds(0) == -1, "rounds 0 becomes the immortal sentinel");
        Check(PropSpec.InitialRounds(-4) == -1, "a negative authored value is also immortal");
        Check(PropSpec.InitialRounds(2) == 2, "a positive value passes through");
        Check(PropSpec.TickRounds(-1) == -1, "immortal never ticks down");
        Check(!PropSpec.Expired(-1), "immortal never expires");
        // rounds 2 survives one round start and dies at the second.
        int r = PropSpec.InitialRounds(2);
        r = PropSpec.TickRounds(r);
        Check(!PropSpec.Expired(r), "rounds 2 survives the first round start");
        r = PropSpec.TickRounds(r);
        Check(PropSpec.Expired(r), "rounds 2 expires at the second round start");
        Check(PropSpec.TickRounds(0) == 0, "an expired value stays expired");

        Console.WriteLine("PropSpec.Validate");
        Check(PropSpec.Validate(null) != null, "a null entry is rejected");
        Check(PropSpec.Validate(new PropEntry()) != null, "an entry with no folder and no prefab is rejected");
        Check(PropSpec.Validate(new PropEntry { folder = "props/knife" }) == null, "a folder entry is valid");
        Check(PropSpec.Validate(new PropEntry { prefab = "KnifeVFX" }) == null, "a prefab entry is valid");
        Check(PropSpec.Validate(new PropEntry { folder = "f", anchor = "sideways" }) != null,
              "an unknown anchor is rejected");
        // A strike moves something out of a ring slot and back; a world prop has no ring.
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", anchor = "world",
                  actions = new[] { new PropAction { @do = "strike", motion = "S1" } } }) != null,
              "a strike on a world-anchored entry is rejected");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", actions = new[] { new PropAction { @do = "strike" } } }) != null,
              "an action with no motion name is rejected");

        // A target ring follows a keyword, not the motion clock, so an action on one would be
        // silently ignored, the kind of wrong an author only finds by staring at a battle.
        Check(PropSpec.Validate(new PropEntry { folder = "f", anchor = "target" }) == null,
              "a target-anchored entry is valid");

        // A slot string the runtime cannot parse matches no slot ever, so the action loads, finds
        // its motion, resolves its target and then silently moves nothing at all.
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", actions = new[] { new PropAction { motion = "S1", slot = "2" } } }) == null,
              "a numeric slot index is valid");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", actions = new[] { new PropAction { motion = "S1", slot = "First" } } }) != null,
              "an unparseable slot is rejected rather than silently matching nothing");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", anchor = "target",
                  actions = new[] { new PropAction { @do = "strike", motion = "S1" } } }) != null,
              "a target-anchored entry with actions is rejected");

        Console.WriteLine("PropSpec.Normalize");
        var messy = new PropEntry {
            folder = "f",
            actions = new[] { new PropAction { @do = "strike", motion = "S1", start = -3, arrive = 55, at = 2 } }
        };
        PropSpec.Normalize(messy);
        Check(messy.pos != null && messy.pos.Length == 3, "an omitted pos becomes [0,0,0]");
        Near(messy.actions[0].start, 0.0, "a negative start clamps to 0");
        Near(messy.actions[0].arrive, 1.0, "an arrive past the end clamps to 1");
        Near(messy.actions[0].at, 1.0, "an at past the end clamps to 1");
        // An invisible prop is indistinguishable from a broken one, so a zeroed scale is read as
        // "leave it alone" rather than multiplied through.
        Near(messy.scale, 1.0, "an omitted scale stays 1");
        var flat = new PropEntry { folder = "f", scale = 0 };
        PropSpec.Normalize(flat);
        Near(flat.scale, 1.0, "a zero scale falls back to 1 instead of vanishing the prop");
        var big = new PropEntry { folder = "f", scale = 6.5 };
        PropSpec.Normalize(big);
        Near(big.scale, 6.5, "an authored scale is left alone");

        var noActions = new PropEntry { folder = "f" };
        PropSpec.Normalize(noActions);
        Check(noActions.actions != null && noActions.actions.Length == 0, "omitted actions becomes an empty array");

        // A hand-authored "actions": [{...}, null] is a plausible JSON slip, and Normalize used to
        // strip it. Validate rejects the whole entry on a null action and runs first in the only
        // load path, so the strip was unreachable and these assertions pinned an impossible input.
        // What matters is that the rejection still happens.
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", actions = new[] { new PropAction { @do = "strike", motion = "S1" }, null } }) != null,
              "an entry with a null action is rejected before Normalize ever sees it");

        Console.WriteLine("PropSpec.Normalize pads action arrays");
        // entry.pos was padded but action arrays were not, so a two-element offset (which the
        // docs' [x,y,z] table invites) survived into PropRig and threw on [2], killing every
        // prop on the character for the rest of the battle.
        var shortArrays = new PropEntry
        {
            folder = "f",
            actions = new[]
            {
                new PropAction { @do = "strike", motion = "S1", offset = new double[] { 1, 2 },
                                 pos = new double[] { 7 }, fromOffset = new double[0] }
            }
        };
        PropSpec.Normalize(shortArrays);
        var paddedAction = shortArrays.actions[0];
        Check(paddedAction.offset.Length == 3, "a two-element offset is padded to three");
        Near(paddedAction.offset[0], 1, "padding keeps the authored x");
        Near(paddedAction.offset[1], 2, "padding keeps the authored y");
        Near(paddedAction.offset[2], 0, "padding zero-fills the missing z");
        Check(paddedAction.pos.Length == 3, "a one-element pos is padded to three");
        Near(paddedAction.pos[0], 7, "padding keeps the authored value");
        Check(paddedAction.fromOffset.Length == 3, "an empty fromOffset is padded to three");
        // A longer-than-three array must not be truncated into a crash either.
        var longArray = new PropEntry
        {
            folder = "f",
            actions = new[] { new PropAction { @do = "strike", motion = "S1",
                                               offset = new double[] { 1, 2, 3, 4 } } }
        };
        PropSpec.Normalize(longArray);
        Check(longArray.actions[0].offset.Length >= 3, "an over-long offset still indexes safely");

        Console.WriteLine("PropSpec.ArcOffset");
        Near(PropSpec.ArcOffset(2.0, 0.0), 0.0, "an arc starts flat");
        Near(PropSpec.ArcOffset(2.0, 1.0), 0.0, "an arc ends flat");
        Near(PropSpec.ArcOffset(2.0, 0.5), 2.0, "an arc peaks at its authored height mid-flight");
        Near(PropSpec.ArcOffset(-2.0, 0.5), -2.0, "a negative arc dips instead of bowing");
        Near(PropSpec.ArcOffset(0.0, 0.5), 0.0, "no arc is a straight line");

        Console.WriteLine("PropSpec.FaceDegrees");
        // The sprite's own forward is +x, so 0 degrees means pointing right.
        Near(PropSpec.FaceDegrees(1, 0), 0.0, "travelling right needs no rotation");
        Near(PropSpec.FaceDegrees(0, 1), 90.0, "travelling up is a quarter turn");
        Near(PropSpec.FaceDegrees(-1, 0), 180.0, "travelling left is a half turn");
        Near(PropSpec.FaceDegrees(0, 0), 0.0, "a zero vector does not produce a NaN angle");

        // A ring-parked prop keeps aiming at what it orbits: the inward direction is its own ring
        // offset negated, which is the slot's angle turned half a turn. PropRig composes exactly
        // these two calls, so this is the rule it relies on.
        // parkRadius lets a formation hang wider around an enemy than it orbits its caster; 0 has
        // to mean "no opinion", or an entry that never set it would collapse onto the park point.
        var wider = new PropEntry { radius = 1, count = 1 };
        PropSpec.RingOffset(wider, 0, 1, 0, out double wx, out _, 3.0);
        Near(wx, 3.0, "a radius override widens the ring");
        PropSpec.RingOffset(wider, 0, 1, 0, out wx, out _, 0.0);
        Near(wx, 1.0, "a zero override keeps the entry's own radius");

        var aimed = new PropEntry { radius = 2, count = 4 };
        PropSpec.RingOffset(aimed, 0, 4, 0, out double ax, out double ay);
        // Half a turn, whichever way Atan2 signs it: negating a y of 0 gives -0.0, so this one
        // comes back as -180. Same rotation, and Quaternion.Euler does not care.
        Near(Math.Abs(PropSpec.FaceDegrees(-ax, -ay)), 180.0, "a slot at the ring's right edge points back at the centre");
        PropSpec.RingOffset(aimed, 1, 4, 0, out ax, out ay);
        Near(PropSpec.FaceDegrees(-ax, -ay), -90.0, "a slot at the top of the ring points down at the centre");

        Console.WriteLine("PropSpec.WindUpSplit");
        // No wind-up: the whole outbound window travels, and progress passes straight through.
        Near(PropSpec.WindUpSplit(0.5, 0.0, out bool drawing0), 0.5, "no wind-up passes progress through");
        Check(!drawing0, "no wind-up is never in the drawing-back phase");
        // With a 0.4 wind-up slice: the first 40% draws back, the rest travels 0..1.
        Near(PropSpec.WindUpSplit(0.2, 0.4, out bool drawing1), 0.5, "halfway through the pull-back");
        Check(drawing1, "the first slice is the drawing-back phase");
        Near(PropSpec.WindUpSplit(0.4, 0.4, out bool drawing2), 0.0, "the throw starts from a standstill");
        Check(!drawing2, "the boundary belongs to the travel phase");
        Near(PropSpec.WindUpSplit(0.7, 0.4, out bool drawing3), 0.5, "halfway through the travel");
        Near(PropSpec.WindUpSplit(1.0, 0.4, out _), 1.0, "the travel completes");
        // A wind-up that eats the whole window must not divide by zero on the travel half.
        Near(PropSpec.WindUpSplit(1.0, 1.0, out _), 1.0, "a full-window wind-up still completes");

        Console.WriteLine("PropSpec.RingOffset");
        // The ring's shape, independent of where its centre is. SlotPosition is this plus the
        // centre, which is what lets a parked formation reuse the ring maths around a new point
        // instead of growing a parallel copy of it.
        var ringOnly = new PropEntry { pos = new double[] { 1, 2, 3 }, radius = 2, count = 1 };
        PropSpec.RingOffset(ringOnly, 0, 1, 0, out double rx, out double ry);
        Near(rx, 2, "slot 0 sits radius along +x from the centre");
        Near(ry, 0, "no bob leaves the offset flat");
        // Composition must hold exactly, or a parked ring drifts from an orbiting one.
        PropSpec.SlotPosition(ringOnly, 0, 1, 0, out double sx, out double sy, out double sz);
        Near(sx, rx + 1, "SlotPosition is RingOffset plus the centre x");
        Near(sy, ry + 2, "SlotPosition is RingOffset plus the centre y");
        Near(sz, 3, "SlotPosition takes its z entirely from the centre, the ring is drawn flat");
        // A frozen t is what "hold" parks with, so the same t must give the same offset forever.
        var spinning = new PropEntry { radius = 1, count = 3, speed = 90, bob = 0.5, bobPeriod = 2 };
        PropSpec.RingOffset(spinning, 1, 3, 1.75, out double hx, out double hy);
        PropSpec.RingOffset(spinning, 1, 3, 1.75, out double hx2, out double hy2);
        Near(hx, hx2, "a frozen t is stable in x");
        Near(hy, hy2, "a frozen t is stable in y");

        Console.WriteLine("PropSpec.Validate park");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f",
                  actions = new[] { new PropAction { motion = "S1", park = "ring" } } }) == null,
              "a parking strike is valid");
        // Two endings authored for one strike is a guess we refuse to make.
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f",
                  actions = new[] { new PropAction { motion = "S1", park = "ring", returnAt = 0.8 } } }) != null,
              "park with a returnAt is rejected");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f",
                  actions = new[] { new PropAction { motion = "S1", park = "sideways" } } }) != null,
              "an unknown park mode is rejected");
        Check(PropSpec.Validate(new PropEntry {
                  folder = "f", anchor = "world",
                  actions = new[] { new PropAction { @do = "plant", motion = "S1", park = "ring" } } }) != null,
              "park on a plant is rejected");
        Check(PropSpec.Validate(new PropEntry { folder = "f", parkUntil = "forever" }) != null,
              "an unknown parkUntil is rejected");
        Check(PropSpec.Validate(new PropEntry { folder = "f", parkUntil = "round" }) == null,
              "parkUntil round is valid");

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
