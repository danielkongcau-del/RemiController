# Remielle recovery status

Generated UTC: 2026-09-04T15:36:38.468473+00:00

Recovery verification: **True**

## Completed recovery

| Domain | Result | Validation |
|---|---|---|
| Mesh | 178/178 authoritative Mesh JSON converted to source-separated GLB | SHA-256, GLB header/chunks, declared length and POSITION accessor vertex counts |
| Animation | 829 source identities / 810 logical clips: 664 full-tier ACL, 9 standalone ACL, 156 native uncompressed | Complete source-scoped JSON/raw/resS, every available sample, float32 NPZ or native YAML, SHA-256 |
| Skeleton | 472 native bones plus 3 Animator hierarchies (origin, passeul, ramiel) | Node counts, local transforms, path CRC32 and SHA-256 |
| Material | 95/95 serialized materials packaged | 410/410 texture identities resolved; 83 source-scoped bindings independently qualified this repair |
| JSON corpus | 54549/54549 active JSON/JSONL/NDJSON records parsed and indexed | Per-file source, authority, size, SHA-256, schema/type/name and classification |

The GLB output preserves vertices, normals, tangents, vertex colors, every observed UV channel, submesh topology, skin weights/indices and inverse bind matrices. Unity left-handed coordinates are converted to glTF right-handed coordinates by an explicit Z reflection and triangle winding reversal. A four-component Unity normal stream is retained as xyz `NORMAL` plus lossless `_UNITY_NORMAL_W`.

The old claim that 58 trailing animation channels had no samples was incorrect: the legacy exporter skipped a second ACL block and the final StreamingInfo reference. All 664 streamed clips now include the complete native resS database. This restores 2,915,554 scalar samples as well as the highest available transform samples. The game-shipped compression remains; no claim is made to recover an uncompressed artist DCC source. Legacy resident-only YAML exports were deleted; only complete native animation products remain.
Current Unity assembly: 27 original-skin meshes, 486 hierarchy nodes, zero unresolved weighted bones, 31 authored material slots. Runtime character/menu/battle LUTs and Wings transmission texture are installed. HoyoToon forward lighting, HairShadow projection, runtime object visibility and live object Mesh identities remain distinct from exact recovered asset data.

Effects, Timeline, environment and gameplay data are not forced into misleading generic 3D formats. Their typed and raw serialized JSON, dependency closures and exact identities are retained and indexed in `recovered/ledger/json_asset_ledger.jsonl`.

## Evidence-limited items

The following historical/runtime boundaries are tracked separately from the completed original-skin assembly. A zero count denotes a resolved item:

| Item | Count | Preserved state | Evidence required to unlock |
|---|---:|---|---|
| `unknown-bone-path-hashes` | 0 | resolved | Historical Feather FX hierarchy; current original-skin assembly has no unresolved weighted bones. |
| `unresolved-material-texture-pointers` | 0 | resolved | All 24 prior gaps now have source-scoped PPtr, native PNG and imported PNG evidence. |
| `optimized-animator-deoptimization` | 34 | serialized-json-retained | Each object's exact external Avatar/hierarchy required by the exporter to deoptimize it. |
| `historical-weatherconfig-fields` | 2 | raw-bytes-retained | A matching historical TypeTree/managed assembly or exact live-object traversal. |
| `runtime-null-timeline-bindings` | 10 | serialized-null-proven | Optional live runtime capture if manager-provided identities are required; serialized assets contain null. |

## Reproduction entry points

- `tools/recover_meshes.py` — rebuild all GLB files from authoritative Mesh JSON.
- `tools/recover_full_animations.py` — rebuild all highest-tier streamed animation packages.
- `tools/recover_standalone_animations.py` — qualify the nine standalone ACL and 156 uncompressed source-scoped clips.
- `tools/recover_failed_animations.py` — shared native ABI helpers only; incomplete legacy exporter removed.
- `tools/recover_hierarchies.py` — rebuild normalized native and Animator transform hierarchies.
- `tools/recover_materials.py` — rebuild source-separated material packages and exact known bindings.
- `tools/build_recovery_ledger.py` — rebuild the whole-corpus JSON ledger.
- `tools/verify_recovered_assets.py` — independently verify every recovery product and emit `metadata/recovery_verification.json`.

Machine-readable status is in `metadata/recovery_summary.json`. Source data and same-name duplicates remain separated by original source identity.
