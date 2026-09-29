using UnityEngine;

namespace VaatusRevenge
{
    // Physics layers, defined once. Set in Project Settings > Tags and Layers:
    //   8 = Player, 9 = Enemy. Solid world geometry stays on Default.
    // Why layers matter here: the camera and line-of-sight checks should hit walls and pillars,
    // not fighters (otherwise the camera jumps forward every time an enemy walks behind you).
    public static class Layers
    {
        static int player = -1;
        static int enemy = -1;

        // Domain reload is off, so the cached layer numbers would survive into the next Play session; forget them in
        // case the layers were renamed in Tags and Layers between sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            player = -1;
            enemy = -1;
        }

        public static int Player
        {
            get
            {
                if (player < 0) player = Resolve("Player", 8);
                return player;
            }
        }

        public static int Enemy
        {
            get
            {
                if (enemy < 0) enemy = Resolve("Enemy", 9);
                return enemy;
            }
        }

        // Solid world geometry (walls, floor, pillars). Camera collision and line-of-sight use this.
        public static int EnvironmentMask => 1 << 0;

        // Sets a layer on an object and all its children (fighters are built from several primitives).
        public static void SetRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform) SetRecursively(child.gameObject, layer);
        }

        static int Resolve(string layerName, int fallback)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? layer : fallback;
        }
    }
}
