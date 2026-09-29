using System.Collections.Generic;
using UnityEngine;

namespace VaatusRevenge
{
    // Sits on the root of the grey-box arena and remembers its named spawn points, so the sandbox builder
    // can place fighters and the sandbox director can respawn or reset them at runtime without searching
    // the scene. The arena geometry itself is built by the editor tool ArenaBuilder.
    [DisallowMultipleComponent]
    public class ArenaLayout : MonoBehaviour
    {
        public const string PlayerSpawn = "Player";
        public const string Dummy1 = "Dummy_1";
        public const string Dummy2 = "Dummy_2";
        public const string Dummy3 = "Dummy_3";
        public const string Soldier1 = "Soldier_1";
        public const string Soldier2 = "Soldier_2";
        public const string CrossbowGround = "Crossbow_Ground";
        public const string CrossbowPlatform = "Crossbow_Platform";

        [Tooltip("Named spawn points. Each point's position is where a fighter's feet go; its forward is the facing.")]
        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        [Tooltip("Floor size in metres (the arena is centred on this object).")]
        [SerializeField] private Vector2 floorSize = new Vector2(60f, 60f);

        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
        public Vector2 FloorSize => floorSize;

        public Transform Player => GetSpawnPoint(PlayerSpawn);
        public Transform DummySpawn1 => GetSpawnPoint(Dummy1);
        public Transform DummySpawn2 => GetSpawnPoint(Dummy2);
        public Transform DummySpawn3 => GetSpawnPoint(Dummy3);
        public Transform SoldierSpawn1 => GetSpawnPoint(Soldier1);
        public Transform SoldierSpawn2 => GetSpawnPoint(Soldier2);
        public Transform CrossbowGroundSpawn => GetSpawnPoint(CrossbowGround);
        public Transform CrossbowPlatformSpawn => GetSpawnPoint(CrossbowPlatform);

        // Null when there is no point with that name.
        public Transform GetSpawnPoint(string pointName)
        {
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                Transform point = spawnPoints[i];
                if (point != null && point.name == pointName) return point;
            }
            return null;
        }

        public bool TryGetSpawnPoint(string pointName, out Transform point)
        {
            point = GetSpawnPoint(pointName);
            return point != null;
        }

        // Yaw in degrees (Unity convention: 0 faces +Z, 90 faces +X) of a spawn point, 0 if missing.
        public float GetSpawnYaw(string pointName)
        {
            Transform point = GetSpawnPoint(pointName);
            return point != null ? point.eulerAngles.y : 0f;
        }

        // For editor tools: adds a point or replaces the one with the same name.
        public void SetSpawnPoint(Transform point)
        {
            if (point == null) return;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (spawnPoints[i] != null && spawnPoints[i].name == point.name)
                {
                    spawnPoints[i] = point;
                    return;
                }
            }
            spawnPoints.Add(point);
        }

        public void Configure(Vector2 newFloorSize, IEnumerable<Transform> points)
        {
            floorSize = newFloorSize;
            spawnPoints.Clear();
            if (points == null) return;
            foreach (Transform point in points) SetSpawnPoint(point);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Spawn markers in the Scene view: a disc for the feet and a line for the facing.
            Gizmos.color = new Color(1f, 0.8f, 0.2f);
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                Transform point = spawnPoints[i];
                if (point == null) continue;
                Gizmos.DrawWireSphere(point.position + Vector3.up * 0.05f, 0.4f);
                Gizmos.DrawLine(point.position + Vector3.up * 0.05f, point.position + Vector3.up * 0.05f + point.forward);
            }
        }
#endif
    }
}
