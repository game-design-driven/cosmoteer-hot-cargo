# Part art pass (spec 1057)

Goal: the Computer, the targeted reactor and the Funnel look like vanilla terran parts. Art only: same sizes, same gameplay.

Decisions:
- Hybrid workflow. The rules reference generic vanilla layers (deck tiles, wall frames, the roof light blob); no game file is copied.
- tools/sprites/kit.py draws the own layers: shapes into a height map, shaded like vanilla (top-left light, dark crevices),
  roofs as paint plus holes plus normals (G up, like vanilla) plus glows.
- The tech vault is out of scope: the tech vault rework thread replaces it with a 2x2 part.

Steps:
- [x] Inventory and vanilla study
- [x] kit.py and new part designs
- [x] Rules: deck, walls, machine, roof glows, roof light
- [ ] In-game before and after (Mac runner), iterate
- [ ] docs/part-art.md and skill hot-cargo-part-art
- [ ] Spec 1057, smoke test, PR
