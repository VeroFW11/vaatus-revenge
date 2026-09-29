using System.Collections.Generic;
using UnityEngine;

namespace VaatusRevenge
{
    // Simple procedural meshes for the fighters' grey-box bodies, built in code so no art is needed yet:
    //   TaperedCapsule: a limb segment, thicker at one end (upper arm, forearm, thigh, shin).
    //   RoundedBox:     a box with softened edges (pelvis, chest, fists, feet, helmet), a "superellipsoid".
    // Meshes are cached by their dimensions and shared by every fighter, so ten soldiers don't mean ten copies.
    // Built in edit mode by the sandbox builder they're saved inside the scene file along with the fighters.
    public static class BodyMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        // Domain reload is off in this project: forget meshes from the last Play session (they may be destroyed).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache.Clear();
        }

        // A capsule along +Y from y = 0 to y = length: radius r0 at the bottom, r1 at the top, rounded ends.
        public static Mesh TaperedCapsule(float r0, float r1, float length, int sides = 12, int capRings = 4)
        {
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "cap_{0:0.###}_{1:0.###}_{2:0.###}", r0, r1, length);
            if (cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            var vertices = new List<Vector3>();
            var rings = new List<int>();   // start index of each ring
            // Bottom cap (a hemisphere of radius r0 centred at y 0), the straight part, top cap (r1 at y = length).
            for (int i = 0; i <= capRings; i++)
            {
                float a = Mathf.PI * 0.5f * (1f - (float)i / capRings);     // from -90 deg (bottom pole) up to 0
                AddRing(vertices, rings, -Mathf.Sin(a) * r0, Mathf.Cos(a) * r0, sides);
            }
            for (int i = 0; i <= capRings; i++)
            {
                float a = Mathf.PI * 0.5f * i / capRings;                    // from 0 up to +90 deg (top pole)
                AddRing(vertices, rings, length + Mathf.Sin(a) * r1, Mathf.Cos(a) * r1, sides);
            }
            Mesh mesh = Stitch("BodyCapsule", vertices, rings, sides);
            cache[key] = mesh;
            return mesh;
        }

        // A rounded box centred on the origin with the given full size. roundness 0.1 = nearly a box, 1 = an ellipsoid.
        public static Mesh RoundedBox(Vector3 size, float roundness = 0.35f, int sides = 16, int bands = 10)
        {
            string key = string.Format(System.Globalization.CultureInfo.InvariantCulture, "box_{0:0.###}_{1:0.###}_{2:0.###}_{3:0.##}",
                size.x, size.y, size.z, roundness);
            if (cache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            float e = Mathf.Clamp(roundness, 0.05f, 1f);
            Vector3 half = size * 0.5f;
            var vertices = new List<Vector3>();
            var rings = new List<int>();
            for (int i = 0; i <= bands; i++)
            {
                float lat = -Mathf.PI * 0.5f + Mathf.PI * i / bands;
                float cy = Power(Mathf.Sin(lat), e);
                float cr = Power(Mathf.Cos(lat), e);
                rings.Add(vertices.Count);
                for (int s = 0; s < sides; s++)
                {
                    float lon = Mathf.PI * 2f * s / sides;
                    vertices.Add(new Vector3(half.x * cr * Power(Mathf.Cos(lon), e), half.y * cy, half.z * cr * Power(Mathf.Sin(lon), e)));
                }
            }
            Mesh mesh = Stitch("BodyRoundedBox", vertices, rings, sides);
            cache[key] = mesh;
            return mesh;
        }

        static float Power(float w, float e)
        {
            return Mathf.Sign(w) * Mathf.Pow(Mathf.Abs(w), e);
        }

        static void AddRing(List<Vector3> vertices, List<int> rings, float y, float radius, int sides)
        {
            rings.Add(vertices.Count);
            for (int s = 0; s < sides; s++)
            {
                float a = Mathf.PI * 2f * s / sides;
                vertices.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
            }
        }

        // Joins consecutive rings of vertices into a closed surface (quads between rings, wrapping around).
        static Mesh Stitch(string name, List<Vector3> vertices, List<int> rings, int sides)
        {
            var triangles = new List<int>();
            for (int r = 0; r < rings.Count - 1; r++)
            {
                int a0 = rings[r], b0 = rings[r + 1];
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    // Wound so the faces point outward in Unity's left-handed space.
                    triangles.Add(a0 + s); triangles.Add(b0 + s); triangles.Add(b0 + s1);
                    triangles.Add(a0 + s); triangles.Add(b0 + s1); triangles.Add(a0 + s1);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
