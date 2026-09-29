using UnityEngine;
using UnityEngine.InputSystem;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The grey-box combat HUD, drawn with Unity's immediate-mode GUI (OnGUI). It only reads state
    // (PlayerController.Instance, LockOnController.Instance, SandboxDirector.Instance) and never changes gameplay.
    //   top-left     health (with a trail showing the damage just taken), stamina, Momentum and its damage
    //                multiplier, heal charges, current element and "not learned yet" messages
    //   top-centre   the lock-on target's name and health; sparring dummies also show combo, damage and DPS
    //   top-right    the tuning preset (F5 / F6) and the F2 slow-motion indicator
    //   centre       the heavy attack's charge meter (get-ready mark, gold sweet-spot band), messages,
    //                "You died", "Paused"
    //   F1           controls overlay        F3   debug panel (state, frame data, FPS, time scale)
    //
    // It allocates no memory while playing: styles and the one texture are made once and numbers are turned
    // into text only when they change (see HudPainter and HudNumberText). Grey-box only: the real game will
    // get proper UI later.
    [DisallowMultipleComponent]
    public class CombatHud : MonoBehaviour
    {
        // OnGUI scripts with a lower depth draw on top: the HUD covers enemy health bars and the lock-on marker.
        const int GuiDepth = -10;
        // Presentation timings in real seconds (not gameplay numbers).
        const float DamageTrailDelay = 0.6f;   // the "damage just taken" part of the health bar waits this long...
        const float DamageTrailSpeed = 0.8f;   // ...then drains at this share of the bar per second
        const float DeathFadeTime = 1.2f;
        const float SlowTextRefresh = 0.25f;   // FPS / DPS text updates this often, so it's readable and cheap
        const float SweetSpotPulseSpeed = 30f; // radians per second of the charge meter's sweet-spot flash

        static readonly Color BarBackground = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color BarOutline = new Color(0f, 0f, 0f, 0.75f);
        static readonly Color PanelColor = new Color(0.03f, 0.03f, 0.04f, 0.82f);
        static readonly Color DimText = new Color(1f, 1f, 1f, 0.65f);
        static readonly Color EmptyChargeColor = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color DeathTextColor = new Color(0.72f, 0.09f, 0.07f, 1f);

        [Tooltip("Name shown next to the heal charges. Lore names live in data, not in code.")]
        [SerializeField] private string healItemName = "Spirit Water";
        [Tooltip("Open the controls overlay (F1) when Play starts.")]
        [SerializeField] private bool showControlsAtStart = false;
        [Tooltip("Open the debug panel (F3) when Play starts.")]
        [SerializeField] private bool showDebugAtStart = false;

        [Header("Colours")]
        [SerializeField] private Color healthColor = new Color(0.8f, 0.12f, 0.1f, 1f);
        [SerializeField] private Color damageTrailColor = new Color(1f, 0.8f, 0.45f, 1f);
        [SerializeField] private Color staminaColor = new Color(0.62f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color momentumColor = new Color(1f, 0.5f, 0.08f, 1f);
        [SerializeField] private Color healChargeColor = new Color(0.45f, 0.8f, 1f, 1f);
        [SerializeField] private Color chargeColor = new Color(1f, 0.55f, 0.15f, 1f);
        [SerializeField] private Color overchargeColor = new Color(0.85f, 0.25f, 0.1f, 1f);
        [SerializeField] private Color sweetSpotColor = new Color(1f, 0.92f, 0.5f, 1f);
        [Tooltip("The charge meter's \"get ready\" mark and zone, just before the gold sweet-spot band.")]
        [SerializeField] private Color readyColor = new Color(0.9f, 0.95f, 1f, 1f);
        [SerializeField] private Color accentColor = new Color(1f, 0.78f, 0.35f, 1f);

        readonly HudPainter painter = new HudPainter();
        readonly HudControlsOverlay controlsOverlay = new HudControlsOverlay();
        readonly HudNumberText healthText = new HudNumberText("", "0", 1f);
        readonly HudNumberText multiplierText = new HudNumberText("x", "0.00", 0.01f);
        readonly HudNumberText respawnText = new HudNumberText("Respawning in ", "0", 1f);
        readonly HudNumberText slowMotionText = new HudNumberText("Slow motion ", "0.##", 0.01f, "x  (F2)");
        readonly HudNumberText fpsText = new HudNumberText("FPS ", "0", 1f);
        readonly HudNumberText timeScaleText = new HudNumberText("time scale ", "0.00", 0.01f);

        bool showControls;
        bool showDebug;

        float healthTrail01 = 1f;
        float lastHealth01 = 1f;
        float trailHold;
        float deathTimer;

        float fpsTime;
        int fpsFrames;
        float fps;
        float slowTextTimer;
        string debugHeader = "";

        // The lock-on target and its components, looked up once per target instead of every frame.
        Combatant cachedTarget;
        EnemyController cachedEnemy;
        TrainingDummy cachedDummy;
        string targetName = "";
        string targetTitle = "";
        string dummyStats = "";
        int lastComboHits = -1;

        string lastPresetName;
        string presetLabel = "";
        ElementId lastElement = ElementId.None;
        string elementName = "";
        string healLabelFor;
        float healLabelScale;
        float healLabelWidth;

        void Awake()
        {
            // We only use GUI.* calls (no GUILayout), so skip Unity's extra layout pass each frame.
            useGUILayout = false;
            showControls = showControlsAtStart;
            showDebug = showDebugAtStart;
        }

        void OnDestroy()
        {
            painter.Dispose();
        }

        void Update()
        {
            // Keys are read here, not in OnGUI: OnGUI runs several times per frame and would toggle twice.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame) showControls = !showControls;
                if (keyboard.f3Key.wasPressedThisFrame)
                {
                    showDebug = !showDebug;
                    slowTextTimer = 0f; // fill the panel's header straight away
                }
            }

            // Real time throughout: the HUD must keep animating during hitstop, slow motion and pause.
            float realDt = Time.unscaledDeltaTime;
            PlayerController player = PlayerController.Instance;
            UpdateHealthTrail(player, realDt);
            deathTimer = player != null && player.IsDead ? deathTimer + realDt : 0f;
            RefreshTarget();
            UpdateSlowTexts(realDt);
        }

        void OnGUI()
        {
            GUI.depth = GuiDepth;
            // Draw once per frame, on the Repaint pass; the other GUI events (mouse, keys) don't need us.
            if (Event.current.type != EventType.Repaint) return;
            Color previousColor = GUI.color;
            painter.Begin();

            PlayerController player = PlayerController.Instance;
            SandboxDirector director = SandboxDirector.Instance;
            if (player != null)
            {
                DrawPlayerPanel(player);
                if (player.IsCharging) DrawChargeMeter(player);
            }
            else
            {
                painter.Text(new Rect(Margin, Margin, painter.U(900f), painter.U(24f)),
                    "No player in the scene. Use Vaatu's Revenge > Build Fire Combat Sandbox.", painter.Body, accentColor);
            }
            DrawTargetPanel();
            DrawTopRight(player);
            painter.Text(new Rect(Margin, Screen.height - Margin - painter.U(22f), painter.U(400f), painter.U(22f)),
                "F1 controls  ·  F3 debug", painter.Small, DimText);
            DrawToast(director);
            if (player != null && player.IsDead) DrawDeathScreen(director);
            if (director != null ? director.IsPaused : TimeScaleController.IsPaused) DrawPauseScreen();
            if (showDebug) DrawDebugPanel(player);
            if (showControls) DrawControls(player);

            GUI.color = previousColor;
        }

        float Margin => painter.U(24f);

        // ---- Top-left: the player ----

        void DrawPlayerPanel(PlayerController player)
        {
            float x = Margin;
            float y = Margin;

            // Health, with the part just lost shown as a lighter trail that drains after a moment, so you can
            // read how much a hit took (a souls-like staple).
            var health = new Rect(x, y, painter.U(380f), painter.U(18f));
            float health01 = HudPainter.Clamp01(player.Health01);
            painter.Fill(health, BarBackground);
            painter.Fill(new Rect(health.x, health.y, health.width * Mathf.Max(health01, healthTrail01), health.height), damageTrailColor);
            painter.Fill(new Rect(health.x, health.y, health.width * health01, health.height), healthColor);
            painter.Outline(health, 1f, BarOutline);
            PlayerCombatModel model = player.Model;
            if (model != null)
            {
                var numbers = new Rect(health.x, health.y + (health.height - painter.Small.fontSize * 1.2f) * 0.5f,
                    health.width - painter.U(6f), health.height);
                painter.Text(numbers, healthText.Get(model.Health, model.MaxHealth), painter.SmallRight, Color.white);
            }
            y = health.yMax + painter.U(7f);

            var stamina = new Rect(x, y, painter.U(320f), painter.U(12f));
            painter.Bar(stamina, player.Stamina01, staminaColor, BarBackground);
            painter.Outline(stamina, 1f, BarOutline);
            y = stamina.yMax + painter.U(7f);

            // Momentum (Fire's identity mechanic): landing hits fills it, and it boosts damage by the multiplier.
            var momentum = new Rect(x, y, painter.U(320f), painter.U(10f));
            painter.Bar(momentum, player.Momentum01, momentumColor, BarBackground);
            painter.Outline(momentum, 1f, BarOutline);
            painter.Text(new Rect(momentum.xMax + painter.U(10f), momentum.center.y - painter.Small.fontSize * 0.65f, painter.U(90f), painter.U(22f)),
                multiplierText.Get(player.MomentumMultiplier), painter.Small, momentumColor);
            y = momentum.yMax + painter.U(12f);

            DrawHealCharges(x, y, player.HealCharges, player.MaxHealCharges);
            y += painter.U(26f);

            painter.Text(new Rect(x, y, painter.U(400f), painter.U(26f)), ElementName(player), painter.Body, accentColor);
            y += painter.U(28f);
            string message = player.ElementMessage;
            if (!string.IsNullOrEmpty(message)) painter.Text(new Rect(x, y, painter.U(600f), painter.U(22f)), message, painter.Small, Color.white);
        }

        void DrawHealCharges(float x, float y, int charges, int maxCharges)
        {
            if (healLabelFor != healItemName || !Mathf.Approximately(healLabelScale, painter.Scale))
            {
                healLabelFor = healItemName;
                healLabelScale = painter.Scale;
                healLabelWidth = string.IsNullOrEmpty(healItemName) ? 0f : painter.Measure(healItemName, painter.Small).x;
            }
            painter.Text(new Rect(x, y, healLabelWidth + 2f, painter.U(22f)), healItemName, painter.Small, Color.white);
            float size = painter.U(13f);
            float dotX = x + healLabelWidth + (healLabelWidth > 0f ? painter.U(10f) : 0f);
            float dotY = y + (painter.Small.fontSize * 1.2f - size) * 0.5f;
            for (int i = 0; i < maxCharges; i++)
            {
                painter.Dot(new Rect(dotX + i * (size + painter.U(5f)), dotY, size, size), i < charges ? healChargeColor : EmptyChargeColor);
            }
        }

        // The element's display name comes from its move set (data); the enum name is only a fallback.
        string ElementName(PlayerController player)
        {
            ElementId element = player.CurrentElement;
            MoveSetAsset moves = player.MoveSetAsset;
            if (moves != null && moves.MoveSet != null && moves.MoveSet.Element == element && !string.IsNullOrEmpty(moves.MoveSet.DisplayName))
            {
                return moves.MoveSet.DisplayName;
            }
            if (element != lastElement || elementName.Length == 0)
            {
                lastElement = element;
                elementName = element == ElementId.None ? "" : element.ToString();
            }
            return elementName;
        }

        void UpdateHealthTrail(PlayerController player, float realDt)
        {
            float health = player != null ? HudPainter.Clamp01(player.Health01) : 1f;
            if (health < lastHealth01) trailHold = DamageTrailDelay; // fresh damage: hold the trail a moment
            lastHealth01 = health;
            if (health >= healthTrail01)
            {
                healthTrail01 = health; // healed or respawned: nothing to show
                return;
            }
            if (trailHold > 0f)
            {
                trailHold -= realDt;
                return;
            }
            healthTrail01 = Mathf.Max(health, healthTrail01 - DamageTrailSpeed * realDt);
        }

        // ---- Centre: the heavy attack's charge ----

        void DrawChargeMeter(PlayerController player)
        {
            float width = painter.U(300f);
            float height = painter.U(14f);
            var bar = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.64f, width, height);
            float start = HudPainter.Clamp01(player.SweetSpotStart01);
            float end = HudPainter.Clamp01(player.SweetSpotEnd01);
            if (end < start)
            {
                float swap = start;
                start = end;
                end = swap;
            }
            float ready = ReadyPoint01(player, start);
            float level = HudPainter.Clamp01(player.ChargeLevel01);
            bool sweet = player.InSweetSpot;
            // The "get ready" zone runs from the ready mark to the gold band. People need about a fifth of a second
            // to react, so letting go when the band lights up is usually too late: the meter brightens here as the
            // warning, and a practised player lets go as the fill enters the gold (playtest report HUD-01).
            bool readying = !sweet && ready < start && level >= ready && level < start;
            float glow = painter.U(5f);
            var glowRect = new Rect(bar.x - glow, bar.y - glow, bar.width + glow * 2f, bar.height + glow * 2f);

            // Inside the sweet spot the whole meter flashes: the fa jin window is open.
            if (sweet)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * SweetSpotPulseSpeed);
                painter.Fill(glowRect, WithAlpha(sweetSpotColor, 0.3f + 0.45f * pulse));
            }
            else if (readying)
            {
                painter.Fill(glowRect, WithAlpha(readyColor, 0.22f));
            }
            painter.Fill(bar, BarBackground);
            if (ready < start)
            {
                painter.Fill(new Rect(bar.x + width * ready, bar.y, width * (start - ready), height), WithAlpha(readyColor, readying ? 0.3f : 0.1f));
            }
            var band = new Rect(bar.x + width * start, bar.y, width * (end - start), height);
            painter.Fill(band, WithAlpha(sweetSpotColor, 0.3f));
            Color fill = sweet ? sweetSpotColor
                : level > end ? overchargeColor
                : readying ? Color.Lerp(chargeColor, sweetSpotColor, 0.55f)
                : chargeColor;
            painter.Fill(new Rect(bar.x, bar.y, width * level, height), fill);
            float tick = Mathf.Max(1f, painter.U(2f));
            float tickOverhang = painter.U(4f);
            painter.Fill(new Rect(band.x - tick * 0.5f, bar.y - tickOverhang, tick, height + tickOverhang * 2f), sweetSpotColor);
            painter.Fill(new Rect(band.xMax - tick * 0.5f, bar.y - tickOverhang, tick, height + tickOverhang * 2f), sweetSpotColor);
            if (ready < start)
            {
                painter.Fill(new Rect(bar.x + width * ready - tick * 0.5f, bar.y - tickOverhang * 0.5f, tick, height + tickOverhang), WithAlpha(readyColor, 0.9f));
            }
            if (readying) painter.Outline(bar, Mathf.Max(1f, painter.U(1.5f)), WithAlpha(readyColor, 0.85f));
            else painter.Outline(bar, 1f, BarOutline);

            string moveName = player.MoveName;
            if (!string.IsNullOrEmpty(moveName))
            {
                painter.Text(new Rect(bar.x - width, bar.y - painter.U(28f), width * 3f, painter.U(22f)), moveName, painter.SmallCenter,
                    sweet ? sweetSpotColor : Color.white);
            }
        }

        // Where the "get ready" mark sits on the 0..1 meter: the move set's Charge.ReadyCueLead seconds before the
        // sweet spot opens. Returns sweetSpotStart01 (no ready zone) when there's no model yet or no lead is set.
        static float ReadyPoint01(PlayerController player, float sweetSpotStart01)
        {
            PlayerCombatModel model = player.Model;
            ChargeSettings charge = model != null && model.MoveSet != null ? model.MoveSet.Charge : null;
            if (charge == null || !(charge.MaxChargeTime > 0f) || !(charge.ReadyCueLead > 0f)) return sweetSpotStart01;
            return Mathf.Min(sweetSpotStart01, HudPainter.Clamp01(sweetSpotStart01 - charge.ReadyCueLead / charge.MaxChargeTime));
        }

        // ---- Top-centre: the lock-on target ----

        void RefreshTarget()
        {
            LockOnController lockOn = LockOnController.Instance;
            Combatant target = lockOn != null ? lockOn.Target : null;
            if (!ReferenceEquals(target, cachedTarget))
            {
                cachedTarget = target;
                cachedEnemy = null;
                cachedDummy = null;
                targetName = "";
                targetTitle = "";
                dummyStats = "";
                lastComboHits = -1;
                if (target != null)
                {
                    target.TryGetComponent(out cachedEnemy);
                    target.TryGetComponent(out cachedDummy);
                    targetName = target.DisplayName;
                    targetTitle = "Target: " + targetName;
                }
            }
        }

        void DrawTargetPanel()
        {
            // Unity reports a destroyed target as == null, so a fighter removed mid-fight is simply skipped.
            if (cachedTarget == null) return;
            float width = painter.U(460f);
            float x = (Screen.width - width) * 0.5f;
            float y = Margin;
            painter.Text(new Rect(x - width, y, width * 3f, painter.U(26f)), targetName, painter.BodyCenter, Color.white);
            y += painter.U(30f);

            float health01 = -1f;
            if (cachedEnemy != null) health01 = cachedEnemy.Health01;
            else if (cachedDummy != null) health01 = cachedDummy.Health01;
            if (health01 >= 0f)
            {
                var bar = new Rect(x, y, width, painter.U(10f));
                painter.Bar(bar, health01, healthColor, BarBackground);
                painter.Outline(bar, 1f, BarOutline);
                y = bar.yMax + painter.U(6f);
            }
            if (cachedDummy != null) painter.Text(new Rect(x - width, y, width * 3f, painter.U(22f)), dummyStats, painter.SmallCenter, accentColor);
        }

        // ---- Top-right, messages, death and pause ----

        void DrawTopRight(PlayerController player)
        {
            float width = painter.U(460f);
            float x = Screen.width - Margin - width;
            float y = Margin;
            if (player != null)
            {
                string preset = player.PresetName;
                if (preset != lastPresetName)
                {
                    lastPresetName = preset;
                    presetLabel = "Preset: " + (string.IsNullOrEmpty(preset) ? "?" : preset) + "  (F5 / F6)";
                }
                painter.Text(new Rect(x, y, width, painter.U(26f)), presetLabel, painter.BodyRight, Color.white);
            }
            y += painter.U(30f);
            if (TimeScaleController.IsDebugSlowMotion)
            {
                painter.Text(new Rect(x, y, width, painter.U(22f)), slowMotionText.Get(TimeScaleController.DebugSlowMotionScale), painter.SmallRight, accentColor);
            }
        }

        void DrawToast(SandboxDirector director)
        {
            if (director == null) return;
            string text = director.ToastText;
            if (string.IsNullOrEmpty(text)) return;
            float alpha = director.ToastAlpha;
            Vector2 size = painter.Measure(text, painter.Body);
            float padX = painter.U(22f);
            float padY = painter.U(10f);
            var box = new Rect((Screen.width - size.x) * 0.5f - padX, Screen.height * 0.24f, size.x + padX * 2f, size.y + padY * 2f);
            painter.Fill(box, new Color(0f, 0f, 0f, 0.6f * alpha));
            painter.Text(new Rect(box.x + padX, box.y + padY, size.x + 2f, size.y), text, painter.Body, new Color(1f, 1f, 1f, alpha));
        }

        void DrawDeathScreen(SandboxDirector director)
        {
            float fade = HudPainter.Clamp01(deathTimer / DeathFadeTime);
            float bandHeight = painter.U(180f);
            var band = new Rect(0f, (Screen.height - bandHeight) * 0.5f, Screen.width, bandHeight);
            painter.Fill(band, new Color(0f, 0f, 0f, 0.7f * fade));
            painter.Text(new Rect(0f, band.y, Screen.width, bandHeight * 0.72f), "You died", painter.Huge, WithAlpha(DeathTextColor, fade));
            string next = director != null && director.RespawnPending
                ? respawnText.Get(Mathf.Ceil(director.RespawnRemaining))
                : "Press F4 to respawn";
            painter.Text(new Rect(0f, band.y + bandHeight * 0.7f, Screen.width, painter.U(24f)), next, painter.SmallCenter, new Color(1f, 1f, 1f, 0.8f * fade));
        }

        void DrawPauseScreen()
        {
            painter.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.45f));
            float middle = Screen.height * 0.5f;
            painter.Text(new Rect(0f, middle - painter.U(70f), Screen.width, painter.U(80f)), "Paused", painter.Huge, Color.white);
            painter.Text(new Rect(0f, middle + painter.U(18f), Screen.width, painter.U(26f)), "Esc to resume  ·  F1 controls", painter.BodyCenter, DimText);
        }

        // ---- F3 debug panel ----

        void UpdateSlowTexts(float realDt)
        {
            // Frames per second, averaged over a short window so the number is readable.
            fpsFrames++;
            fpsTime += realDt;
            if (fpsTime >= SlowTextRefresh)
            {
                fps = fpsFrames / fpsTime;
                fpsFrames = 0;
                fpsTime = 0f;
            }

            // These strings are rebuilt a few times a second at most (or on a new hit), and only while shown.
            slowTextTimer -= realDt;
            bool comboChanged = cachedDummy != null && cachedDummy.ComboHits != lastComboHits;
            if (slowTextTimer > 0f && !comboChanged) return;
            slowTextTimer = SlowTextRefresh;
            if (cachedDummy != null)
            {
                lastComboHits = cachedDummy.ComboHits;
                dummyStats = "Combo " + cachedDummy.ComboHits
                             + "  ·  " + Mathf.RoundToInt(cachedDummy.ComboDamage) + " damage"
                             + "  ·  " + cachedDummy.ComboDps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " DPS"
                             + "  ·  last hit " + Mathf.RoundToInt(cachedDummy.LastHitDamage);
            }
            if (showDebug)
            {
                debugHeader = fpsText.Get(fps) + "  ·  " + timeScaleText.Get(Time.timeScale)
                              + "  ·  slow-mo " + (TimeScaleController.IsDebugSlowMotion ? "on (F2)" : "off (F2)")
                              + (TimeScaleController.IsHitstopActive ? "  ·  hitstop" : "");
            }
        }

        void DrawDebugPanel(PlayerController player)
        {
            float pad = painter.U(12f);
            float width = Mathf.Min(painter.U(480f), Screen.width * 0.45f);
            float textWidth = width - pad * 2f;
            GUIStyle style = painter.Wrapped;
            float line = style.fontSize * 1.35f;

            string playerText = player != null ? Safe(player.DebugText) : "No player in the scene.";
            bool hasTarget = cachedTarget != null;
            string targetText = "";
            if (hasTarget)
            {
                if (cachedEnemy != null) targetText = Safe(cachedEnemy.DebugText);
                else if (cachedDummy != null) targetText = dummyStats;
            }

            float playerHeight = painter.MeasureHeight(playerText, style, textWidth);
            float targetHeight = hasTarget ? painter.MeasureHeight(targetText, style, textWidth) : 0f;
            float height = pad * 2f + line * 2f + playerHeight + (hasTarget ? line * 1.5f + targetHeight : 0f);
            var panel = new Rect(Screen.width - Margin - width, Margin + painter.U(62f), width, height);
            painter.Fill(panel, PanelColor);

            float x = panel.x + pad;
            float y = panel.y + pad;
            painter.Text(new Rect(x, y, textWidth, line), debugHeader, painter.Small, accentColor);
            y += line;
            painter.Text(new Rect(x, y, textWidth, line), "Player", painter.Small, accentColor);
            y += line;
            painter.Text(new Rect(x, y, textWidth, playerHeight), playerText, style, Color.white);
            y += playerHeight;
            if (!hasTarget) return;
            y += line * 0.5f;
            painter.Text(new Rect(x, y, textWidth, line), targetTitle, painter.Small, accentColor);
            y += line;
            painter.Text(new Rect(x, y, textWidth, targetHeight), targetText, style, Color.white);
        }

        // ---- F1 controls overlay ----

        void DrawControls(PlayerController player)
        {
            MoveSetAsset moves = player != null ? player.MoveSetAsset : null;
            ElementMoveSet set = moves != null ? moves.MoveSet : null;
            controlsOverlay.Draw(painter, set, healItemName, TimeScaleController.DebugSlowMotionScale, accentColor);
        }

        static string Safe(string text)
        {
            return text ?? "";
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
