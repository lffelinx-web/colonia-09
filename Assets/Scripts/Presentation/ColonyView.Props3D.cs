using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Colonia09.Presentation
{
    /// <summary>
    /// The colony's own 3D pieces, built from boxes, cylinders and domes: street lamps, works sites and scaffolds,
    /// enemy barricades and pylons, cargo drones and the landmark of every zone with its moving parts.
    /// Sizes are in tiles (a 2 x 2 lot holds a building about 1.5 tiles tall). +x is +a, -z is +b; the camera
    /// looks from +x, -z, so fronts face that way.
    /// </summary>
    public sealed partial class ColonyView
    {
        Material propMat, glowMat;

        /// <summary>Lit parts and glowing parts of one piece, in its own coordinates.</summary>
        sealed class Kit
        {
            public readonly MeshBuilder Lit = new MeshBuilder(), Glow = new MeshBuilder();

            public void Box(float x, float z, float y0, float y1, float hx, float hz, Color32 color, bool glow = false) =>
                (glow ? Glow : Lit).Box(new Vector3(x, 0, z), hx, hz, y0, y1, color, Shade(color, .82f));

            public void Cyl(float x, float z, float r, float y0, float y1, Color32 color, int sides = 12, bool glow = false)
            {
                var mb = glow ? Glow : Lit;
                Ring(mb, x, z, r, r, y0, y1, color, sides);
                Cap(mb, x, z, r, y1, color, sides);
            }

            public void Cone(float x, float z, float r0, float r1, float y0, float y1, Color32 color, int sides = 12, bool glow = false) =>
                Ring(glow ? Glow : Lit, x, z, r0, r1, y0, y1, color, sides);

            /// <summary>A half sphere (squashed by <paramref name="squash"/>) standing on y0.</summary>
            public void Dome(float x, float z, float r, float y0, Color32 color, float squash = 1, bool glow = false)
            {
                const int Rings = 5;
                for (int k = 0; k < Rings; k++)
                {
                    float p0 = Mathf.PI * .5f * k / Rings, p1 = Mathf.PI * .5f * (k + 1) / Rings;
                    Ring(glow ? Glow : Lit, x, z, Mathf.Cos(p0) * r, Mathf.Cos(p1) * r, y0 + Mathf.Sin(p0) * r * squash, y0 + Mathf.Sin(p1) * r * squash, color, 16);
                }
            }

            /// <summary>A square bar from one point to another.</summary>
            public void Beam(Vector3 a, Vector3 b, float w, Color32 color, bool glow = false)
            {
                var mb = glow ? Glow : Lit;
                var d = (b - a).normalized;
                var side = Vector3.Cross(d, Mathf.Abs(d.y) > .9f ? Vector3.right : Vector3.up).normalized * (w / 2);
                var up = Vector3.Cross(side, d).normalized * (w / 2);
                Vector3[] c = { side + up, -side + up, -side - up, side - up };
                for (int i = 0; i < 4; i++)
                {
                    var p = c[i]; var q = c[(i + 1) % 4];
                    // Both windings: thin bars are seen from every side.
                    mb.Quad(a + p, b + p, b + q, a + q, color);
                    mb.Quad(a + q, b + q, b + p, a + p, color);
                }
            }

            static void Ring(MeshBuilder mb, float x, float z, float r0, float r1, float y0, float y1, Color32 color, int sides)
            {
                for (int s = 0; s < sides; s++)
                {
                    float t0 = s * Mathf.PI * 2 / sides, t1 = (s + 1) * Mathf.PI * 2 / sides;
                    var a0 = new Vector3(x + Mathf.Cos(t0) * r0, y0, z + Mathf.Sin(t0) * r0);
                    var a1 = new Vector3(x + Mathf.Cos(t1) * r0, y0, z + Mathf.Sin(t1) * r0);
                    var b0 = new Vector3(x + Mathf.Cos(t0) * r1, y1, z + Mathf.Sin(t0) * r1);
                    var b1 = new Vector3(x + Mathf.Cos(t1) * r1, y1, z + Mathf.Sin(t1) * r1);
                    mb.Quad(a0, b0, b1, a1, color);
                }
            }

            static void Cap(MeshBuilder mb, float x, float z, float r, float y, Color32 color, int sides)
            {
                var c = new Vector3(x, y, z);
                for (int s = 0; s < sides; s++)
                {
                    float t0 = s * Mathf.PI * 2 / sides, t1 = (s + 1) * Mathf.PI * 2 / sides;
                    var p = new Vector3(x + Mathf.Cos(t0) * r, y, z + Mathf.Sin(t0) * r);
                    var q = new Vector3(x + Mathf.Cos(t1) * r, y, z + Mathf.Sin(t1) * r);
                    mb.Tri(c, q, p, Vector3.up, color);
                }
            }
        }

        static Color32 Shade(Color32 c, float f) => new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), c.a);

        void EnsurePropMaterials()
        {
            if (propMat != null) return;
            var detail = groundMat != null ? groundMat.GetTexture("_DetailTex") : null;
            propMat = SurfaceMaterial(detail, 3f, .12f, .15f);
            glowMat = SurfaceMaterial(detail, 3f, 0);
            glowMat.SetFloat("_VertexGlow", 1.1f);
        }

        /// <summary>Makes the piece's objects under <paramref name="parent"/> at a world position.</summary>
        Transform Place(Kit kit, string name, Vector3 at, Transform parent = null, bool shadows = true)
        {
            EnsurePropMaterials();
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.position = at;
            void Part(MeshBuilder mb, Material mat, string part)
            {
                if (mb.V.Count == 0) return;
                var child = new GameObject(part);
                child.transform.SetParent(go.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = mb.Build(name + part);
                var r = child.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows && mat == propMat ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            Part(kit.Lit, propMat, "Lit");
            Part(kit.Glow, glowMat, "Glow");
            return go.transform;
        }

        // ---- Moving parts ---------------------------------------------------------------

        sealed class Spinner { public Transform T; public Vector3 Axis; public float Speed, Swing, Phase; public Quaternion Rest; }
        readonly List<Spinner> spinners = new List<Spinner>();

        /// <summary>A part that turns all the time (Swing 0) or swings back and forth this many degrees.</summary>
        void Spin(Transform t, Vector3 axis, float speed, float swing = 0, float phase = 0) =>
            spinners.Add(new Spinner { T = t, Axis = axis, Speed = speed, Swing = swing, Phase = phase, Rest = t.localRotation });

        void TurnSpinners(float now)
        {
            foreach (var s in spinners)
            {
                if (s.T == null) continue;
                float angle = s.Swing > 0 ? Mathf.Sin(now * s.Speed + s.Phase) * s.Swing : now * s.Speed * Mathf.Rad2Deg;
                s.T.localRotation = s.Rest * Quaternion.AngleAxis(angle, s.Axis);
            }
        }

        // ---- Palette ---------------------------------------------------------------------

        static readonly Color32 White = new Color32(222, 229, 232, 255), Steel = new Color32(170, 181, 187, 255), Dark = new Color32(46, 54, 61, 255),
            Concrete = new Color32(128, 136, 140, 255), Yellow = new Color32(224, 172, 26, 255), Cyan = new Color32(94, 232, 255, 255),
            Warm = new Color32(255, 199, 122, 255), Red = new Color32(255, 58, 42, 255), GreenLight = new Color32(125, 255, 120, 255),
            Hazard = new Color32(200, 60, 40, 255), Teal = new Color32(47, 106, 114, 255), Rust = new Color32(138, 75, 47, 255);

        static readonly Color32[] Containers =
        {
            new Color32(193, 74, 50, 255), new Color32(47, 106, 114, 255), new Color32(61, 95, 138, 255), new Color32(201, 162, 58, 255), new Color32(93, 122, 58, 255)
        };

        // ---- Small pieces ------------------------------------------------------------------

        Transform Lamp(Vector3 at)
        {
            var k = new Kit();
            k.Cyl(0, 0, .07f, 0, .06f, Dark, 8);
            k.Cyl(0, 0, .025f, .06f, .85f, Steel, 6);
            k.Box(.08f, -.08f, .82f, .88f, .12f, .05f, Dark);
            k.Box(.13f, -.13f, .79f, .82f, .07f, .04f, Warm, true);
            return Place(k, "Lamp", at, null, false);
        }

        Transform WorksSite(Vector3 lotCenter, float size)
        {
            var k = new Kit();
            float h = size * .47f;
            k.Box(0, 0, 0, .04f, h, h, new Color32(122, 102, 78, 255));
            for (int i = 0; i < 3; i++)
                k.Box(-h * .6f + i * .32f, h * .55f, .04f, .26f, .13f, .13f, new Color32(196, 154, 90, 255));
            k.Box(h * .55f, -h * .5f, .04f, .14f, .25f, .08f, Yellow);
            return Place(k, "Site", lotCenter);
        }

        /// <summary>A scaffold around the lot with a small tower crane over it.</summary>
        Transform Scaffold(Vector3 lotCenter, float size)
        {
            var k = new Kit();
            float h = size * .44f, top = size * .7f;
            var pole = new Color32(150, 118, 60, 255);
            foreach (var (x, z) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                k.Beam(new Vector3(x, 0, z), new Vector3(x, top, z), .05f, pole);
            for (float y = .25f; y < top; y += .25f)
            {
                k.Beam(new Vector3(-h, y, -h), new Vector3(h, y, -h), .03f, Steel);
                k.Beam(new Vector3(h, y, -h), new Vector3(h, y, h), .03f, Steel);
                k.Beam(new Vector3(h, y, h), new Vector3(-h, y, h), .03f, Steel);
                k.Beam(new Vector3(-h, y, h), new Vector3(-h, y, -h), .03f, Steel);
            }
            float cx = -h * .7f, cz = h * .7f, mast = size * 1.05f;
            k.Beam(new Vector3(cx, 0, cz), new Vector3(cx, mast, cz), .07f, Yellow);
            var root3 = Place(k, "Scaffold", lotCenter);
            var jib = new Kit();
            jib.Beam(new Vector3(-.25f, 0, 0), new Vector3(size * .75f, 0, 0), .06f, Yellow);
            jib.Box(-.32f, 0, -.08f, .05f, .09f, .09f, Concrete);
            jib.Box(0, 0, -.03f, .1f, .07f, .07f, Dark);
            jib.Box(size * .5f, 0, -.42f, -.4f, .02f, .02f, Steel);
            jib.Box(size * .5f, 0, -.5f, -.42f, .07f, .07f, Yellow);
            jib.Box(-.28f, 0, .05f, .09f, .04f, .04f, Red, true);
            var arm = Place(jib, "Jib", lotCenter + new Vector3(cx, mast, cz), root3);
            Spin(arm, Vector3.up, .5f, 70, cx);
            return root3;
        }

        Transform Barricade(Vector3 at, float yaw)
        {
            var k = new Kit();
            for (int i = -1; i <= 1; i++)
            {
                k.Box(i * .3f, 0, 0, .18f, .13f, .07f, Concrete);
                k.Box(i * .3f, -.071f, .08f, .13f, .1f, .002f, Hazard);
            }
            k.Beam(new Vector3(-.45f, .25f, .05f), new Vector3(.45f, .25f, .05f), .03f, Dark);
            var t = Place(k, "Barricade", at);
            t.rotation = Quaternion.Euler(0, yaw, 0);
            return t;
        }

        Transform Pylon(Vector3 at)
        {
            var k = new Kit();
            float h = 1.15f;
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                k.Beam(new Vector3(x * .14f, 0, z * .14f), new Vector3(x * .03f, h, z * .03f), .025f, Dark);
            for (float y = .25f; y < h; y += .3f)
            {
                float r = Mathf.Lerp(.14f, .03f, y / h);
                k.Beam(new Vector3(-r, y, -r), new Vector3(r, y, r), .015f, Dark);
                k.Beam(new Vector3(r, y, -r), new Vector3(-r, y, r), .015f, Dark);
            }
            k.Cyl(0, 0, .05f, h, h + .06f, Red, 8, true);
            return Place(k, "Pylon", at, null, false);
        }

        /// <summary>The fortification: an armored turret on its pad, bigger with each level.</summary>
        Transform Turret(Vector3 lotCenter, float size, int level)
        {
            var k = new Kit();
            float s = size * (.36f + .04f * level);
            k.Box(0, 0, 0, .085f, size * .47f, size * .47f, Concrete);
            k.Cyl(0, 0, s, .085f, .085f + s * .55f, Dark, 8);
            k.Cone(0, 0, s, s * .82f, .085f + s * .55f, .085f + s * .7f, new Color32(84, 92, 98, 255), 8);
            k.Dome(0, 0, s * .6f, .085f + s * .7f, Steel, .7f);
            k.Beam(new Vector3(0, .085f + s * .95f, 0), new Vector3(s * 1.1f, .085f + s * 1.0f, -s * 1.1f), s * .14f, Dark);
            k.Cyl(s * .35f, -s * .35f, s * .08f, .085f + s * 1.1f, .085f + s * 1.18f, Red, 8, true);
            for (int i = 0; i < level; i++) k.Box(-s * .9f + i * s * .3f, s * .95f, .085f, .085f + s * .3f, s * .12f, s * .12f, Yellow);
            return Place(k, "Turret", lotCenter);
        }

        Transform Drone3D()
        {
            var k = new Kit();
            k.Box(0, 0, -.04f, .05f, .09f, .09f, White);
            k.Box(0, 0, -.06f, -.04f, .05f, .05f, Dark);
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                k.Beam(Vector3.zero, new Vector3(x * .16f, .02f, z * .16f), .02f, Dark);
                k.Cyl(x * .16f, z * .16f, .07f, .03f, .035f, Steel, 10);
            }
            k.Box(.05f, -.05f, 0, .03f, .02f, .02f, Cyan, true);
            k.Box(0, 0, -.13f, -.06f, .05f, .05f, Containers[3]);
            return Place(k, "Drone", Vector3.zero, null, false);
        }
    }
}
