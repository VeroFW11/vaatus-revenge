# Character models

Imported, rigged character models. Today there is one: the player Avatar David picked (painted style B, see
`docs/Art/Character-Sheets/Player-Avatar-Style-B-chosen.webp`), at `Art/Characters/Player/player_avatar.glb`.

**The game works without it.** Without the file the player is the grey-box body, exactly as before.

## Putting the painted Avatar in the game

1. **Unzip the art bundle into the repository folder** (the folder that has `game`, `docs` and `tools` in it). Say yes
   if it asks to merge folders. Check the model landed at
   `game/Assets/_Project/Art/Characters/Player/player_avatar.glb`.
2. **Open the project in Unity.** The first time after this change Unity downloads *glTFast*, the package that reads
   `.glb` files (it's listed in `game/Packages/manifest.json`). That needs the internet and takes a minute. When it
   has finished, `player_avatar` shows in the Project window with a small arrow (click it to see the mesh inside).
3. **Run Vaatu's Revenge > Build Combat Sandbox.** It finds the model and puts it on the player by itself.
   (Already have a sandbox open and don't want to rebuild? Use **Vaatu's Revenge > Use Player Avatar Model** instead,
   then save the scene with Ctrl+S.)
4. **Press Play.**

## How to tell it worked

- After the build, the **Console** says `Player avatar model added to 'Player': ...`.
- In the **Scene view** the painted character stands where the player starts, upright, facing the dummies. Before Play
  it stands in its A-pose (arms angled down) inside the grey shapes; that's normal.
- **In Play mode** the grey shapes are gone and the painted Avatar does every attack, dodge and jump. The Console says
  `Player avatar model on for 'Player': ...` and what it had to fix (for example "scaled 1.012x").
- Hit flashes, the dodge shimmer and the fa jin charge glow light up the whole model.
- In the **Vaatu's Revenge** menu, *Use Player Avatar Model* has a tick next to it.

If something is wrong with the model, the Console shows **one yellow warning** starting `Player avatar model is off`
that says what (for example which bones it couldn't find), and the player stays the grey-box body. Nothing breaks.

## Switching back to the grey-box body

- **Vaatu's Revenge > Use Procedural Body** takes the model off the player in the open scene. Unity remembers the
  choice on your PC, so rebuilding the sandbox keeps the grey-box body until you pick *Use Player Avatar Model* again.
- For a quick look during Play: select **Player**, untick **Skinned Avatar Mirror** in the Inspector. Tick it again to
  bring the model back.

## Adjusting it

Select **Player** and find **Skinned Avatar Mirror** in the Inspector:

- **Height Scale**: 1 makes the model's head exactly as high as the grey-box body's head. Move it a little if the
  model looks too big or small. (Hit ranges don't change: this is only how it looks.)
- **Tint**: how strongly hit flashes and attack glows show on the model.

## Notes for whoever touches this next

- The model never animates itself. The grey-box body still does all the moving (it's just hidden) and every frame
  `SkinnedAvatarMirror` copies its pose onto the model through Unity's Humanoid retargeting. Hit boxes, effects and the
  camera keep following the grey-box bones, which sit almost exactly where the model's limbs are.
- The model's bones are matched by name for arms and legs, and by position for the spine (this model numbers its spine
  `Spine02 > Spine01 > Spine` from the hips up, which name matching would get backwards).
- The animation clip baked into the `.glb` is switched off.
- `.glb` files are stored with Git LFS (see `.gitattributes`). They can't be merged, so tell each other before
  replacing one.
- A different model works the same way if it has a humanoid skeleton (hips, spine, head, both arms and legs) and is
  saved at the same path.
