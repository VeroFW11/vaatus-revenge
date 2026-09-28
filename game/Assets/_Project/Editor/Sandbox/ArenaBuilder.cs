using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VaatusRevenge.EditorTools
{
    // Builds the grey-box combat sandbox arena: a 60 x 60 m walled floor with areas for testing specific
    // things. Seen from the player spawn (south, facing north):
    //   - Training area (south): sparring dummies in front of the player, and to the left a 12 x 12 m
    //     measuring pad with a line every metre, for judging dodge and lunge distances by eye.
    //   - 5 m grid lines across the whole floor for a general sense of distance.
    //   - Pillar field (south-east): camera collision and line-of-sight tests.
    //   - Duel ring (centre): where the dao soldiers stand.
    //   - Low-ceiling corridor (north-east): camera behaviour in tight spaces.
    //   - Raised platform (north-west) with a ramp and stairs: a crossbowman up high tests camera pitch.
    // Deterministic: no randomness, so every build gives exactly the same layout.
    public static class ArenaBuilder
    {
        public const string RootName = "Arena";

        // Level layout in metres (arena-local: floor top at y = 0, centre at the origin). These are level
        // design numbers, not gameplay tuning.
        const float FloorSize = 60f;
        const float WallHeight = 4f;
        const float WallThickness = 1f;
        const float MajorGridSpacing = 5f;
        const float MajorLineWidth = 0.08f;
        const float MajorLineTop = 0.012f;
        const float MinorLineWidth = 0.04f;
        const float MinorLineTop = 0.008f;
        static readonly Vector2 PadMin = new Vector2(-18f, -26f); // measuring pad corners (x, z)
        static readonly Vector2 PadMax = new Vector2(-6f, -14f);
        const float RingRadius = 7f;
        const float RingBorderWidth = 0.3f;
        static readonly Vector3 PlatformCenter = new Vector3(-13f, 0f, 12f);
        const float PlatformSize = 6f;
        const float PlatformHeight = 2.5f;
        const float RampLength = 6.2f;   // horizontal run: about a 22 degree slope, walkable for a CharacterController
        const float RampWidth = 3f;
        const float StepRise = 0.25f;    // below a CharacterController's default 0.3 m step offset
        const float StepRun = 0.4f;
        const float StairWidth = 2.5f;
        static readonly Vector3 CorridorCenter = new Vector3(18f, 0f, 12f);
        const float CorridorWidth = 3f;
        const float CorridorLength = 12f;
        const float CorridorHeight = 2.6f;

        static readonly Vector3 PlayerSpawn = new Vector3(0f, 0f, -18f);

        // Builds the arena under parent (null = scene root) and returns its layout. An arena already built
        // under the same parent is replaced, so running the builder twice never stacks two arenas.
        public static ArenaLayout Build(Transform parent)
        {
            RemoveExisting(parent);

            var rootObject = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootObject, "Build Arena");
            Transform root = rootObject.transform;
            root.SetParent(parent, false);
            ArenaLayout layout = rootObject.AddComponent<ArenaLayout>();

            Materials m = Materials.Load();
            BuildFloorAndWalls(root, m);
            BuildMarkings(root, m);
            BuildPillarField(root, m);
            BuildCorridor(root, m);
            BuildPlatform(root, m);
            layout.Configure(new Vector2(FloorSize, FloorSize), BuildSpawnPoints(root));
            return layout;
        }

        static void RemoveExisting(Transform parent)
        {
            var existing = new List<GameObject>();
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++) existing.Add(parent.GetChild(i).gameObject);
            }
            else
            {
                Scene scene = SceneManager.GetActiveScene();
                if (scene.IsValid()) existing.AddRange(scene.GetRootGameObjects());
            }
            for (int i = 0; i < existing.Count; i++)
            {
                GameObject go = existing[i];
                if (go != null && go.name == RootName && go.GetComponent<ArenaLayout>() != null) Undo.DestroyObjectImmediate(go);
            }
        }

        static void BuildFloorAndWalls(Transform root, Materials m)
        {
            ArenaGeometry.Box("Floor", root, new Vector3(0f, -0.5f, 0f), new Vector3(FloorSize, 1f, FloorSize), m.Floor);
            Transform walls = ArenaGeometry.Group("Walls", root);
            float half = FloorSize * 0.5f + WallThickness * 0.5f;
            float span = FloorSize + WallThickness * 2f;
            ArenaGeometry.BoxOnFloor("Wall_North", walls, new Vector3(0f, 0f, half), new Vector3(span, WallHeight, WallThickness), m.Wall);
            ArenaGeometry.BoxOnFloor("Wall_South", walls, new Vector3(0f, 0f, -half), new Vector3(span, WallHeight, WallThickness), m.Wall);
            ArenaGeometry.BoxOnFloor("Wall_East", walls, new Vector3(half, 0f, 0f), new Vector3(WallThickness, WallHeight, FloorSize), m.Wall);
            ArenaGeometry.BoxOnFloor("Wall_West", walls, new Vector3(-half, 0f, 0f), new Vector3(WallThickness, WallHeight, FloorSize), m.Wall);
        }

        static void BuildMarkings(Transform root, Materials m)
        {
            Transform markings = ArenaGeometry.Group("FloorMarkings", root);

            // Every 5 m across the whole floor. Slightly taller than the 1 m lines so crossings never flicker.
            Transform grid = ArenaGeometry.Group("Grid_5m", markings);
            int lines = Mathf.FloorToInt(FloorSize * 0.5f / MajorGridSpacing - 0.01f);
            for (int i = -lines; i <= lines; i++)
            {
                float p = i * MajorGridSpacing;
                ArenaGeometry.Strip("X_" + p.ToString("0"), grid, new Vector3(p, 0f, 0f), MajorLineWidth, FloorSize, MajorLineTop, m.MajorLine);
                ArenaGeometry.Strip("Z_" + p.ToString("0"), grid, new Vector3(0f, 0f, p), FloorSize, MajorLineWidth, MajorLineTop, m.MajorLine);
            }

            // The measuring pad: a line every metre (the 5 m grid lines through it double as its 5 m marks).
            Transform pad = ArenaGeometry.Group("MeasuringPad_1m", markings);
            Vector2 padCenter = (PadMin + PadMax) * 0.5f;
            Vector2 padSize = PadMax - PadMin;
            for (int x = Mathf.RoundToInt(PadMin.x); x <= Mathf.RoundToInt(PadMax.x); x++)
            {
                if (x % Mathf.RoundToInt(MajorGridSpacing) == 0) continue; // already a 5 m line
                ArenaGeometry.Strip("X_" + x, pad, new Vector3(x, 0f, padCenter.y), MinorLineWidth, padSize.y, MinorLineTop, m.MinorLine);
            }
            for (int z = Mathf.RoundToInt(PadMin.y); z <= Mathf.RoundToInt(PadMax.y); z++)
            {
                if (z % Mathf.RoundToInt(MajorGridSpacing) == 0) continue;
                ArenaGeometry.Strip("Z_" + z, pad, new Vector3(padCenter.x, 0f, z), padSize.x, MinorLineWidth, MinorLineTop, m.MinorLine);
            }

            // Duel ring: a sand-coloured disc with a darker border, sitting on top of the grid lines.
            Transform ring = ArenaGeometry.Group("DuelRing", markings);
            ArenaGeometry.Disc("RingBorder", ring, Vector3.zero, RingRadius + RingBorderWidth, 0.016f, m.RingBorder);
            ArenaGeometry.Disc("RingFloor", ring, Vector3.zero, RingRadius, 0.02f, m.RingFloor);
        }

        static void BuildPillarField(Transform root, Materials m)
        {
            // Pillars of two widths on a 5 m grid: things to lose line of sight behind and for the camera to dodge.
            Transform field = ArenaGeometry.Group("PillarField", root);
            float[] xs = { 12f, 17f, 22f };
            float[] zs = { -24f, -19f, -14f, -9f };
            for (int i = 0; i < xs.Length; i++)
            {
                for (int j = 0; j < zs.Length; j++)
                {
                    float width = (i + j) % 2 == 0 ? 1f : 1.6f;
                    ArenaGeometry.BoxOnFloor("Pillar_" + i + "_" + j, field, new Vector3(xs[i], 0f, zs[j]), new Vector3(width, WallHeight, width), m.Pillar);
                }
            }
        }

        static void BuildCorridor(Transform root, Materials m)
        {
            // Open at both ends, 3 m wide inside and 2.6 m high: the camera must pull in without jitter.
            Transform corridor = ArenaGeometry.Group("LowCorridor", root);
            const float wall = 0.5f;
            const float roof = 0.3f;
            float sideOffset = CorridorWidth * 0.5f + wall * 0.5f;
            ArenaGeometry.BoxOnFloor("Wall_West", corridor, CorridorCenter + new Vector3(-sideOffset, 0f, 0f),
                new Vector3(wall, CorridorHeight, CorridorLength), m.Corridor);
            ArenaGeometry.BoxOnFloor("Wall_East", corridor, CorridorCenter + new Vector3(sideOffset, 0f, 0f),
                new Vector3(wall, CorridorHeight, CorridorLength), m.Corridor);
            ArenaGeometry.BoxOnFloor("Roof", corridor, CorridorCenter + new Vector3(0f, CorridorHeight, 0f),
                new Vector3(CorridorWidth + wall * 2f, roof, CorridorLength), m.Corridor);
        }

        static void BuildPlatform(Transform root, Materials m)
        {
            Transform platform = ArenaGeometry.Group("RaisedPlatform", root);
            float half = PlatformSize * 0.5f;
            ArenaGeometry.BoxOnFloor("Block", platform, PlatformCenter, new Vector3(PlatformSize, PlatformHeight, PlatformSize), m.Platform);

            // Ramp down the east side (towards the duel ring).
            var rampTop = new Vector3(PlatformCenter.x + half, PlatformHeight, PlatformCenter.z);
            var rampBottom = new Vector3(PlatformCenter.x + half + RampLength, 0f, PlatformCenter.z);
            ArenaGeometry.Ramp("Ramp", platform, rampTop, rampBottom, RampWidth, 0.4f, m.Stairs);

            // Stairs down the north side: solid blocks, each one step lower and further out.
            int steps = Mathf.RoundToInt(PlatformHeight / StepRise) - 1;
            for (int k = 1; k <= steps; k++)
            {
                float height = PlatformHeight - StepRise * k;
                float z = PlatformCenter.z + half + StepRun * (k - 0.5f);
                ArenaGeometry.BoxOnFloor("Step_" + k, platform, new Vector3(PlatformCenter.x, 0f, z), new Vector3(StairWidth, height, StepRun), m.Stairs);
            }
        }

        static List<Transform> BuildSpawnPoints(Transform root)
        {
            Transform group = ArenaGeometry.Group("SpawnPoints", root);
            var points = new List<Transform>();
            Vector3 ringCenter = Vector3.zero;

            points.Add(ArenaGeometry.Point(ArenaLayout.PlayerSpawn, group, PlayerSpawn, 0f));

            // Dummies a few steps in front of the player, facing them.
            AddFacing(points, group, ArenaLayout.Dummy1, new Vector3(0f, 0f, -13.5f), PlayerSpawn);
            AddFacing(points, group, ArenaLayout.Dummy2, new Vector3(-3.5f, 0f, -12.5f), PlayerSpawn);
            AddFacing(points, group, ArenaLayout.Dummy3, new Vector3(3.5f, 0f, -12.5f), PlayerSpawn);

            // Soldiers in the duel ring, facing the direction the player arrives from.
            points.Add(ArenaGeometry.Point(ArenaLayout.Soldier1, group, new Vector3(-2.5f, 0f, 2f), 180f));
            points.Add(ArenaGeometry.Point(ArenaLayout.Soldier2, group, new Vector3(2.5f, 0f, 2f), 180f));

            // One crossbowman on open ground about 12 m from the ring centre, one up on the platform.
            AddFacing(points, group, ArenaLayout.CrossbowGround, new Vector3(8f, 0f, 9f), ringCenter);
            AddFacing(points, group, ArenaLayout.CrossbowPlatform, new Vector3(PlatformCenter.x, PlatformHeight, PlatformCenter.z), ringCenter);
            return points;
        }

        static void AddFacing(List<Transform> points, Transform group, string name, Vector3 position, Vector3 lookAt)
        {
            points.Add(ArenaGeometry.Point(name, group, position, ArenaGeometry.YawTowards(position, lookAt)));
        }

        // The arena's material assets (created in Assets/_Project/Materials on first build).
        sealed class Materials
        {
            public Material Floor, Wall, Pillar, Platform, Stairs, Corridor, MajorLine, MinorLine, RingFloor, RingBorder;

            public static Materials Load()
            {
                return new Materials
                {
                    Floor = GreyboxMaterials.GetOrCreate("Floor", new Color(0.36f, 0.37f, 0.39f), false),
                    Wall = GreyboxMaterials.GetOrCreate("Wall", new Color(0.52f, 0.54f, 0.58f), false),
                    Pillar = GreyboxMaterials.GetOrCreate("Pillar", new Color(0.58f, 0.52f, 0.45f), false),
                    Platform = GreyboxMaterials.GetOrCreate("Platform", new Color(0.45f, 0.5f, 0.42f), false),
                    Stairs = GreyboxMaterials.GetOrCreate("Stairs", new Color(0.52f, 0.57f, 0.48f), false),
                    Corridor = GreyboxMaterials.GetOrCreate("Corridor", new Color(0.47f, 0.43f, 0.52f), false),
                    MajorLine = GreyboxMaterials.GetOrCreate("FloorLine_5m", new Color(0.85f, 0.85f, 0.8f), true),
                    MinorLine = GreyboxMaterials.GetOrCreate("FloorLine_1m", new Color(0.62f, 0.64f, 0.67f), false),
                    RingFloor = GreyboxMaterials.GetOrCreate("DuelRing", new Color(0.62f, 0.52f, 0.38f), false),
                    RingBorder = GreyboxMaterials.GetOrCreate("DuelRingBorder", new Color(0.45f, 0.14f, 0.1f), false)
                };
            }
        }
    }
}
