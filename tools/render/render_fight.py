#!/usr/bin/env python3
"""Render the CombatSim `anim` scenario JSON as a video or a contact sheet, so the procedural animation can be
checked without Unity.

    dotnet run --project tools/CombatSim -- anim --out /tmp/anim.json
    python3 tools/render/render_fight.py /tmp/anim.json --mp4 fight.mp4            # the whole fight at 30 fps
    python3 tools/render/render_fight.py /tmp/anim.json --sheet sheet.png --scene chain --count 12
    python3 tools/render/render_fight.py /tmp/anim.json --sheet keys.png --frames 120,130,140

Needs: pip install matplotlib imageio imageio-ffmpeg (ffmpeg comes with imageio-ffmpeg).

What you see: a 3/4 camera that follows the player; each fighter drawn as thick limbs with joints, a torso, a head
with a face marker (so facing reads); team colours (the Avatar in the chosen character sheet's colours: crimson
tunic, bare arms with black bracers, black sash, maroon trousers, dark shin wraps; soldiers slate and iron with a sword,
crossbowmen tan, dummies straw); a 1 m ground grid; effects as translucent shapes matching their EffectKey (cone,
ring, ribbon, pillar, slam, trails, jets, and since Build 05 wave, line, dome, vortex, shards, stomp) coloured by the
element that made them (fire orange, water blue, earth brown, air pale green-white); a ring round the player's
shadow in the active element's colour; projectiles; and a caption with the move name, the element, the string
branch and the beat grade. Outputs are scratch files: never commit renders.
"""
import argparse
import json
import math
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.patches import Circle, Polygon  # noqa: E402

J = {}  # joint name -> index, filled from the file

COLOURS = {
    # the chosen character sheet (docs/Art/Character-Sheets/Player-Avatar-Style-B-chosen.webp): crimson sleeveless tunic,
    # bare warm-tan arms with black leather bracers, black sash with gold trim, maroon trousers, dark shin wraps, black shoes
    "player": {"body": "#8c1d2c", "limb": "#c98a5c", "trim": "#d4a43a", "skin": "#c98a5c", "dark": "#2a1416",
               "uarm": "#c98a5c", "farm": "#231a1a", "thigh": "#4a1420", "shin": "#3a2a26", "foot": "#141010", "fist": "#c98a5c"},
    "soldier": {"body": "#333a45", "limb": "#262a30", "trim": "#946b33", "skin": "#c79470", "dark": "#0f0f12"},  # BodyLook.Soldier: slate and iron, bronze trim
    "crossbow": {"body": "#9c7f55", "limb": "#b39468", "trim": "#5b4a30", "skin": "#c89878", "dark": "#3b2f1d"},
    "dummy": {"body": "#c9a86a", "limb": "#d8bb80", "trim": "#8a6a3a", "skin": "#c9a86a", "dark": "#6a5230"},
}
FIRE = "#ff7a1a"
FIRE_HOT = "#ffd24a"

# Each element's effect colours (outer, hot core). Effects carry "el"; enemies' and old files' effects are fire-coloured.
ELEMENT_COLOURS = {
    "fire": (FIRE, FIRE_HOT),
    "water": ("#2f8fe0", "#a8dcff"),
    "earth": ("#8a6a3a", "#d2b07a"),
    "air": ("#a9cfc4", "#f2fbf7"),
}


def element_colours(el):
    return ELEMENT_COLOURS.get(el or "fire", ELEMENT_COLOURS["fire"])

# Limb segments: (from, to, thickness in metres, colour role). The thigh/shin/foot/uarm/farm roles fall back to the
# kind's limb/trim colours (enemies); the player has its own (bare upper arms, bracers, trousers, wraps, shoes).
SEGMENTS = [
    ("LeftUpperLeg", "LeftLowerLeg", 0.16, "thigh"), ("LeftLowerLeg", "LeftFoot", 0.12, "shin"), ("LeftFoot", "LeftToes", 0.09, "foot"),
    ("RightUpperLeg", "RightLowerLeg", 0.16, "thigh"), ("RightLowerLeg", "RightFoot", 0.12, "shin"), ("RightFoot", "RightToes", 0.09, "foot"),
    ("Hips", "Spine", 0.26, "body"), ("Spine", "Chest", 0.28, "body"), ("Chest", "UpperChest", 0.3, "body"),
    ("UpperChest", "Neck", 0.12, "skin"), ("Neck", "Head", 0.1, "skin"),
    ("LeftShoulder", "LeftUpperArm", 0.12, "body"), ("RightShoulder", "RightUpperArm", 0.12, "body"),
    ("LeftUpperArm", "LeftLowerArm", 0.11, "uarm"), ("LeftLowerArm", "LeftHand", 0.1, "farm"),
    ("RightUpperArm", "RightLowerArm", 0.11, "uarm"), ("RightLowerArm", "RightHand", 0.1, "farm"),
]
ROLE_FALLBACK = {"thigh": "limb", "shin": "limb", "foot": "trim", "uarm": "limb", "farm": "limb", "fist": "skin"}


def colour_of(colours, role):
    return colours.get(role) or colours[ROLE_FALLBACK.get(role, "limb")]


class Camera:
    """A pinhole camera looking at a target from a fixed 3/4 angle (front-right, above)."""

    def __init__(self, width, height, fov=50.0):
        self.w, self.h = width, height
        self.f = (height / 2) / math.tan(math.radians(fov) / 2)
        self.target = [0.0, 1.0, 0.0]
        self.offset = (3.4, 1.5, 1.1)

    def follow(self, point, smooth=1.0):
        for i in range(3):
            self.target[i] += (point[i] - self.target[i]) * smooth
        self._basis()

    def _basis(self):
        t = self.target
        self.pos = [t[0] + self.offset[0], t[1] + self.offset[1], t[2] + self.offset[2]]
        fwd = norm(sub(t, self.pos))
        right = norm(cross((0, 1, 0), fwd))
        up = cross(fwd, right)
        self.fwd, self.right, self.up = fwd, right, up

    NEAR = 0.4

    def to_camera(self, p):
        d = sub(p, self.pos)
        return (dot(d, self.right), dot(d, self.up), dot(d, self.fwd))

    def from_camera(self, c):
        z = max(c[2], self.NEAR)
        return c[0] * self.f / z + self.w / 2, -c[1] * self.f / z + self.h / 2, z

    def project(self, p):
        return self.from_camera(self.to_camera(p))

    def clip_polygon(self, pts3):
        """Projects a polygon, cutting away the part behind the camera's near plane (so it doesn't smear)."""
        cam = [self.to_camera(p) for p in pts3]
        out = []
        n = len(cam)
        for i in range(n):
            a, b = cam[i], cam[(i + 1) % n]
            a_in, b_in = a[2] >= self.NEAR, b[2] >= self.NEAR
            if a_in:
                out.append(a)
            if a_in != b_in:
                t = (self.NEAR - a[2]) / (b[2] - a[2])
                out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, self.NEAR))
        return [self.from_camera(c) for c in out]

    def scale(self, metres, depth):
        return metres * self.f / max(depth, 0.1)


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def mul(a, k):
    return (a[0] * k, a[1] * k, a[2] * k)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    length = math.sqrt(dot(a, a)) or 1.0
    return (a[0] / length, a[1] / length, a[2] / length)


def joints_of(fighter):
    j = fighter["j"]
    return [(j[i * 3], j[i * 3 + 1], j[i * 3 + 2]) for i in range(len(j) // 3)]


class Renderer:
    def __init__(self, data, width=960, height=540, follow="player"):
        self.follow = follow
        self.data = data
        self.frames = data["frames"]
        for i, name in enumerate(data["joints"]):
            J[name] = i
        self.w, self.h = width, height
        self.cam = Camera(width, height)
        self.active_fx = []   # (start_index, fx)
        self.history = {}     # fighter id -> list of joint lists (recent frames, for trails)

    # ---------------------------------------------------------------- per frame state

    def advance(self, index):
        """Collect effects that are alive at frame `index` (effects start on their frame and last `dur` seconds)."""
        frame = self.frames[index]
        fps = self.data.get("fps", 60)
        for fx in frame["fx"]:
            self.active_fx.append((index, fx))
        self.active_fx = [(s, fx) for (s, fx) in self.active_fx if (index - s) / fps <= fx["dur"] and s <= index]
        for f in frame["fighters"]:
            hist = self.history.setdefault(f["id"], [])
            hist.append(joints_of(f))
            if len(hist) > 12:
                hist.pop(0)

    def reset_state(self):
        self.active_fx = []
        self.history = {}

    def warm_up(self, index, lookback=40):
        """Rebuild effect and trail state for a frame picked out of order (contact sheets)."""
        self.reset_state()
        start = max(0, index - lookback)
        for i in range(start, index + 1):
            if self.frames[i]["scene"] != self.frames[index]["scene"]:
                self.reset_state()
                continue
            self.advance(i)

    # ---------------------------------------------------------------- drawing

    def draw(self, ax, index, snap_camera=False):
        frame = self.frames[index]
        fps = self.data.get("fps", 60)
        fighters = frame["fighters"]
        player = next((f for f in fighters if f["kind"] == self.follow), None)
        if player is None:
            player = next((f for f in fighters if f["kind"] == "player"), fighters[0])
        hips = joints_of(player)[J["Hips"]]
        focus = (hips[0], 1.0 + 0.85 * max(0.0, hips[1] - 1.0), hips[2])
        self.cam.follow(focus, 1.0 if snap_camera else 0.12)

        ax.set_xlim(0, self.w)
        ax.set_ylim(self.h, 0)
        ax.set_facecolor("#20232a")
        ax.axis("off")
        self.draw_grid(ax)

        # shadows first
        for f in fighters:
            self.draw_shadow(ax, f)

        items = []   # (depth, callable)
        for f in fighters:
            items.extend(self.fighter_items(f))
        for (start, fx) in self.active_fx:
            age = (index - start) / fps
            items.extend(self.fx_items(fx, age, frame))
        for p in frame.get("proj", []):
            items.append(self.projectile_item(p))
        items.sort(key=lambda it: -it[0])
        for _, paint in items:
            paint(ax)

        ax.text(12, 24, frame["scene"], color="#e8e8e8", fontsize=11, family="DejaVu Sans", clip_on=True)
        ax.text(12, 44, "t = %.2f s" % frame["t"], color="#9aa0a6", fontsize=8, clip_on=True)
        caption = frame["caption"] or ""
        el = player.get("el", "")
        if caption:
            ax.text(self.w / 2, self.h - 22, caption, color=element_colours(el)[1], fontsize=16, ha="center", weight="bold", clip_on=True)
        if el:
            tags = [el.capitalize()] + [player[k] for k in ("branch", "grade", "dodge") if player.get(k)]
            if player.get("combo"):
                tags.append("%d hits" % player["combo"] + (", MIX %d" % player["mix"] if player.get("mix", 0) >= 2 else ""))
            ax.text(self.w / 2, self.h - 44, "  ".join(tags), color=element_colours(el)[0], fontsize=9, ha="center", clip_on=True)
        for f in fighters:
            head = joints_of(f)[J["Head"]]
            x, y, z = self.cam.project(add(head, (0, 0.42, 0)))
            ax.text(x, y, "%s%s" % (f["key"], "" if f["alive"] else " (down)"), color="#c8ccd2", fontsize=7, ha="center", clip_on=True)

    def draw_grid(self, ax):
        cx, cz = round(self.cam.target[0]), round(self.cam.target[2])
        for i in range(-9, 10):
            for a, b in (((cx + i, 0, cz - 9), (cx + i, 0, cz + 9)), ((cx - 9, 0, cz + i), (cx + 9, 0, cz + i))):
                pa, pb = self.cam.project(a), self.cam.project(b)
                ax.plot([pa[0], pb[0]], [pa[1], pb[1]], color="#343a44", lw=0.7, zorder=0)

    def draw_shadow(self, ax, f):
        joints = joints_of(f)
        hips = joints[J["Hips"]]
        x, y, z = self.cam.project((hips[0], 0.0, hips[2]))
        r = self.cam.scale(0.38, z)
        ax.add_patch(Circle((x, y), r, color="#000000", alpha=0.35, lw=0, zorder=1))
        if f.get("el"):
            # the active element, as a ring on the floor round the player
            self.ring((hips[0], 0.01, hips[2]), 0.48, element_colours(f["el"])[0], 0.55, 0.04)[1](ax)

    def fighter_items(self, f):
        colours = COLOURS.get(f["kind"], COLOURS["player"])
        joints = joints_of(f)
        items = []
        dead = not f["alive"]
        alpha = 0.75 if dead else 1.0

        for a, b, thickness, role in SEGMENTS:
            pa, pb = joints[J[a]], joints[J[b]]
            if b.endswith("Toes"):
                # draw the foot on past the toes joint a little
                pb = add(pb, mul(sub(pb, pa), 0.45))
            items.append(self.segment(pa, pb, thickness, colour_of(colours, role), alpha))
        # hands and fists
        for side in ("Left", "Right"):
            lower, hand = joints[J[side + "LowerArm"]], joints[J[side + "Hand"]]
            fist = add(hand, mul(norm(sub(hand, lower)), 0.07))
            items.append(self.ball(fist, 0.065, colour_of(colours, "fist"), alpha))
        # torso plate between shoulders and hips (gives the body mass)
        items.append(self.torso(joints, colours, alpha))
        # head with a face marker so facing reads
        items.append(self.head(joints, colours, f["kind"], alpha))
        # joints
        for name in ("LeftLowerArm", "RightLowerArm", "LeftLowerLeg", "RightLowerLeg", "LeftUpperArm", "RightUpperArm"):
            items.append(self.ball(joints[J[name]], 0.055, colours["dark"], alpha))
        if "prop" in f:
            items.append(self.prop(f["prop"], f["kind"], alpha))
        return items

    def segment(self, a, b, thickness, colour, alpha):
        pa, pb = self.cam.project(a), self.cam.project(b)
        depth = (pa[2] + pb[2]) / 2
        width = self.cam.scale(thickness, depth) * 0.9

        def paint(ax):
            ax.plot([pa[0], pb[0]], [pa[1], pb[1]], color=colour, lw=width, solid_capstyle="round", alpha=alpha, zorder=3)
        return depth, paint

    def ball(self, p, radius, colour, alpha):
        x, y, z = self.cam.project(p)
        r = self.cam.scale(radius, z)

        def paint(ax):
            ax.add_patch(Circle((x, y), r, color=colour, alpha=alpha, lw=0, zorder=4))
        return z - 0.01, paint

    def torso(self, joints, colours, alpha):
        ls, rs = joints[J["LeftUpperArm"]], joints[J["RightUpperArm"]]
        lh, rh = joints[J["LeftUpperLeg"]], joints[J["RightUpperLeg"]]
        chest = joints[J["UpperChest"]]
        quad = [ls, rs, add(rh, (0, 0.02, 0)), add(lh, (0, 0.02, 0))]
        pts = [self.cam.project(p) for p in quad]
        depth = sum(p[2] for p in pts) / 4 + 0.02
        sash_a, sash_b = self.cam.project(joints[J["Spine"]]), self.cam.project(add(joints[J["Spine"]], (0, 0.001, 0)))

        def paint(ax):
            ax.add_patch(Polygon([(p[0], p[1]) for p in pts], closed=True, color=colours["body"], alpha=alpha, lw=0, zorder=2))
            # sash / belt
            l, r = self.cam.project(add(lh, (0, 0.13, 0))), self.cam.project(add(rh, (0, 0.13, 0)))
            ax.plot([l[0], r[0]], [l[1], r[1]], color=colours["trim"], lw=self.cam.scale(0.06, depth), alpha=alpha, solid_capstyle="round", zorder=2)
        return depth, paint

    def head(self, joints, colours, kind, alpha):
        neck, head = joints[J["Neck"]], joints[J["Head"]]
        up = norm(sub(head, neck))
        centre = add(head, mul(up, 0.1))
        # facing: the chest's forward, estimated from the shoulders (left->right) and the spine (up)
        across = sub(joints[J["RightUpperArm"]], joints[J["LeftUpperArm"]])
        forward = norm(cross(across, up))
        nose = add(centre, mul(forward, 0.13))
        pc, pn = self.cam.project(centre), self.cam.project(nose)
        r = self.cam.scale(0.115, pc[2])

        def paint(ax):
            ax.add_patch(Circle((pc[0], pc[1]), r, color=colours["skin"], alpha=alpha, lw=0, zorder=5))
            if kind == "soldier":
                ax.add_patch(Circle((pc[0], pc[1] - r * 0.3), r * 0.95, color=colours["dark"], alpha=alpha, lw=0, zorder=5))
            elif kind == "player":
                ax.add_patch(Circle((pc[0], pc[1] - r * 0.35), r * 0.9, color="#151012", alpha=alpha, lw=0, zorder=5))   # black hair
                top = self.cam.project(add(centre, mul(up, 0.15)))
                ax.add_patch(Circle((top[0], top[1]), r * 0.38, color="#151012", alpha=alpha, lw=0, zorder=5))   # high topknot
                ax.add_patch(Circle((top[0], top[1] + r * 0.3), r * 0.16, color="#7a1020", alpha=alpha, lw=0, zorder=5))   # red tie
            ax.plot([pc[0], pn[0]], [pc[1], pn[1]], color="#1a1a1a", lw=max(1.5, r * 0.35), alpha=alpha, zorder=5)
        return pc[2] - 0.02, paint

    def prop(self, prop, kind, alpha):
        grip = (prop[0], prop[1], prop[2])
        d = (prop[3], prop[4], prop[5])
        length = prop[6]
        tip = add(grip, mul(d, length))
        pg, pt = self.cam.project(grip), self.cam.project(tip)
        depth = (pg[2] + pt[2]) / 2

        def paint(ax):
            if kind == "crossbow":
                ax.plot([pg[0], pt[0]], [pg[1], pt[1]], color="#4a3a22", lw=self.cam.scale(0.06, depth), alpha=alpha, zorder=4)
                side = norm(cross(d, (0, 1, 0)))
                a = self.cam.project(add(tip, mul(side, 0.3)))
                b = self.cam.project(add(tip, mul(side, -0.3)))
                ax.plot([a[0], b[0]], [a[1], b[1]], color="#3a2e1a", lw=self.cam.scale(0.03, depth), alpha=alpha, zorder=4)
            else:
                colour = "#d9dde3" if kind == "soldier" else "#7a5a30"
                ax.plot([pg[0], pt[0]], [pg[1], pt[1]], color=colour, lw=self.cam.scale(0.045, depth), alpha=alpha,
                        solid_capstyle="butt", zorder=4)
        return depth - 0.01, paint

    # ---------------------------------------------------------------- effects

    def fx_items(self, fx, age, frame):
        key = fx["key"]
        outer, hot = element_colours(fx.get("el"))
        dur = max(fx["dur"], 1e-3)
        u = min(1.0, age / dur)
        fade = max(0.0, 1.0 - u)
        o = tuple(fx["o"])
        d = norm(tuple(fx["d"])) if any(fx["d"]) else (0, 0, 1)
        fighter = next((f for f in frame["fighters"] if f["id"] == fx["fighter"]), None)
        items = []

        if key in ("cone",):
            yaw = math.atan2(d[0], d[2])
            half = math.radians(max(fx["arc"], 20)) / 2
            reach = fx["range"] * min(1.0, 0.35 + u * 1.6)
            pts = [o] + [add(o, (math.sin(yaw + a) * reach, d[1] * reach, math.cos(yaw + a) * reach))
                         for a in [(-half + 2 * half * i / 12) for i in range(13)]]
            items.append(self.poly(pts, outer, 0.45 * fade + 0.1))
        elif key == "whip":
            yaw = math.atan2(d[0], d[2])
            half = math.radians(fx["arc"]) / 2
            sweep = min(1.0, u * 1.8)
            # a long curving ribbon from the fighter, its far end sweeping right -> left across the arc
            end_angle = yaw + half - 2 * half * sweep
            pts = []
            hand = o
            if fighter is not None:
                hand = joints_of(fighter)[J["RightHand"]]
            for i in range(16):
                s = i / 15
                a = end_angle + (1 - s) * 0.5 * (1 if sweep < 1 else 0.3) * (0.6 - s)
                r = fx["range"] * s
                pts.append(add(hand, (math.sin(a) * r, -0.3 * s * s, math.cos(a) * r)))
            items.append(self.ribbon(pts, outer, fade))
        elif key in ("wheel",):
            items.append(self.ring((o[0], 0.02, o[2]), fx["range"] * min(1.0, 0.2 + u * 1.5), outer, 0.6 * fade + 0.15, 0.25))
        elif key in ("slam", "stomp"):
            items.append(self.ring((o[0], 0.02, o[2]), max(0.5, fx["range"]) * min(1.0, 0.2 + u * 2), outer, 0.6 * fade + 0.1, 0.25))
            if key == "slam":
                items.append(self.flame_ball(add(o, (0, 0.3 * (1 - u), 0)), 0.45 * fade + 0.1, fade, outer, hot))
            else:
                # cracks running out from the impact
                for k in range(6):
                    a = k * math.pi / 3 + 0.4
                    r = max(0.6, fx["range"]) * min(1.0, 0.3 + u * 2)
                    items.append(self.streak((o[0], 0.02, o[2]), (o[0] + math.sin(a) * r, 0.02, o[2] + math.cos(a) * r), fade, hot, 0.05))
        elif key == "pillar":
            height = 3.2 * min(1.0, u * 2.5)
            items.append(self.pillar((o[0], 0.0, o[2]), height, 0.3, fade, outer, hot))
        elif key in ("burst", "dust"):
            # dust: Earth off the ground (no rock, canon): a fainter, wider puff
            if fighter is not None:
                p = joints_of(fighter)[fx["joint"]]
                reach = min(fx["range"], 3.5) * 0.35
                soft = 0.5 if key == "dust" else 1.0
                items.append(self.flame_ball(add(p, mul(d, reach * u)), (0.18 + 0.25 * u) * (1.4 if key == "dust" else 1.0), fade * soft, outer, hot))
        elif key == "trail":
            hist = self.history.get(fx["fighter"], [])
            pts = [h[fx["joint"]] for h in hist[-8:]]
            if len(pts) >= 2:
                items.append(self.ribbon(pts, hot, fade, width=0.12))
            if fighter is not None:
                items.append(self.flame_ball(joints_of(fighter)[fx["joint"]], 0.12, fade, outer, hot))
        elif key == "wave":
            # a crescent surge travelling out along the strike
            yaw = math.atan2(d[0], d[2])
            half = math.radians(min(max(fx["arc"], 40), 140)) / 2
            r = max(1.0, fx["range"]) * min(1.0, 0.25 + u * 1.4)
            inner = max(0.2, r - 0.5)
            arc_out = [add(o, (math.sin(yaw + a) * r, -0.6 + 0.5 * math.cos(a / max(half, 1e-3) * 1.4), math.cos(yaw + a) * r))
                       for a in [(-half + 2 * half * i / 10) for i in range(11)]]
            arc_in = [add(o, (math.sin(yaw + a) * inner, -0.8, math.cos(yaw + a) * inner)) for a in [(half - 2 * half * i / 10) for i in range(11)]]
            items.append(self.poly(arc_out + arc_in, outer, 0.45 * fade + 0.1))
        elif key == "line":
            # a straight strip along the ground from the fighter, with spikes popping up along it
            base = (o[0], 0.02, o[2])
            flat = norm((d[0], 0.0, d[2])) if (d[0] or d[2]) else (0, 0, 1)
            reach = fx["range"] * min(1.0, 0.2 + u * 2)
            items.append(self.streak(base, add(base, mul(flat, reach)), fade, outer, 0.22))
            n = max(2, int(reach / 0.8))
            for k in range(1, n + 1):
                p = add(base, mul(flat, reach * k / n))
                items.append(self.streak(p, add(p, (0, 0.5 * fade + 0.1, 0)), fade, hot, 0.07))
        elif key == "dome":
            # a shell round the body: a ground ring and three arcs over the top
            centre = (o[0], 0.0, o[2])
            r = max(1.0, fx["range"]) * min(1.0, 0.3 + u * 2)
            items.append(self.ring((centre[0], 0.02, centre[2]), r, outer, 0.55 * fade + 0.1, 0.2))
            for k in range(3):
                a = k * math.pi / 3
                pts = [add(centre, (math.sin(a) * r * math.cos(t), r * math.sin(t) * 0.9, math.cos(a) * r * math.cos(t)))
                       for t in [i * math.pi / 12 for i in range(13)]]
                items.append(self.ribbon(pts, hot, fade * 0.8, width=0.08))
        elif key == "vortex":
            # a spiral winding round the fighter (or the strike's origin), turning as it fades
            centre = (o[0], 0.0, o[2])
            if fighter is not None:
                hips = joints_of(fighter)[J["Hips"]]
                centre = (hips[0], 0.0, hips[2])
            r = max(0.6, min(fx["range"], 4.0)) * 0.4
            spin = u * 6.0
            pts = [add(centre, (math.sin(spin + t) * r * (0.4 + 0.6 * t / 9.4), 0.2 + 1.6 * t / 9.4, math.cos(spin + t) * r * (0.4 + 0.6 * t / 9.4)))
                   for t in [i * 0.4 for i in range(24)]]
            items.append(self.ribbon(pts, outer, fade, width=0.1))
        elif key == "shards":
            # a few small fast pieces fanning out along the strike
            for k in (-1, 0, 1):
                side = norm(cross(d, (0, 1, 0)))
                a = add(o, mul(side, 0.12 * k))
                b = add(a, add(mul(d, 1.2 * u + 0.3), mul(side, 0.25 * k * u)))
                items.append(self.streak(a, b, fade, hot, 0.05))
        elif key == "switch":
            # the element switch: a flash ring round the chest in the new element's colour
            items.append(self.ring((o[0], o[1], o[2]), 0.3 + 0.5 * u, outer, 0.7 * fade, 0.06))
        elif key == "jet":
            if fighter is not None:
                js = joints_of(fighter)
                for foot in ("LeftFoot", "RightFoot"):
                    p = js[J[foot]]
                    items.append(self.streak(p, add(p, mul(d, 0.9 * fade + 0.2)), fade, outer))
        elif key in ("muzzle", "spark"):
            items.append(self.flame_ball(o, 0.25 * (1 - u) + 0.05, fade, outer, hot))
        elif key == "embers":
            if fighter is not None:
                hist = self.history.get(fx["fighter"], [])
                pts = [h[J["Chest"]] for h in hist[-10:]]
                if len(pts) >= 2:
                    items.append(self.ribbon(pts, "#ff5a10", fade * 0.8, width=0.08))
        return items

    def poly(self, pts3, colour, alpha):
        pts = self.cam.clip_polygon(pts3)
        if len(pts) < 3:
            return 0.0, lambda ax: None
        depth = sum(p[2] for p in pts) / len(pts)

        def paint(ax):
            ax.add_patch(Polygon([(p[0], p[1]) for p in pts], closed=True, color=colour, alpha=max(0.0, min(1.0, alpha)), lw=0, zorder=6))
        return depth - 0.5, paint

    def ribbon(self, pts3, colour, alpha, width=0.16):
        pts = [self.cam.project(p) for p in pts3]
        depth = sum(p[2] for p in pts) / len(pts)

        def paint(ax):
            for i in range(len(pts) - 1):
                a, b = pts[i], pts[i + 1]
                k = (i + 1) / len(pts)
                ax.plot([a[0], b[0]], [a[1], b[1]], color=colour, alpha=max(0.0, min(1.0, alpha * (0.35 + 0.65 * k))),
                        lw=self.cam.scale(width * (0.4 + k), a[2]), solid_capstyle="round", zorder=7)
        return depth - 0.3, paint

    def ring(self, centre, radius, colour, alpha, width):
        ring3 = [(centre[0] + math.sin(a) * radius, centre[1], centre[2] + math.cos(a) * radius)
                 for a in [i * 2 * math.pi / 40 for i in range(41)]]
        cams = [self.cam.to_camera(p) for p in ring3]
        depth = self.cam.project(centre)[2]

        def paint(ax):
            for i in range(len(cams) - 1):
                a, b = cams[i], cams[i + 1]
                if a[2] < self.cam.NEAR or b[2] < self.cam.NEAR:
                    continue
                pa, pb = self.cam.from_camera(a), self.cam.from_camera(b)
                ax.plot([pa[0], pb[0]], [pa[1], pb[1]], color=colour, alpha=max(0.0, min(1.0, alpha)),
                        lw=self.cam.scale(width, pa[2]), solid_capstyle="round", zorder=2)
        return depth + 5.0, paint

    def pillar(self, base, height, radius, alpha, outer=FIRE, hot=FIRE_HOT):
        bottom, top = self.cam.project(base), self.cam.project(add(base, (0, height, 0)))
        w = self.cam.scale(radius, bottom[2])

        def paint(ax):
            ax.add_patch(Polygon([(bottom[0] - w, bottom[1]), (bottom[0] + w, bottom[1]), (top[0] + w * 0.4, top[1]), (top[0] - w * 0.4, top[1])],
                                 closed=True, color=outer, alpha=0.35 * alpha, lw=0, zorder=6))
            ax.add_patch(Polygon([(bottom[0] - w * 0.4, bottom[1]), (bottom[0] + w * 0.4, bottom[1]), (top[0], top[1])],
                                 closed=True, color=hot, alpha=0.45 * alpha, lw=0, zorder=6))
        return bottom[2] - 0.2, paint

    def flame_ball(self, p, radius, alpha, outer=FIRE, hot=FIRE_HOT):
        x, y, z = self.cam.project(p)
        r = self.cam.scale(radius, z)

        def paint(ax):
            ax.add_patch(Circle((x, y), r, color=outer, alpha=max(0.0, min(1.0, 0.55 * alpha + 0.05)), lw=0, zorder=7))
            ax.add_patch(Circle((x, y), r * 0.5, color=hot, alpha=max(0.0, min(1.0, 0.7 * alpha + 0.05)), lw=0, zorder=7))
        return z - 0.4, paint

    def streak(self, a, b, alpha, colour=FIRE, width=0.14):
        pa, pb = self.cam.project(a), self.cam.project(b)

        def paint(ax):
            ax.plot([pa[0], pb[0]], [pa[1], pb[1]], color=colour, alpha=max(0.0, min(1.0, 0.8 * alpha)), lw=self.cam.scale(width, pa[2]),
                    solid_capstyle="round", zorder=7)
        return pa[2] - 0.2, paint

    def projectile_item(self, p):
        x, y, z = self.cam.project((p[0], p[1], p[2]))
        player = p[3] == 1
        outer, hot = element_colours(p[4] if len(p) > 4 else "fire")
        r = self.cam.scale(0.3 if player else 0.08, z)

        def paint(ax):
            ax.add_patch(Circle((x, y), r, color=outer if player else "#cfcfcf", alpha=0.9, lw=0, zorder=8))
            if player:
                ax.add_patch(Circle((x, y), r * 0.5, color=hot, alpha=0.9, lw=0, zorder=8))
        return z - 0.5, paint


def new_figure(w, h, dpi=100):
    fig = plt.figure(figsize=(w / dpi, h / dpi), dpi=dpi)
    ax = fig.add_axes([0, 0, 1, 1])
    return fig, ax


def frame_indices(frames, scene_filter, start, end):
    idx = [i for i, f in enumerate(frames) if (not scene_filter or scene_filter.lower() in f["scene"].lower())]
    if start is not None:
        idx = [i for i in idx if i >= start]
    if end is not None:
        idx = [i for i in idx if i <= end]
    return idx


def render_video(data, out, step, scene_filter, start, end, width, height, follow="player"):
    import imageio.v2 as imageio
    import numpy as np

    renderer = Renderer(data, width, height, follow)
    idx = frame_indices(data["frames"], scene_filter, start, end)
    if not idx:
        sys.exit("no frames match")
    fps = data.get("fps", 60) / step
    writer = imageio.get_writer(out, fps=fps, codec="libx264", quality=7, macro_block_size=8)
    fig, ax = new_figure(width, height)
    last_scene = None
    for n, i in enumerate(idx):
        frame = data["frames"][i]
        if frame["scene"] != last_scene:
            renderer.reset_state()
            snap = True
            last_scene = frame["scene"]
        else:
            snap = False
        renderer.advance(i)
        if n % step:
            continue
        ax.clear()
        renderer.draw(ax, i, snap_camera=snap)
        fig.canvas.draw()
        image = np.asarray(fig.canvas.buffer_rgba())[:, :, :3]
        writer.append_data(image)
    writer.close()
    plt.close(fig)
    print("wrote", out, "(%d frames at %.0f fps)" % (len(idx[::step]), fps))


def render_sheet(data, out, picks, cols, width, height, follow="player"):
    renderer = Renderer(data, width, height, follow)
    rows = (len(picks) + cols - 1) // cols
    dpi = 100
    fig = plt.figure(figsize=(cols * width / dpi, rows * height / dpi), dpi=dpi)
    for n, i in enumerate(picks):
        ax = fig.add_axes([(n % cols) / cols, 1 - (n // cols + 1) / rows, 1 / cols, 1 / rows])
        renderer.warm_up(i)
        renderer.draw(ax, i, snap_camera=True)
        ax.text(width - 10, 24, "#%d" % i, color="#9aa0a6", fontsize=8, ha="right")
    fig.savefig(out, facecolor="#20232a")
    plt.close(fig)
    print("wrote", out, "(%d frames)" % len(picks))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("json", help="output of: dotnet run --project tools/CombatSim -- anim --out file.json")
    parser.add_argument("--mp4", help="write a video here")
    parser.add_argument("--sheet", help="write a contact sheet PNG here")
    parser.add_argument("--scene", help="only frames whose scene title contains this text")
    parser.add_argument("--start", type=int, help="first frame index")
    parser.add_argument("--end", type=int, help="last frame index")
    parser.add_argument("--frames", help="contact sheet: comma-separated frame indices")
    parser.add_argument("--count", type=int, default=12, help="contact sheet: how many frames (evenly spread)")
    parser.add_argument("--cols", type=int, default=4, help="contact sheet: columns")
    parser.add_argument("--step", type=int, default=2, help="video: render every Nth frame (2 = 30 fps)")
    parser.add_argument("--follow", default="player", help="camera follows this kind of fighter: player, soldier, crossbow, dummy")
    parser.add_argument("--size", default="960x540", help="frame size, e.g. 960x540 (video) or 480x300 (sheet cells)")
    args = parser.parse_args()

    with open(args.json) as fh:
        data = json.load(fh)
    width, height = (int(v) for v in args.size.lower().split("x"))
    if args.mp4:
        render_video(data, args.mp4, max(1, args.step), args.scene, args.start, args.end, width, height, args.follow)
    if args.sheet:
        if args.frames:
            picks = [int(v) for v in args.frames.split(",") if v.strip()]
        else:
            idx = frame_indices(data["frames"], args.scene, args.start, args.end)
            count = max(1, min(args.count, len(idx)))
            picks = [idx[int(k * (len(idx) - 1) / max(1, count - 1))] for k in range(count)] if count > 1 else idx[:1]
        render_sheet(data, args.sheet, picks, max(1, args.cols), width, height, args.follow)
    if not args.mp4 and not args.sheet:
        parser.error("say --mp4 and/or --sheet")


if __name__ == "__main__":
    main()
