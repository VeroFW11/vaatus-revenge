using System.Globalization;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The F1 controls overlay: every binding from the prototype spec (docs/Prototype/Fire-Combat-Prototype-Spec.md,
    // section 1) for gamepad and keyboard + mouse, then the sandbox keys. If a binding changes in
    // PlayerInputReader, update the table here too.
    // The rows are built once (and rebuilt only if a name shown in them changes), so drawing allocates nothing.
    public sealed class HudControlsOverlay
    {
        const int GameplayRows = 19;
        const int SandboxRows = 7;

        static readonly Color PanelColor = new Color(0.035f, 0.035f, 0.045f, 0.92f);
        static readonly Color StripeColor = new Color(1f, 1f, 1f, 0.045f);
        static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color DimTextColor = new Color(1f, 1f, 1f, 0.6f);

        readonly string[] actions = new string[GameplayRows];
        readonly string[] gamepad = new string[GameplayRows];
        readonly string[] keyboard = new string[GameplayRows];
        readonly string[] sandboxKeys = new string[SandboxRows];
        readonly string[] sandboxActions = new string[SandboxRows];
        bool built;
        string builtSkill, builtZip, builtHeavy, builtElement, builtHeal, builtNorth, builtEast, builtLauncher;
        bool builtParryOnly;
        float builtSlowScale = -1f;

        // Move names come from data (the move set and the HUD's settings), so lore names never live in code. Empty
        // names fall back to generic words. moves may be null (generic words throughout).
        public void Draw(HudPainter p, ElementMoveSet moves, string healName, float slowMotionScale, Color accent)
        {
            EnsureRows(moves, healName, slowMotionScale);

            float margin = p.U(20f);
            float pad = p.U(26f);
            float titleHeight = p.U(52f);
            float gap = p.U(16f);
            float footerHeight = p.U(34f);
            float rowHeight = p.U(28f);
            int rows = (GameplayRows + 1) + (SandboxRows + 1);
            // Shrink the rows on short screens so the whole table always fits.
            float fixedHeight = pad * 2f + titleHeight + gap + footerHeight;
            float available = Screen.height - margin * 2f - fixedHeight;
            if (rows * rowHeight > available) rowHeight = Mathf.Max(p.U(14f), available / rows);

            float width = Mathf.Min(p.U(1120f), Screen.width - margin * 2f);
            float height = fixedHeight + rows * rowHeight;
            var panel = new Rect((Screen.width - width) * 0.5f, Mathf.Max(margin, (Screen.height - height) * 0.5f), width, height);
            p.Fill(panel, PanelColor);
            p.Outline(panel, Mathf.Max(1f, p.U(1.5f)), DividerColor);

            float x = panel.x + pad;
            float innerWidth = width - pad * 2f;
            float y = panel.y + pad;
            p.Text(new Rect(x, y, innerWidth, titleHeight * 0.7f), "Controls", p.Heading, Color.white);
            p.Text(new Rect(x, y, innerWidth, titleHeight * 0.7f), "F1 to close", p.SmallRight, DimTextColor);
            y += titleHeight;

            // Gameplay: action | gamepad | keyboard + mouse.
            float actionWidth = innerWidth * 0.43f;
            float padWidth = innerWidth * 0.25f;
            float textTop = Mathf.Max(0f, (rowHeight - p.Small.fontSize * 1.25f) * 0.5f);
            p.Text(new Rect(x, y + textTop, actionWidth, rowHeight), "Action", p.Body, accent);
            p.Text(new Rect(x + actionWidth, y + textTop, padWidth, rowHeight), "Gamepad (Xbox / PS)", p.Body, accent);
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
            p.Text(new Rect(x, y + textTop, innerWidth, rowHeight), "Sandbox keys (keyboard)", p.Body, accent);
            y += rowHeight;
            p.Fill(new Rect(x, y - 1f, innerWidth, 1f), DividerColor);
            for (int i = 0; i < SandboxRows; i++)
            {
                if (i % 2 == 0) p.Fill(new Rect(x, y, innerWidth, rowHeight), StripeColor);
                p.Text(new Rect(x + p.U(6f), y + textTop, keyWidth, rowHeight), sandboxKeys[i], p.Body, accent);
                p.Text(new Rect(x + keyWidth, y + textTop, innerWidth - keyWidth, rowHeight), sandboxActions[i], p.Small, Color.white);
                y += rowHeight;
            }

            p.Text(new Rect(x, panel.yMax - pad - footerHeight * 0.6f, innerWidth, footerHeight * 0.6f),
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
            string elementName = moves != null ? moves.DisplayName : "";
            bool parryOnly = moves == null || moves.Guard == null || moves.Guard.IsParryOnly;
            if (built && builtSkill == skillName && builtZip == zipName && builtNorth == northName && builtEast == eastName
                && builtLauncher == launcherName && builtHeavy == heavyName && builtElement == elementName
                && builtHeal == healName && builtParryOnly == parryOnly && Mathf.Approximately(builtSlowScale, slowMotionScale))
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
            builtElement = elementName;
            builtHeal = healName;
            builtParryOnly = parryOnly;
            builtSlowScale = slowMotionScale;

            // Spider-Man 2's layout (see the spec's controls table and PlayerInputReader).
            string skill = Named("Ranged skill", skillName);
            string heal = Named("Heal", healName);
            string zip = Named("Zip strike: dash to the enemy you aim at", zipName);
            string heavy = string.IsNullOrEmpty(heavyName) ? "Heavy" : heavyName;
            string element = string.IsNullOrEmpty(elementName) ? "Hold RB / R1 + a face button" : "Hold RB / R1 + a face button (Y = " + elementName + ")";

            int r = 0;
            Row(ref r, "Move", "Left stick", "W A S D");
            Row(ref r, "Camera", "Right stick", "Mouse");
            Row(ref r, "Swap camera shoulder (hold)", "Hold L3 (left-stick click)", "Hold V");
            Row(ref r, "Attack: 5-hit string (aims where your stick points)", "X / Square", "Left mouse");
            Row(ref r, Named("Launcher: hold attack, then attack in the air", launcherName), "Hold X / Square", "Hold left mouse");
            Row(ref r, "Air combo (in the air) / plunge (in the air)", "X  /  hold LB + X", "Left mouse  /  hold Q + left mouse");
            Row(ref r, zip, "Y / Triangle", "F");
            // Taught as a rhythm, not a reaction: letting go when the band lights up is usually too late
            // (playtest report HUD-01). The meter's white "get ready" mark comes just before the gold band.
            Row(ref r, heavy + ": hold, let go as the meter fills the gold band", "Hold LB / L1, then hold X / Square", "Hold Q, then hold left mouse");
            Row(ref r, Named("Mid-range ability", northName), "Hold LB / L1, then Y / Triangle", "Hold Q, then F");
            Row(ref r, Named("Close all-round ability", eastName), "Hold LB / L1, then B / Circle", "Hold Q, then Left Shift");
            Row(ref r, "Dodge (tap) / Sprint (hold) / Air dash (in the air)", "B / Circle", "Left Shift");
            Row(ref r, "Jump", "A / Cross", "Space");
            Row(ref r, parryOnly ? "Parry (tap just before a hit lands)" : "Guard (hold) / Deflect (press just before a hit)",
                parryOnly ? "Tap LB / L1" : "LB / L1", "Q");
            Row(ref r, skill, "Tap RB / R1", "Right mouse");
            Row(ref r, heal, "D-pad down", "R");
            Row(ref r, "Lock on / off (optional)", "R3", "Middle mouse or Tab");
            Row(ref r, "Switch target", "Flick right stick while locked", "Mouse wheel, or Z / C");
            Row(ref r, "Element select", element, "1 - 4");

            string slow = slowMotionScale > 0f && slowMotionScale < 1f
                ? "Slow motion (" + slowMotionScale.ToString("0.##", CultureInfo.InvariantCulture) + "x) for studying moves"
                : "Slow motion for studying moves";
            int s = 0;
            SandboxRow(ref s, "F1", "Show / hide these controls");
            SandboxRow(ref s, "F2", slow);
            SandboxRow(ref s, "F3", "Debug panel: state, frame data, buffered input, i-frames");
            SandboxRow(ref s, "F4", "Respawn at the start");
            SandboxRow(ref s, "F5 / F6", "Fluid / Punishing preset, to compare the two feels");
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
