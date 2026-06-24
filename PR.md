fix: wooden trunk recognition, multiblock sorting, and container-group editor

## Summary

- **Wooden trunk recognition** — trunks report inventory class `"trunk"` at runtime (not `"chest"` as the JSON implies); added `"trunk"` to default `SupportedInventoryClasses` and its own default group so trunk networks sort and distribute independently of chests
- **Multiblock filler sorting** — clicking the filler half of a trunk now hooks the inventory correctly; root causes fixed:
  - VS encodes the `multiblock-monolithic-*` offset as *principal→filler*, so the resolution must negate it to walk *filler→principal*
  - candidate resolution now requires a supported inventory class so an adjacent unrelated container (e.g. ground storage) can't steal the lookup
  - candidate resolution requires a match against `SupportedInventoryClasses` (not just `IBlockEntityContainer`) at both the multiblock resolution step and the final hook gate, so unrelated adjacent containers are rejected at both points
- **Collapsed trunk exclusion** — collapsed/ruined trunk variants (`collapsed1`/`collapsed2`/`collapsed3`) are `retrieveOnly` and excluded from sorting
- **Container-group editor** — ConfigLib settings screen now lets players add/remove/reorder container groups; groups are encoded over the wire via `ContainerGroupCodec` (tested)
- **Crates as single-type containers** — crates hold only one item type at a time; sorter treats them as sticky and never mixes types across crate slots
- **Backpack re-hook** — when backpack sorting is toggled on, all currently-online players get hooked immediately without needing to reconnect
- **Config UI save fix** — saving from the ConfigLib screen no longer resets fields to defaults

## Test plan

- [ ] Place a wooden trunk, click the **filler half** first — confirm it sorts on close without ever clicking the main half
- [ ] Collapsed trunk in same room — confirm it is untouched
- [ ] Two trunks + two chests in same room — confirm trunks stay in trunk network, chests in chest network
- [ ] Edit container groups via ConfigLib screen — confirm changes persist after server restart
- [ ] Crates with mixed items — confirm no cross-type bleed
- [ ] Run unit tests: `dotnet test`
