# Character VFX

Motions has another VFX-like feature; the ability to activate VFX depending on different buff conditions (i.e R Corp. Rodya).

In order to do this, a `CharacterVFX.json` must be created under your appearance folder.

```
custom_motions/
       `insert_appearance_here`/
                   CharacterVFX.json
```

The `.json` file is formatted as such:
```json
{
    "allVFX": [
        {
            "keyword": "Breath",
            "stackThres": 5,
            "turnThres": 0,
            "active": true,
            "vfxName": "gunstinks"
        },
        {
            "keyword": "Breath",
            "stackThres": 10,
            "turnThres": 0,
            "active": false,
            "vfxName": "gunstinks"
        }
    ]
}
```

What do all the effects do? 
- `keyword`: The keyword that will be tracked in order for the VFX to activate.
- `stackThres`: The Stack threshold that's required for the VFX to activate (leave at 0 for none)
- `turnThres`: The Turn threshold that's required for the VFX to activate (leave at 0 for none, turn = Count in code terms)
- `active`: If the VFX will turn ON/OFF when the condition is fulfilled (i.e in the above example, on at 5+ stack but at 10+ stack it turns off)
- `vfxName`: The name of the `.prefab` that houses your VFX to be activated.

The prefab containing the VFX can go into any bundle under the unit's appearance. The VFX priority on entries works on a pure dominance system, with higher thresholds = higher dominance (which is why the above example works). Entries can be removed or added as you please.
