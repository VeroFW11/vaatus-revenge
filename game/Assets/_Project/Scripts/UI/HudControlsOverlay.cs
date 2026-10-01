using System.Globalization;
using System.Text;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The F1 overlay, two pages. F1 cycles: closed -> page 1 -> page 2 -> closed (CombatHud calls NextPage).
    //   Page 1, "Rhythm & Mixing": how combos work, as button pictures: the five-hit string pressed on the beat, the pause
    //     finisher, launcher and air string, dodging without losing the combo, the dodge strike, switching element mid-string
    //     (the colour-matched layout: the button's colour is the element), MIX, danger sense, then one line per element with
    //     its martial art and its two finishers (names from the move data).
    //   Page 2, "Controls": every binding for gamepad and keyboard + mouse (the controls table in
    //     docs/Prototype/Fire-Combat-Prototype-Spec.md, section 1, and PlayerInputReader), then the sandbox keys.
    // If a binding changes in PlayerInputReader, update the rows here too. The rows are built once (and rebuilt only if a
    // name or the element layout shown in them changes), so drawing allocates nothing.
    public sealed class HudControlsOverlay
    {
        public enum Page { Closed, RhythmAndMixing, Controls }

        const int GameplayRows = 18;
        const int SandboxRows = 8;
        const int PatternRows = 8;
        const int ElementCount = 4;

        static readonly Color PanelColor = new Color(0.035f, 0.035f, 0.045f, 0.92f);
        static readonly Color StripeColor = new Color(1f, 1f, 1f, 0.045f);
        static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.6f);

        // Each element's martial art and how that changes the way it plays (spec section 1). Real-world arts, not names
        // from the show; the element names themselves come from the move data.
        static readonly string[] ElementStyles =
        {
            "Northern Shaolin: fast, steady, relentless. Hits on the beat build Momentum",
            "Tai Chi: slower, forgiving timing, long reach. Pulls them in, heals you, widest parry",
            "Hung Gar: slow, heavy, rooted. Hits through their attacks and holds a real block (hold LB)",
            "Baguazhang: the fastest, many little hits, circles round them. Longest dodge",
        };

        Page page;

        // Page 2.
        readonly string[] actions = new string[GameplayRows];
        readonly string[] gamepad = new string[GameplayRows];
        readonly string[] keyboard = new string[GameplayRows];
        readonly string[] sandboxKeys = new string[SandboxRows];
        readonly string[] sandboxActions = new string[SandboxRows];
        bool built;
        string builtSkill, builtZip, builtHeavy, builtHeal, builtNorth, builtEast, builtLauncher, builtSwitchRow;
        bool builtParryOnly;
        float builtSlowScale = -1f;

        // Page 1.
        readonly HudGlyphText[] patternPad = new HudGlyphText[PatternRows];
        readonly HudGlyphText[] patternKeys = new HudGlyphText[PatternRows];
        readonly string[] patternMeaning = new string[PatternRows];
        readonly HudGlyphText[] elementPad = new HudGlyphText[ElementCount];
        readonly HudGlyphText[] elementKeys = new HudGlyphText[ElementCount];
        readonly string[] elementFinishers = new string[ElementCount];
        readonly ElementMoveSet[] builtSets = new ElementMoveSet[ElementCount];
        readonly string[] builtFinisherNames = new string[ElementCount * 2];
        readonly string[] builtElementNames = new string[ElementCount];
        readonly ElementId[] builtSlots = new ElementId[ElementButtonLayout.SlotCount];
        string builtDangerName;
        bool rhythmBuilt;
        string switchPad = "Hold RB + a face button", switchKeys = "1 - 4";   // page 2's switch row, built with page 1

        public Page CurrentPage => page;
        public bool IsOpen => page != Page.Closed;

        // F1: closed -> Rhythm & Mixing -> Controls -> closed. Returns whether the overlay is now open.
        public bool NextPage()
        {
            page = page == Page.Closed ? Page.RhythmAndMixing : page == Page.RhythmAndMixing ? Page.Controls : Page.Closed;
            return IsOpen;
        }

        public void Close()
        {
            page = Page.Closed;
        }

        // Move names come from data (the move sets and the HUD's settings), so lore names never live in code. Empty names
        // fall back to generic words. moves (the element in hand) may be null (generic words throughout). The other
        // elements are read from the player's loadout. Drawn while open; a host that opens it without NextPage gets page 1.
        public void Draw(HudPainter p, ElementMoveSet moves, string healName, float slowMotionScale, Color accent)
        {
            if (page == Page.Closed) page = Page.RhythmAndMixing;
            PlayerInputReader reader = PlayerInputReader.Instance;
            if (page == Page.RhythmAndMixing) DrawRhythm(p, reader, accent);
            else DrawControls(p, reader, moves, healName, slowMotionScale, accent);
        }

        // ---------------------------------------------------------------- page 1: Rhythm & Mixing

        void DrawRhythm(HudPainter p, PlayerInputReader reader, Color accent)
        {
            EnsureElementRows(reader);
            bool pad = reader == null || reader.UsingGamepad;

            float margin = p.U(20f);
            float padding = p.U(26f);
            float titleHeight = p.U(52f);
            float sectionGap = p.U(14f);
            float rowHeight = p.U(34f);
            float elementRowHeight = p.U(50f);
            float footerHeight = p.U(34f);
            float fixedHeight = padding * 2f + titleHeight + rowHeight * 2f + sectionGap + footerHeight;
            float rowsHeight = PatternRows * rowHeight + ElementCount * elementRowHeight;
            float available = Screen.height - margin * 2f - fixedHeight;
            if (rowsHeight > available)
            {
                float shrink = Mathf.Max(0.5f, available / rowsHeight);
                rowHeight *= shrink;
                elementRowHeight *= shrink;
                rowsHeight = PatternRows * rowHeight + ElementCount * elementRowHeight;
            }

            float width = Mathf.Min(p.U(1180f), Screen.width - margin * 2f);
            float height = fixedHeight + rowsHeight;
            Rect panel = PanelRect(p, width, height, margin);
            float x = panel.x + padding;
            float inner = width - padding * 2f;
            float y = panel.y + padding;
            p.Text(new Rect(x, y, inner, titleHeight * 0.7f), "Rhythm & Mixing", p.Heading, Color.white);
            p.Text(new Rect(x, y, inner, titleHeight * 0.7f), "1 / 2   F1: controls", p.SmallRight, DimTextColor);
            y += titleHeight;

            float patternWidth = inner * 0.38f;
            float glyphLine = rowHeight * 0.72f;
            y = SectionHeader(p, x, y, inner, rowHeight, "Combos", accent);
            for (int i = 0; i < PatternRows; i++)
            {
                if (i % 2 == 0) p.Fill(new Rect(x, y, inner, rowHeight), StripeColor);
                HudGlyphText glyphs = pad ? patternPad[i] : patternKeys[i];
                glyphs.Draw(p, new Rect(x + p.U(6f), y + (rowHeight - glyphLine) * 0.5f, patternWidth, glyphLine), pad);
                float textTop = Mathf.Max(0f, (rowHeight - p.Small.fontSize * 1.25f) * 0.5f);
                p.Text(new Rect(x + patternWidth, y + textTop, inner - patternWidth, rowHeight), patternMeaning[i], p.Small, Color.white);
                y += rowHeight;
            }

            y += sectionGap;
            y = SectionHeader(p, x, y, inner, rowHeight, "The four elements (hold RB + the button of the same colour)", accent);
            float elementGlyphLine = Mathf.Min(glyphLine, elementRowHeight * 0.5f);
            float line = elementRowHeight * 0.5f;
            for (int i = 0; i < ElementCount; i++)
            {
                if (i % 2 == 0) p.Fill(new Rect(x, y, inner, elementRowHeight), StripeColor);
                HudGlyphText glyphs = pad ? elementPad[i] : elementKeys[i];
                glyphs.Draw(p, new Rect(x + p.U(6f), y + (elementRowHeight - elementGlyphLine) * 0.5f, patternWidth, elementGlyphLine), pad);
                p.Text(new Rect(x + patternWidth, y + p.U(3f), inner - patternWidth, line), ElementStyles[i], p.Small, Color.white);
                p.Text(new Rect(x + patternWidth, y + line, inner - patternWidth, line), elementFinishers[i], p.Small, DimTextColor);
                y += elementRowHeight;
            }

            p.Text(new Rect(x, panel.yMax - padding - footerHeight * 0.6f, inner, footerHeight * 0.6f),
                "New to it? Press View (F7) in the sandbox for the 11-step tutorial.", p.SmallCenter, DimTextColor);
        }

        float SectionHeader(HudPainter p, float x, float y, float width, float rowHeight, string title, Color accent)
        {
            float textTop = Mathf.Max(0f, (rowHeight - p.Body.fontSize * 1.25f) * 0.5f);
            p.Text(new Rect(x, y + textTop, width, rowHeight), title, p.Body, accent);
            y += rowHeight;
            p.Fill(new Rect(x, y - 1f, width, 1f), DividerColor);
            return y;
        }

        // Page 1's rows and page 2's switch row: they name the elements (from the player's loadout) and their buttons (from
        // the reader's layout), so they're rebuilt when either changes.
        void EnsureElementRows(PlayerInputReader reader)
        {
            PlayerController player = PlayerController.Instance;
            PlayerCombatModel model = player != null ? player.Model : null;
            ElementLoadout loadout = model != null ? model.Loadout : null;
            ElementButtonLayout layout = reader != null ? reader.ElementLayout : null;
            string dangerName = model != null && model.Tuning.DangerSense != null ? model.Tuning.DangerSense.DisplayName : "";
            if (rhythmBuilt && !RhythmRowsChanged(loadout, layout, dangerName)) return;
            rhythmBuilt = true;
            builtDangerName = dangerName;
            for (int s = 0; s < builtSlots.Length; s++) builtSlots[s] = layout != null ? layout.PadSlot(s) : ElementId.None;

            string sense = string.IsNullOrEmpty(dangerName) ? "Danger sense" : dangerName;
            var chords = new StringBuilder();
            var keys = new StringBuilder();
            var padRow = new StringBuilder("Hold RB + ");
            var keyRow = new StringBuilder();
            for (int i = 0; i < ElementCount; i++)
            {
                ElementId element = ElementId.Fire + i;
                ElementMoveSet set = loadout != null ? loadout.Get(element) : null;
                builtSets[i] = set;
                string name = set != null && !string.IsNullOrEmpty(set.DisplayName) ? set.DisplayName : element.ToString();
                builtElementNames[i] = set != null ? set.DisplayName : null;
                int slot = layout != null ? layout.PadSlotOf(element) : -1;
                string chord = slot < 0 ? "{RB}" : "{RB}+{" + FaceName(slot) + "}";
                string key = KeyOf(layout, element);
                elementPad[i] = HudGlyphText.Parse(chord + " " + name);
                elementKeys[i] = HudGlyphText.Parse(key + "  " + name);
                Join(chords, " ", chord);
                Join(keys, "  ", key);
                if (slot >= 0) Join(padRow, padRow.Length > "Hold RB + ".Length ? ", " : "", FaceName(slot) + " " + name);
                if (key != "-") Join(keyRow, ", ", key + " " + name);

                string finisher = LastName(set != null ? set.LightChain : null);
                string pause = LastName(set != null ? set.PauseChain : null);
                builtFinisherNames[i * 2] = finisher;
                builtFinisherNames[i * 2 + 1] = pause;
                elementFinishers[i] = "Finisher: " + (string.IsNullOrEmpty(finisher) ? "the 5th hit" : finisher)
                                      + "   ·   Pause finisher: " + (string.IsNullOrEmpty(pause) ? "X X (wait) X" : pause);
            }

            switchPad = layout != null ? padRow.ToString() : "Hold RB + a face button";
            switchKeys = keyRow.Length > 0 ? keyRow.ToString() : "1 - 4";

            int r = 0;
            Pattern(ref r, "{X} · {X} · {X} · {X} · {X}",
                "The string: press as each hit lands (flash, chime, gold ring) for faster, harder hits. Mashing is slow");
            Pattern(ref r, "{X} · {X} · (wait) · {X}", "Pause finisher: two hits, a short breath, one more. A different ending");
            Pattern(ref r, "hold {X}, then {X} · {X} · {X}", "Launcher throws them up and you follow; then the air string");
            Pattern(ref r, "{B} toward  /  {B} away", "Dodge: slip in close / evade out of reach. Your combo keeps going");
            Pattern(ref r, "{B}, then {X} late", "Dodge strike: a counter that dashes back in and counts as the next hit");
            Pattern(ref r, chords.ToString(), keys.ToString(),
                "Switch element. Mid-string it's a switch strike: the next hit is in that element, and harder");
            Pattern(ref r, "MIX", "More elements landing in one combo: 2 hit harder, 3 launch on the finisher, 4 break guard");
            Pattern(ref r, "gold: {LB}   red: {B}", sense + ": the mark above you. Gold = parry, red = dodge. Press when it turns white");
        }

        bool RhythmRowsChanged(ElementLoadout loadout, ElementButtonLayout layout, string dangerName)
        {
            if (!ReferenceEquals(dangerName, builtDangerName)) return true;
            for (int s = 0; s < builtSlots.Length; s++)
            {
                if (builtSlots[s] != (layout != null ? layout.PadSlot(s) : ElementId.None)) return true;
            }
            for (int i = 0; i < ElementCount; i++)
            {
                ElementMoveSet set = loadout != null ? loadout.Get(ElementId.Fire + i) : null;
                if (!ReferenceEquals(set, builtSets[i])) return true;
                if (set == null) continue;
                // Names edited in the Inspector are new string objects: a reference check is enough (and allocates nothing).
                if (!ReferenceEquals(set.DisplayName, builtElementNames[i])
                    || !ReferenceEquals(LastName(set.LightChain), builtFinisherNames[i * 2])
                    || !ReferenceEquals(LastName(set.PauseChain), builtFinisherNames[i * 2 + 1])) return true;
            }
            return false;
        }

        void Pattern(ref int index, string glyphs, string meaning)
        {
            Pattern(ref index, glyphs, glyphs, meaning);
        }

        void Pattern(ref int index, string padGlyphs, string keyGlyphs, string meaning)
        {
            if (index >= PatternRows) return;
            patternPad[index] = HudGlyphText.Parse(padGlyphs);
            patternKeys[index] = HudGlyphText.Parse(keyGlyphs);
            patternMeaning[index] = meaning;
            index++;
        }

        static void Join(StringBuilder text, string separator, string item)
        {
            if (text.Length > 0) text.Append(separator);
            text.Append(item);
        }

        static string KeyOf(ElementButtonLayout layout, ElementId element)
        {
            for (int i = 0; i < ElementButtonLayout.SlotCount; i++)
            {
                if (layout != null && layout.KeySlot(i) == element) return (i + 1).ToString(CultureInfo.InvariantCulture);
            }
            return "-";
        }

        // Face-button slots in the reader's order: Up (Y), Right (B), Down (A), Left (X). With the colour-matched layout that
        // reads Y yellow Air, B red Fire, A green Earth, X blue Water.
        static string FaceName(int slot)
        {
            switch (slot)
            {
                case 0: return "Y";
                case 1: return "B";
                case 2: return "A";
                default: return "X";
            }
        }

        static string LastName(MoveData[] chain)
        {
            if (chain == null || chain.Length == 0 || chain[chain.Length - 1] == null) return null;
            return chain[chain.Length - 1].DisplayName;
        }

        static Rect PanelRect(HudPainter p, float width, float height, float margin)
        {
            var panel = new Rect((Screen.width - width) * 0.5f, Mathf.Max(margin, (Screen.height - height) * 0.5f), width, height);
            p.Fill(panel, PanelColor);
            p.Outline(panel, Mathf.Max(1f, p.U(1.5f)), DividerColor);
            return panel;
        }

        // ---------------------------------------------------------------- page 2: Controls

        void DrawControls(HudPainter p, PlayerInputReader reader, ElementMoveSet moves, string healName, float slowMotionScale, Color accent)
        {
            EnsureElementRows(reader);
            EnsureRows(moves, healName, slowMotionScale);

            float margin = p.U(20f);
            float padding = p.U(26f);
            float titleHeight = p.U(52f);
            float gap = p.U(16f);
            float footerHeight = p.U(34f);
            float rowHeight = p.U(28f);
            int rows = (GameplayRows + 1) + (SandboxRows + 1);
            // Shrink the rows on short screens so the whole table always fits.
            float fixedHeight = padding * 2f + titleHeight + gap + footerHeight;
            float available = Screen.height - margin * 2f - fixedHeight;
            if (rows * rowHeight > available) rowHeight = Mathf.Max(p.U(14f), available / rows);

            float width = Mathf.Min(p.U(1180f), Screen.width - margin * 2f);
            float height = fixedHeight + rows * rowHeight;
            Rect panel = PanelRect(p, width, height, margin);

            float x = panel.x + padding;
            float innerWidth = width - padding * 2f;
            float y = panel.y + padding;
            p.Text(new Rect(x, y, innerWidth, titleHeight * 0.7f), "Controls", p.Heading, Color.white);
            p.Text(new Rect(x, y, innerWidth, titleHeight * 0.7f), "2 / 2   F1: close", p.SmallRight, DimTextColor);
            y += titleHeight;

            // Gameplay: action | gamepad | keyboard + mouse.
            float actionWidth = innerWidth * 0.42f;
            float padWidth = innerWidth * 0.30f;
            float textTop = Mathf.Max(0f, (rowHeight - p.Small.fontSize * 1.25f) * 0.5f);
            p.Text(new Rect(x, y + textTop, actionWidth, rowHeight), "Action", p.Body, accent);
            p.Text(new Rect(x + actionWidth, y + textTop, padWidth, rowHeight), "Gamepad (Xbox)", p.Body, accent);
            p.Text(new Rect(x + actionWidth + padWidth, y + textTop, innerWidth - actionWidth - padWidth, rowHeight), "Keyboard + mouse", p.Body, accent);
            y += rowHeight;
            p.Fill(new Rect(x, y - 1f, innerWidth, 1f), DividerColor);
            for (int i = 0; i < GameplayRows; i++)
            {
                if (i % 2 == 0) p.Fill(new Rect(x, y, innerWidth, rowHeight), StripeColor);
                p.Text(new Rect(x + p.U(6f), y + textTop, actionWidth, rowHeight), actions[i], p.Small, Color.white);
                p.Text(new Rect(x + actionWidth, y + textTop, padWidth, rowHeight), gamepad[i], p.Small, Color.white);
                p.Text(new Rect(x + actionWidth + padWidth, y + textTop, innerWidth - actionWidth - padWidth, rowHeight), keyboard[i], p.Small, Color.white);
                y += rowHeight;
            }

            // Sandbox keys: key | what it does.
            y += gap;
            float keyWidth = innerWidth * 0.14f;
            p.Text(new Rect(x, y + textTop, innerWidth, rowHeight), "Sandbox keys", p.Body, accent);
            y += rowHeight;
            p.Fill(new Rect(x, y - 1f, innerWidth, 1f), DividerColor);
            for (int i = 0; i < SandboxRows; i++)
            {
                if (i % 2 == 0) p.Fill(new Rect(x, y, innerWidth, rowHeight), StripeColor);
                p.Text(new Rect(x + p.U(6f), y + textTop, keyWidth, rowHeight), sandboxKeys[i], p.Body, accent);
                p.Text(new Rect(x + keyWidth, y + textTop, innerWidth - keyWidth, rowHeight), sandboxActions[i], p.Small, Color.white);
                y += rowHeight;
            }

            p.Text(new Rect(x, panel.yMax - padding - footerHeight * 0.6f, innerWidth, footerHeight * 0.6f),
                "Click the game to capture the mouse for the camera. Gamepad and keyboard work at the same time.",
                p.SmallCenter, DimTextColor);
        }

        void EnsureRows(ElementMoveSet moves, string healName, float slowMotionScale)
        {
            string skillName = moves != null && moves.Skill != null ? moves.Skill.DisplayName : "";
            string zipName = moves != null && moves.ZipStrike != null ? moves.ZipStrike.DisplayName : "";
            string northName = moves != null && moves.AbilityNorth != null ? moves.AbilityNorth.DisplayName : "";
            string eastName = moves != null && moves.AbilityEast != null ? moves.AbilityEast.DisplayName : "";
            string launcherName = moves != null && moves.Launcher != null ? moves.Launcher.DisplayName : "";
            string heavyName = moves != null && moves.Heavy != null ? moves.Heavy.DisplayName : "";
            bool parryOnly = moves == null || moves.Guard == null || moves.Guard.IsParryOnly;
            if (built && builtSkill == skillName && builtZip == zipName && builtNorth == northName && builtEast == eastName
                && builtLauncher == launcherName && builtHeavy == heavyName && builtHeal == healName && builtParryOnly == parryOnly
                && Mathf.Approximately(builtSlowScale, slowMotionScale) && ReferenceEquals(builtSwitchRow, switchPad))
            {
                return;
            }
            built = true;
            builtSkill = skillName;
            builtZip = zipName;
            builtNorth = northName;
            builtEast = eastName;
            builtLauncher = launcherName;
            builtHeavy = heavyName;
            builtHeal = healName;
            builtParryOnly = parryOnly;
            builtSlowScale = slowMotionScale;
            builtSwitchRow = switchPad;

            // Spider-Man 2's layout with the colour-matched elements (spec section 1 and 8.1; PlayerInputReader).
            string heavy = string.IsNullOrEmpty(heavyName) ? "Charged fa jin heavy" : heavyName;
            int r = 0;
            Row(ref r, "Move  /  camera", "Left stick  /  right stick", "W A S D  /  mouse");
            Row(ref r, "Change target while locked on", "Flick the right stick", "Mouse wheel, or Z / C");
            Row(ref r, "Attack: the 5-hit string, press as each hit lands", "X", "Left mouse");
            Row(ref r, "Pause finisher", "X  X  (wait)  X", "The same with left mouse");
            Row(ref r, Named("Launcher, then the air string", launcherName), "Hold X, then X in the air", "Hold left mouse, then left mouse");
            Row(ref r, Named("Zip strike to a far enemy (keeps your combo)", zipName), "Y", "F");
            Row(ref r, "Dodge (stick: toward = slip in, away = evade)  /  sprint", "B  /  hold B", "Left Shift  /  hold Left Shift");
            Row(ref r, "Dodge strike: attack late in a dodge", "B, then X", "Left Shift, then left mouse");
            Row(ref r, "Jump", "A", "Space");
            Row(ref r, parryOnly ? "Parry (tap just before a hit lands)" : "Block (hold)  /  parry (tap just before a hit)",
                parryOnly ? "Tap LB" : "Hold LB  /  tap LB", "Q");
            Row(ref r, Named("Ranged skill", skillName), "Tap RB", "Right mouse");
            // Taught as a rhythm, not a reaction: letting go when the band lights up is usually too late (playtest report
            // HUD-01). The meter's white "get ready" mark comes just before the gold band.
            Row(ref r, heavy + ": let go in the gold band (in the air: plunge)", "Hold LB + X", "Hold Q + left mouse");
            Row(ref r, Named("Mid-range ability", northName), "Hold LB + Y", "Hold Q + F");
            Row(ref r, Named("Close all-round ability", eastName), "Hold LB + B", "Hold Q + Left Shift");
            Row(ref r, "Switch element (mid-string: a switch strike)", switchPad, switchKeys);
            Row(ref r, Named("Heal", healName), "D-pad down", "R");
            Row(ref r, "Lock on (optional)  /  swap camera shoulder", "R3  /  hold L3", "Tab or middle mouse  /  hold V");
            Row(ref r, "Tutorial: start  /  skip a step  /  quit", "View  /  tap View  /  hold View", "F7  /  F8  /  F7");

            string slow = slowMotionScale > 0f && slowMotionScale < 1f
                ? "Slow motion (" + slowMotionScale.ToString("0.##", CultureInfo.InvariantCulture) + "x) for studying moves"
                : "Slow motion for studying moves";
            int s = 0;
            SandboxRow(ref s, "F1", "This overlay: combos, then controls, then close");
            SandboxRow(ref s, "F2", slow);
            SandboxRow(ref s, "F3", "Debug panel: state, frame data, buffered input, i-frames");
            SandboxRow(ref s, "F4", "Respawn at the start");
            SandboxRow(ref s, "F5 / F6", "Fluid / Punishing preset (all four elements), to compare the two feels");
            SandboxRow(ref s, "F7 / F8", "Combat tutorial: start or quit / skip a step (View on the gamepad)");
            SandboxRow(ref s, "T", "Reset enemies and dummies");
            SandboxRow(ref s, "Esc", "Release the mouse / pause");
        }

        static string Named(string generic, string name)
        {
            return string.IsNullOrEmpty(name) ? generic : generic + " (" + name + ")";
        }

        void Row(ref int index, string action, string pad, string keys)
        {
            if (index >= GameplayRows) return;
            actions[index] = action;
            gamepad[index] = pad;
            keyboard[index] = keys;
            index++;
        }

        void SandboxRow(ref int index, string key, string action)
        {
            if (index >= SandboxRows) return;
            sandboxKeys[index] = key;
            sandboxActions[index] = action;
            index++;
        }
    }
}
