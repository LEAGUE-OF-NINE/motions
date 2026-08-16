using System;
using System.Collections.Generic;
using UnityEngine;

namespace Motions;

/// <summary>
/// Ticks placed props. Separate from PropRig because a planted prop has to outlive both the
/// motion that placed it and the character that cast it.
/// </summary>
public class PropWorldDriver : MonoBehaviour
{
    public PropWorldDriver(IntPtr ptr) : base(ptr) { }

    void Update() => PropWorld.Tick(Time.time);
}

/// <summary>
/// Every prop instance standing in the scene rather than orbiting a character. Owns their
/// per-frame animation and their round-based expiry.
/// </summary>
public static class PropWorld
{
    /// <summary>
    /// A placed instance, and the handle callers hold onto. Internal rather than private so PropRig
    /// can name the type: a handle typed as <c>object</c> made a mismatched Remove a silent no-op
    /// instead of a compile error.
    /// </summary>
    internal sealed class Placed
    {
        public string AppearanceID;
        public PropEntry Entry;
        public GameObject Obj;
        public SpriteRenderer Renderer;
        public SpriteMotion Art;
        /// <summary>Last art frame written; see PropLoader.Advance.</summary>
        public int Frame = -1;
        public int RoundsLeft;
        public float Born;
        /// <summary>Where it was placed. A placed prop has no parent to hold its rest position,
        /// so bobbing has to offset from a remembered point rather than from its own transform.</summary>
        public Vector3 Rest;

        /// <summary>The single instance a gated world entry keeps standing, not something a plant
        /// left behind. It belongs to that entry's ring/gated pool, so the planted ceiling must not
        /// count it - otherwise 16 standing plants permanently hide the gated instance.</summary>
        public bool Gated;
    }

    private static readonly List<Placed> _placed = new();

    /// <summary>Entries that already logged a ceiling hit. A plant runs off the motion clock, so
    /// without this the 17th attempt logs every frame of every cast. Keyed on the (appearanceID,
    /// entry) pair the ceiling is counted on: PropEntry alone is shared by every character wearing
    /// the appearance, so one character's log would suppress another's hit on the same entry.</summary>
    private static readonly HashSet<(string AppearanceID, PropEntry Entry)> _ceilingLogged = new();

    private static GameObject _driver;

    /// <summary>
    /// Increments once per round start, and again on Clear so a new battle never looks like the same
    /// round as the last. Read by PropRig; only the change matters, not the absolute value.
    /// </summary>
    public static int RoundToken;
    private static int _lastRoundFrame = -1;
    private static bool _broken;

    /// <summary>
    /// Places one instance at a world position. Returns a handle for callers that need to take it
    /// away again (a gated prop whose gate stops passing); planted props discard it. Refuses past
    /// <see cref="PropSpec.HardCountCeiling"/> instances of one entry, or a repeatable skill cast
    /// twenty times leaves twenty totems standing.
    ///
    /// <paramref name="ceilingHit"/> separates a ceiling null from a creation failure: the ceiling
    /// is transient (space frees as planted instances expire), missing art or prefab is not.
    /// Conflating them into one "give up on this entry" flag permanently suppresses a gated entry
    /// whose plants are merely standing at 16.
    /// </summary>
    internal static Placed Place(string appearanceID, PropEntry entry, Vector3 position, int authoredRounds,
                                 out bool ceilingHit, bool gated = false)
    {
        ceilingHit = false;

        // Per (appearance, entry), not global: one appearance's plants must not starve another's.
        // Two characters wearing it share the pool - the rig has no per-unit identity that outlives
        // it, and plants outlive their caster, so keying on one hands every rebuilt rig a fresh 16.
        // Documented in props.md. Gated instances are excluded both sides: they are the other pool.
        int existing = 0;
        for (int i = 0; i < _placed.Count; i++)
            if (!_placed[i].Gated && _placed[i].Entry == entry && _placed[i].AppearanceID == appearanceID)
                existing++;

        if (!gated && existing >= PropSpec.HardCountCeiling)
        {
            ceilingHit = true;

            // Skipped rather than evicting the oldest: silently replacing an author's standing
            // totem is worse than declining to place the seventeenth.
            if (_ceilingLogged.Add((appearanceID, entry)))
                Logger.LogWarning($"[Props] {appearanceID}: '{entry.folder ?? entry.prefab}' already has " +
                                  $"{PropSpec.HardCountCeiling} placed instances, the ceiling. " +
                                  $"Skipping further placements of it.");
            return null;
        }

        var obj = PropLoader.Build(appearanceID, entry, null, position, out var art, out var renderer);
        if (obj == null) return null;

        var placed = new Placed
        {
            AppearanceID = appearanceID,
            Entry = entry,
            Obj = obj,
            Renderer = renderer,
            Art = art,
            RoundsLeft = PropSpec.InitialRounds(authoredRounds),
            Born = Time.time,
            Rest = position,
            Gated = gated
        };

        _placed.Add(placed);
        EnsureDriver();

        Logger.LogInfo($"[Props] Placed a world prop for {appearanceID} at {position}, " +
                       $"rounds={placed.RoundsLeft}.");
        return placed;
    }

    internal static void Remove(Placed placed)
    {
        if (placed == null) return;

        _placed.Remove(placed);
        if (placed.Obj != null) UnityEngine.Object.Destroy(placed.Obj);
    }

    private static void EnsureDriver()
    {
        if (_driver != null) return;

        // Battle-scene scoped on purpose: no DontDestroyOnLoad, and Clear covers the rest.
        _driver = new GameObject("Motions_PropWorld");
        _driver.AddComponent<PropWorldDriver>();
    }

    /// <summary>
    /// A throw here aborts the loop, so every other placed prop stops animating too, and it would
    /// repeat every frame for the rest of the battle. One log then silence, same shape as PropRig's
    /// per-rig broken flag.
    /// </summary>
    public static void Tick(float now)
    {
        if (_broken) return;

        try
        {
            for (int i = _placed.Count - 1; i >= 0; i--)
            {
                var placed = _placed[i];

                if (placed.Obj == null) { _placed.RemoveAt(i); continue; }

                // Time since it was placed, not absolute Time.time: against the clock a battle has
                // been running, a totem's first frame lands at an arbitrary point of the bob sine
                // and an arbitrary spin angle, so it pops instead of appearing at rest.
                // Placed.Rest is where it was put, and this is the phase that matches it.
                double age = now - placed.Born;

                if (placed.Entry.spin != 0)
                    placed.Obj.transform.localRotation =
                        Quaternion.Euler(0f, 0f, (float)(placed.Entry.spin * age));

                if (placed.Entry.bob != 0 && placed.Entry.bobPeriod > 0)
                {
                    double dy = PropSpec.BobOffset(placed.Entry.bob, placed.Entry.bobPeriod, age, 0, 1);
                    placed.Obj.transform.position = placed.Rest + new Vector3(0f, (float)dy, 0f);
                }

                PropLoader.Advance(placed.Art, placed.Renderer, age, ref placed.Frame);
            }
        }
        catch (Exception ex)
        {
            _broken = true;
            Logger.LogError($"[Props] World tick threw, placed props stop animating this battle: {ex}");
        }
    }

    /// <summary>
    /// Decrements every placed prop's rounds and destroys the ones that hit zero. Called from the
    /// per-unit OnRoundStart postfix, so it guards against running once per unit.
    /// </summary>
    // ponytail: expiry at round start, not round end, and deduped by frame number. Patch a
    // real round-end hook if the one-frame-late despawn is ever visible.
    public static void OnRoundStart()
    {
        if (Time.frameCount == _lastRoundFrame) return;
        _lastRoundFrame = Time.frameCount;

        // Bumped for anyone who needs to know a round turned over without patching the hook twice.
        // PropRig compares it in the poll it already runs, so a parkUntil: "round" entry drops its
        // parked slots without a Harmony patch of its own.
        RoundToken++;

        for (int i = _placed.Count - 1; i >= 0; i--)
        {
            var placed = _placed[i];
            placed.RoundsLeft = PropSpec.TickRounds(placed.RoundsLeft);

            if (!PropSpec.Expired(placed.RoundsLeft)) continue;

            _placed.RemoveAt(i);
            if (placed.Obj != null) UnityEngine.Object.Destroy(placed.Obj);
        }
    }

    public static void Clear()
    {
        foreach (var placed in _placed)
            if (placed.Obj != null) UnityEngine.Object.Destroy(placed.Obj);

        _placed.Clear();
        _ceilingLogged.Clear();

        if (_driver != null) UnityEngine.Object.Destroy(_driver);
        _driver = null;
        _lastRoundFrame = -1;
        _broken = false;
        RoundToken++;
    }
}
