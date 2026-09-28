using UnityEngine;

namespace VaatusRevenge.EditorTools
{
    // Building blocks for grey-box level geometry, used by ArenaBuilder. Solid pieces keep the BoxCollider
    // CreatePrimitive gives them (fighters and the camera collide with them) and stay on the Default layer,
    // which is what Layers.EnvironmentMask means by "solid world". Floor markings get no collider, so they
    // can never trip a CharacterController or pull the camera in.
    public static class ArenaGeometry
    {
        public const int EnvironmentLayer = 0; // Default

        public static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // Solid box by its centre and size.
        public static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = EnvironmentLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            SetMaterial(go, material);
            return go;
        }

        // Solid box standing on the floor (bottomCenter.y is where its base sits).
        public static GameObject BoxOnFloor(string name, Transform parent, Vector3 bottomCenter, Vector3 size, Material material)
        {
            return Box(name, parent, bottomCenter + new Vector3(0f, size.y * 0.5f, 0f), size, material);
        }

        // A solid slab whose walking surface runs along its centre line from 'top' down to 'bottom'.
        public static GameObject Ramp(string name, Transform parent, Vector3 top, Vector3 bottom, float width, float thickness, Material material)
        {
            Vector3 along = bottom - top;
            float length = along.magnitude;
            if (length < 1e-3f) return null;
            Quaternion rotation = Quaternion.LookRotation(along / length, Vector3.up);
            Vector3 surfaceUp = rotation * Vector3.up;
            Vector3 center = (top + bottom) * 0.5f - surfaceUp * (thickness * 0.5f);
            GameObject go = Box(name, parent, center, new Vector3(width, thickness, length), material);
            go.transform.localRotation = rotation;
            return go;
        }

        // A thin floor marking (no collider, no shadow). topHeight is how far its top sits above the floor;
        // give overlapping markings different heights so they never flicker against each other.
        public static GameObject Strip(string name, Transform parent, Vector3 floorCenter, float sizeX, float sizeZ, float topHeight, Material material)
        {
            GameObject go = Box(name, parent, new Vector3(floorCenter.x, floorCenter.y + topHeight * 0.5f, floorCenter.z),
                new Vector3(sizeX, topHeight, sizeZ), material);
            MakeDecal(go);
            return go;
        }

        // A flat disc marking (no collider). The cylinder primitive is 2 units tall, hence the halving.
        public static GameObject Disc(string name, Transform parent, Vector3 floorCenter, float radius, float topHeight, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.layer = EnvironmentLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(floorCenter.x, floorCenter.y + topHeight * 0.5f, floorCenter.z);
            go.transform.localScale = new Vector3(radius * 2f, topHeight * 0.5f, radius * 2f);
            SetMaterial(go, material);
            MakeDecal(go);
            return go;
        }

        // An empty marker transform: position = feet, rotation = facing (yaw in degrees).
        public static Transform Point(string name, Transform parent, Vector3 position, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        // Yaw (Unity convention: 0 = +Z, 90 = +X) that looks from one point towards another.
        public static float YawTowards(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            if (d.x * d.x + d.z * d.z < 1e-8f) return 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static void MakeDecal(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void SetMaterial(GameObject go, Material material)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }
    }
}
