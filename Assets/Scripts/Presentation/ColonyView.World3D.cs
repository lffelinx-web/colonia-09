using System;
using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Colonia09.Presentation
{
    /// <summary>
    /// The colony island as real 3D geometry, so nothing turns blocky up close: terrain from the baked height grid
    /// (Tools/Render/bake_island3d.py), sea, river and waterfall, roads and viaducts from the district plan, paved
    /// districts with their 2 x 2 lots, trees and rocks, and the outside building models.
    /// Island ground (a, b) in tiles sits at world (a, height, -b) around <see cref="IslandOrigin"/>.
    /// </summary>
    public sealed partial class ColonyView
    {
        /// <summary>Far below the battle board, which the same camera draws at the origin.</summary>
        static readonly Vector3 IslandOrigin = new Vector3(0, -2000, 0);
        const float GroundLevel = 0f, PaveTop = .06f, LotTop = .085f;

        [Serializable] sealed class GridInfo { public int n; public float a0, step, hmin, hmax; }
        [Serializable] sealed class Fall { public float a, b, dirA, dirB, top, bottom; }
        [Serializable] sealed class IslandFeatures
        {
            public GridInfo grid;
            public float sea, riverWidth;
            public float[] river, trees, rocks, peak;
            public Fall[] falls;
        }

        static IslandFeatures features;
        static IslandFeatures Features => features ?? (features = JsonUtility.FromJson<IslandFeatures>(Resources.Load<TextAsset>("Island3D/Features").text));
        static float[] heights;

        static Vector3 World(float a, float b, float h) => IslandOrigin + new Vector3(a, h, -b);

        /// <summary>Island ground coordinates of a pixel of the 2D district plan.</summary>
        static Vector2 PlanAb(Vector2 pixel)
        {
            float sx = (pixel.x - Island.width / 2) / Island.pixelsPerTile, sy = (Island.height / 2 - pixel.y) / Island.pixelsPerTile;
            return new Vector2(sx - 2 * sy, -sx - 2 * sy);
        }

        static Vector2 ZoneAb(CampaignSector sector) => PlanAb(Island.centers[sector.Number - 1]);

        /// <summary>Height of the bare land, from the baked grid.</summary>
        static float TerrainHeight(float a, float b)
        {
            var g = Features.grid;
            if (heights == null)
            {
                var bytes = Resources.Load<TextAsset>("Island3D/Height").bytes;
                heights = new float[g.n * g.n];
                for (int k = 0; k < heights.Length; k++)
                    heights[k] = g.hmin + (bytes[2 * k] | bytes[2 * k + 1] << 8) / 65535f * (g.hmax - g.hmin);
            }
            float i = Mathf.Clamp((a - g.a0) / g.step, 0, g.n - 1.001f), j = Mathf.Clamp((b - g.a0) / g.step, 0, g.n - 1.001f);
            int i0 = (int)i, j0 = (int)j;
            float fi = i - i0, fj = j - j0;
            float h00 = heights[j0 * g.n + i0], h10 = heights[j0 * g.n + i0 + 1], h01 = heights[(j0 + 1) * g.n + i0], h11 = heights[(j0 + 1) * g.n + i0 + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, fi), Mathf.Lerp(h01, h11, fi), fj);
        }

        // ---- Mesh building ---------------------------------------------------------------

        /// <summary>Collects vertices with colors and normals; split into meshes of up to 60 000 vertices.</summary>
        sealed class MeshBuilder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int> T = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                int k = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                for (int i = 0; i < 4; i++) { N.Add(n); C.Add(color); }
                T.Add(k); T.Add(k + 1); T.Add(k + 3);
                T.Add(k + 1); T.Add(k + 2); T.Add(k + 3);
            }

            /// <summary>One triangle facing <paramref name="normal"/> (clockwise when seen from that side).</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Color32 color)
            {
                int k = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                for (int i = 0; i < 3; i++) { N.Add(normal); C.Add(color); }
                T.Add(k); T.Add(k + 1); T.Add(k + 2);
            }

            /// <summary>An axis-aligned box on the ground plan (center, half sizes), from y0 to y1, without its bottom.</summary>
            public void Box(Vector3 center, float hx, float hz, float y0, float y1, Color32 top, Color32 side)
            {
                float x0 = center.x - hx, x1 = center.x + hx, z0 = center.z - hz, z1 = center.z + hz;
                float b = center.y + y0, t = center.y + y1;
                Quad(new Vector3(x0, t, z0), new Vector3(x0, t, z1), new Vector3(x1, t, z1), new Vector3(x1, t, z0), top);
                Quad(new Vector3(x0, b, z0), new Vector3(x0, t, z0), new Vector3(x1, t, z0), new Vector3(x1, b, z0), side);
                Quad(new Vector3(x1, b, z1), new Vector3(x1, t, z1), new Vector3(x0, t, z1), new Vector3(x0, b, z1), side);
                Quad(new Vector3(x0, b, z1), new Vector3(x0, t, z1), new Vector3(x0, t, z0), new Vector3(x0, b, z0), side);
                Quad(new Vector3(x1, b, z0), new Vector3(x1, t, z0), new Vector3(x1, t, z1), new Vector3(x1, b, z1), side);
            }

            /// <summary>A flat strip along a path of world points, its width across the path.</summary>
            public void Ribbon(IList<Vector3> path, float width, float lift, Color32 color)
            {
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    Vector3 Side(int k)
                    {
                        var d = path[Mathf.Min(k + 1, path.Count - 1)] - path[Mathf.Max(k - 1, 0)];
                        d.y = 0;
                        return new Vector3(-d.z, 0, d.x).normalized * (width / 2);
                    }
                    Vector3 p = path[i] + Vector3.up * lift, q = path[i + 1] + Vector3.up * lift;
                    Vector3 sp = Side(i), sq = Side(i + 1);
                    Quad(p - sp, p + sp, q + sq, q - sq, color);
                }
            }

            /// <summary>A mesh made somewhere else (a tree, a rock), moved, turned and scaled, and tinted.</summary>
            public void Add(Mesh mesh, Matrix4x4 m, Color32[] colors)
            {
                int k = V.Count;
                var verts = mesh.vertices;
                var norms = mesh.normals;
                for (int i = 0; i < verts.Length; i++)
                {
                    V.Add(m.MultiplyPoint3x4(verts[i]));
                    N.Add(m.MultiplyVector(norms[i]).normalized);
                    C.Add(colors[i]);
                }
                foreach (int t in mesh.triangles) T.Add(k + t);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(V);
                mesh.SetNormals(N);
                mesh.SetColors(C);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        Transform world;
        Material groundMat, waterMat, foamMat;
        Light sun;

        GameObject MeshObject(string name, Mesh mesh, Material mat, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(world, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        static Texture2D DetailTexture()
        {
            // Tileable value noise in a few octaves: grain that keeps the ground crisp however close the camera gets.
            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.R8, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new byte[S * S];
            var rng = new System.Random(7);
            float[] Lattice(int n) { var l = new float[n * n]; for (int i = 0; i < l.Length; i++) l[i] = (float)rng.NextDouble(); return l; }
            var octaves = new[] { (8, Lattice(8), .45f), (16, Lattice(16), .3f), (32, Lattice(32), .17f), (64, Lattice(64), .08f) };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float v = 0;
                    foreach (var (n, l, w) in octaves)
                    {
                        float fx = x * n / (float)S, fy = y * n / (float)S;
                        int x0 = (int)fx, y0 = (int)fy;
                        float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        float a = l[(y0 % n) * n + x0 % n], b = l[(y0 % n) * n + (x0 + 1) % n];
                        float c = l[((y0 + 1) % n) * n + x0 % n], d = l[((y0 + 1) % n) * n + (x0 + 1) % n];
                        v += w * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                    }
                    px[y * S + x] = (byte)Mathf.Clamp(v * 255, 0, 255);
                }
            tex.SetPixelData(px, 0);
            tex.Apply(true, true);
            return tex;
        }

        Material SurfaceMaterial(Texture detail, float scale, float strength, float gloss = 0)
        {
            var mat = new Material(Shader.Find("Colonia/VertexLit")) { enableInstancing = true };
            mat.SetTexture("_DetailTex", detail);
            mat.SetFloat("_DetailScale", scale);
            mat.SetFloat("_DetailStrength", strength);
            mat.SetFloat("_Gloss", gloss);
            return mat;
        }

        /// <summary>Builds the island once; it stays while the map is open and hides with it.</summary>
        void BuildWorld()
        {
            if (world != null) return;
            world = new GameObject("Island3D").transform;
            world.SetParent(transform, false);
            world.position = IslandOrigin;
            var detail = DetailTexture();
            groundMat = SurfaceMaterial(detail, .23f, .55f);
            var paveMat = SurfaceMaterial(detail, .9f, .25f);
            waterMat = SurfaceMaterial(detail, .08f, .3f, .55f);
            foamMat = SurfaceMaterial(detail, .5f, .3f);
            foamMat.SetColor("_Emission", new Color(.18f, .22f, .24f));
            var plantMat = SurfaceMaterial(detail, 2.2f, .35f);

            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(world, false);
            sunGo.transform.rotation = Quaternion.Euler(48, -150, 0);
            sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .75f;

            BuildTerrain();
            BuildWater();
            BuildRoads(paveMat);
            BuildDistricts(paveMat);
            BuildPlants(plantMat);
            world.gameObject.SetActive(Active);
        }

        static Color32 Rgb(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        void BuildTerrain()
        {
            var g = Features.grid;
            // A coarser mesh than the bake keeps the vertex count phone-friendly; the detail texture adds the grain.
            int n = (g.n + 1) / 2;
            float step = g.step * 2;
            var verts = new Vector3[n * n];
            var colors = new Color32[n * n];
            var grassA = (Color)Rgb("#4f7d34"); var grassB = (Color)Rgb("#6f9441"); var rock = (Color)Rgb("#77706a");
            var snow = (Color)Rgb("#f1f4f6"); var sand = (Color)Rgb("#a89a78"); var mud = (Color)Rgb("#5b5040");
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float a = g.a0 + i * step, b = g.a0 + j * step;
                    verts[j * n + i] = World(a, b, TerrainHeight(a, b)) - IslandOrigin;
                }
            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j + 1 < n; j++)
                for (int i = 0; i + 1 < n; i++)
                {
                    int k = j * n + i;
                    tris[t++] = k; tris[t++] = k + 1; tris[t++] = k + n;
                    tris[t++] = k + 1; tris[t++] = k + n + 1; tris[t++] = k + n;
                }
            var mesh = new Mesh { name = "Terrain", indexFormat = IndexFormat.UInt32, vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            for (int k = 0; k < verts.Length; k++)
            {
                var p = verts[k];
                float a = p.x, b = -p.z, h = p.y;
                float patch = Mathf.PerlinNoise(a * .09f + 31, b * .09f + 17);
                var c = Color.Lerp(grassA, grassB, patch);
                float steep = Mathf.InverseLerp(.86f, .62f, normals[k].y);
                c = Color.Lerp(c, rock, steep);
                if (h < -.25f) c = Color.Lerp(c, h < -1.2f ? rock : mud, Mathf.InverseLerp(-.25f, -.6f, h));
                if (h < -1.5f) c = Color.Lerp(c, sand, Mathf.InverseLerp(-1.5f, -2.2f, h) * .6f);
                float snowLine = 6.6f + Mathf.PerlinNoise(a * .3f, b * .3f) * 1.4f;
                c = Color.Lerp(c, snow, Mathf.InverseLerp(snowLine, snowLine + .8f, h) * Mathf.Lerp(1, .55f, steep));
                colors[k] = c;
            }
            mesh.colors32 = colors;
            mesh.RecalculateBounds();
            // The ground only receives shadows: casting them onto itself darkened whole slopes in bands.
            MeshObject("Terrain", mesh, groundMat, false).transform.localPosition = Vector3.zero;
        }

        void BuildWater()
        {
            var f = Features;
            var sea = new MeshBuilder();
            float r = 400;
            var deep = Rgb("#1b5a73");
            sea.Quad(new Vector3(-r, f.sea, -r), new Vector3(-r, f.sea, r), new Vector3(r, f.sea, r), new Vector3(r, f.sea, -r), deep);
            MeshObject("Sea", sea.Build("Sea"), waterMat, false);

            var river = new MeshBuilder();
            var path = new List<Vector3>();
            for (int k = 0; k + 2 < f.river.Length; k += 3) path.Add(World(f.river[k], f.river[k + 1], f.river[k + 2]) - IslandOrigin);
            river.Ribbon(path, f.riverWidth, 0, Rgb("#2f8ea3"));
            MeshObject("River", river.Build("River"), waterMat, false);

            var foam = new MeshBuilder();
            var white = Rgb("#e8f4f6");
            foreach (var fall in f.falls)
            {
                // The sheet of water leaves the river's lip and falls to the sea, a little forward as it drops.
                var dir = new Vector3(fall.dirA, 0, -fall.dirB).normalized;
                var side = new Vector3(-dir.z, 0, dir.x) * (f.riverWidth * .55f);
                var top = World(fall.a, fall.b, fall.top) - IslandOrigin + dir * .2f;
                var bottom = World(fall.a, fall.b, fall.bottom) - IslandOrigin + dir * 1.6f;
                foam.Quad(top - side, top + side, bottom + side * 1.2f, bottom - side * 1.2f, white);
                foam.Quad(top + side, top - side, bottom - side * 1.2f, bottom + side * 1.2f, white);
                foam.Box(bottom + dir * .3f, .9f, .9f, -.05f, .08f, white, white);
            }
            MeshObject("Falls", foam.Build("Falls"), foamMat, false);
        }

        void BuildRoads(Material paveMat)
        {
            var roads = new MeshBuilder();
            var asphalt = Rgb("#3a4146"); var curb = Rgb("#a3aba7"); var mark = Rgb("#d9cc94");
            foreach (var road in Island.roads)
            {
                var path = new List<Vector3>();
                foreach (var p in road.points) { var ab = PlanAb(p); path.Add(World(ab.x, ab.y, GroundLevel) - IslandOrigin); }
                roads.Ribbon(path, 1.15f, .015f, curb);
                roads.Ribbon(path, .9f, .03f, asphalt);
                for (int i = 0; i + 2 < path.Count; i += 4) roads.Ribbon(new[] { path[i], path[i + 1] }, .06f, .035f, mark);
            }
            // Viaducts: rails along both sides and piers down to the river bed.
            var rail = Rgb("#c3ced3"); var pier = Rgb("#5d666c");
            foreach (var viaduct in Island.viaducts)
            {
                for (int i = 0; i < viaduct.points.Length; i += 3)
                {
                    var ab = PlanAb(viaduct.points[i]);
                    float ground = TerrainHeight(ab.x, ab.y);
                    if (ground > -.2f) continue;
                    roads.Box(World(ab.x, ab.y, 0) - IslandOrigin, .12f, .12f, ground - .1f, 0, pier, pier);
                }
                var path = new List<Vector3>();
                foreach (var p in viaduct.points) { var ab = PlanAb(p); path.Add(World(ab.x, ab.y, GroundLevel) - IslandOrigin); }
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    var d = path[i + 1] - path[i];
                    var s = new Vector3(-d.z, 0, d.x).normalized * .55f;
                    foreach (var o in new[] { s, -s })
                        roads.Quad(path[i] + o + Vector3.up * .04f, path[i] + o + Vector3.up * .2f, path[i + 1] + o + Vector3.up * .2f, path[i + 1] + o + Vector3.up * .04f, rail);
                }
            }
            MeshObject("Roads", roads.Build("Roads"), paveMat, false);
        }

        static readonly string[] DistrictPaving = { "#7d8a70", "#8a9670", "#7a8c86", "#83924f", "#968f74", "#868f89", "#8a9592", "#8ea06a" };

        void BuildDistricts(Material paveMat)
        {
            var mb = new MeshBuilder();
            var street = Rgb("#3a4146"); var walk = Rgb("#9aa3a6"); var wall = Rgb("#59636a"); var mark = Rgb("#d9cc94");
            var garden = Rgb("#5d8a42"); var plaza = Rgb("#aab3b5");
            foreach (var sector in CampaignData.Sectors)
            {
                var c = ZoneAb(sector);
                var center = World(c.x, c.y, GroundLevel) - IslandOrigin;
                var pad = (Color)Rgb(DistrictPaving[sector.Number - 1]);
                // The district deck, a little raised, with its curb.
                mb.Box(center, 4.75f, 4.75f, -.35f, PaveTop - .01f, walk, wall);
                mb.Box(center, 4.5f, .45f, 0, PaveTop + .005f, street, street);
                mb.Box(center, .45f, 4.5f, 0, PaveTop + .006f, street, street);
                for (int k = -4; k <= 4; k++)
                {
                    if (k == 0) continue;
                    mb.Box(center + new Vector3(k, 0, 0), .16f, .025f, 0, PaveTop + .012f, mark, mark);
                    mb.Box(center + new Vector3(0, 0, -k), .025f, .16f, 0, PaveTop + .012f, mark, mark);
                }
                var (cx, cy) = CampaignData.ZoneCenter(sector);
                var lots = new HashSet<(int, int)>(CampaignData.Lots(sector));
                foreach (int v in new[] { -4, -2, 1, 3 })
                    foreach (int u in new[] { -4, -2, 1, 3 })
                    {
                        var lot = center + new Vector3(u + .5f, 0, -(v + .5f));
                        bool building = lots.Contains((cx + u, cy + v));
                        var tone = building ? (Color32)Color.Lerp(pad, Color.white, ((u + v) & 1) * .08f) : garden;
                        mb.Box(lot, .93f, .93f, 0, LotTop, tone, wall);
                    }
                // The landmark's own plaza behind the far corner, joined by a short walk (the mines' is the open pit).
                var plazaCenter = World(c.x - 6, c.y - 6, GroundLevel) - IslandOrigin;
                if (sector.Id != "mines") mb.Box(plazaCenter, 2.2f, 2.2f, -.3f, PaveTop, plaza, wall);
                mb.Box(World(c.x - 4.9f, c.y - 4.9f, GroundLevel) - IslandOrigin, .55f, .55f, -.3f, PaveTop - .005f, walk, wall);
            }
            MeshObject("Districts", mb.Build("Districts"), paveMat);
        }

        static Mesh pineMesh, roundMesh, rockMesh;

        static Mesh Cone(int sides, float r0, float r1, float y0, float y1, Color32 color, MeshBuilder mb)
        {
            for (int s = 0; s < sides; s++)
            {
                float t0 = s * Mathf.PI * 2 / sides, t1 = (s + 1) * Mathf.PI * 2 / sides;
                var a0 = new Vector3(Mathf.Cos(t0) * r0, y0, Mathf.Sin(t0) * r0);
                var a1 = new Vector3(Mathf.Cos(t1) * r0, y0, Mathf.Sin(t1) * r0);
                var b0 = new Vector3(Mathf.Cos(t0) * r1, y1, Mathf.Sin(t0) * r1);
                var b1 = new Vector3(Mathf.Cos(t1) * r1, y1, Mathf.Sin(t1) * r1);
                mb.Quad(a0, b0, b1, a1, color);
            }
            return null;
        }

        static Mesh PineMesh()
        {
            if (pineMesh != null) return pineMesh;
            var mb = new MeshBuilder();
            Cone(6, .06f, .05f, 0, .28f, Rgb("#4a3626"), mb);
            Cone(8, .36f, .02f, .2f, .62f, Rgb("#ffffff"), mb);
            Cone(8, .29f, .02f, .45f, .86f, Rgb("#ffffff"), mb);
            Cone(8, .2f, .01f, .7f, 1.08f, Rgb("#ffffff"), mb);
            pineMesh = mb.Build("Pine");
            return pineMesh;
        }

        static Mesh RoundTreeMesh()
        {
            if (roundMesh != null) return roundMesh;
            var mb = new MeshBuilder();
            Cone(6, .05f, .04f, 0, .3f, Rgb("#4a3626"), mb);
            const int rings = 4, sides = 8;
            for (int r = 0; r < rings; r++)
            {
                float p0 = Mathf.PI * r / rings, p1 = Mathf.PI * (r + 1) / rings;
                Cone(sides, Mathf.Sin(p0) * .3f, Mathf.Sin(p1) * .3f, .52f - Mathf.Cos(p0) * .26f, .52f - Mathf.Cos(p1) * .26f, Rgb("#ffffff"), mb);
            }
            roundMesh = mb.Build("RoundTree");
            return roundMesh;
        }

        static Mesh RockMesh()
        {
            if (rockMesh != null) return rockMesh;
            var mb = new MeshBuilder();
            const int rings = 3, sides = 6;
            for (int r = 0; r < rings; r++)
            {
                float p0 = Mathf.PI * .5f * r / rings, p1 = Mathf.PI * .5f * (r + 1) / rings;
                Cone(sides, Mathf.Cos(p0), Mathf.Cos(p1), Mathf.Sin(p0) * .7f, Mathf.Sin(p1) * .7f, Rgb("#ffffff"), mb);
            }
            rockMesh = mb.Build("Rock");
            return rockMesh;
        }

        /// <summary>Trees and rocks, merged into meshes per area of the island so each draws in one call.</summary>
        void BuildPlants(Material mat)
        {
            var f = Features;
            const int Areas = 6;
            var builders = new MeshBuilder[Areas * Areas];
            MeshBuilder Area(float a, float b)
            {
                int i = Mathf.Clamp((int)((a + 72) / 144 * Areas), 0, Areas - 1), j = Mathf.Clamp((int)((b + 72) / 144 * Areas), 0, Areas - 1);
                return builders[j * Areas + i] ?? (builders[j * Areas + i] = new MeshBuilder());
            }
            var rng = new System.Random(5);
            Color32[] Tint(Mesh mesh, Color leaf, Color trunk)
            {
                var src = mesh.colors32;
                var outColors = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++) outColors[i] = src[i].r > 200 ? leaf : (Color32)trunk;
                return outColors;
            }
            var pine = PineMesh(); var round = RoundTreeMesh(); var rock = RockMesh();
            var trunk = (Color)Rgb("#4a3626");
            var leaves = new[] { (Color)Rgb("#2c4f2e"), (Color)Rgb("#3f6a36"), (Color)Rgb("#5a8c3e") };
            for (int k = 0; k + 4 < f.trees.Length; k += 5)
            {
                float a = f.trees[k], b = f.trees[k + 1], y = f.trees[k + 2], s = f.trees[k + 3];
                int kind = (int)f.trees[k + 4];
                var leaf = Color.Lerp(leaves[kind], Color.white, (float)rng.NextDouble() * .08f) * (.9f + (float)rng.NextDouble() * .15f);
                var m = Matrix4x4.TRS(World(a, b, y - .03f) - IslandOrigin, Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0), Vector3.one * s);
                var mesh = kind == 2 ? round : pine;
                Area(a, b).Add(mesh, m, Tint(mesh, leaf, trunk));
            }
            var stone = new[] { (Color)Rgb("#6f6862"), (Color)Rgb("#827a72"), (Color)Rgb("#5d5853") };
            for (int k = 0; k + 4 < f.rocks.Length; k += 5)
            {
                float a = f.rocks[k], b = f.rocks[k + 1], y = f.rocks[k + 2], s = f.rocks[k + 3];
                var m = Matrix4x4.TRS(World(a, b, y - s * .2f) - IslandOrigin, Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0),
                    new Vector3(s * (.8f + (float)rng.NextDouble() * .5f), s * (.5f + (float)rng.NextDouble() * .6f), s * (.8f + (float)rng.NextDouble() * .5f)));
                Area(a, b).Add(rock, m, Tint(rock, stone[(int)f.rocks[k + 4] % 3], stone[0]));
            }
            for (int i = 0; i < builders.Length; i++)
                if (builders[i] != null) MeshObject("Plants" + i, builders[i].Build("Plants" + i), mat);
        }

        // ---- Building models -------------------------------------------------------------

        static readonly Dictionary<string, string> ModelFolders = new Dictionary<string, string>
        {
            { "EX", "Extractores/" }, { "LB", "Laboratorios/" }, { "PS", "Solares/" }, { "GH", "Granjas/" },
            { "TA", "TorresDeAgua/" }, { "RF", "Refinerias/" }, { "TO", "Talleres/" }, { "N", "" }, { "AS", "" },
        };

        static readonly Dictionary<string, (GameObject prefab, float scale, Vector3 offset)> models = new Dictionary<string, (GameObject, float, Vector3)>();

        /// <summary>The outside model for a building's look, measured once so it fills a 2 x 2 lot.</summary>
        static (GameObject prefab, float scale, Vector3 offset) Model(string name)
        {
            if (models.TryGetValue(name, out var found)) return found;
            string key = name.StartsWith("AS") ? "AS" : name.StartsWith("N") ? "N" : name.Substring(0, 2);
            var prefab = ModelFolders.TryGetValue(key, out var folder) ? Resources.Load<GameObject>("Colonia09/" + folder + "Prefabs/" + name) : null;
            float scale = 1;
            var offset = Vector3.zero;
            if (prefab != null)
            {
                var probe = Instantiate(prefab);
                var bounds = new Bounds();
                bool first = true;
                foreach (var r in probe.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                }
                DestroyImmediate(probe);
                scale = 1.85f / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z));
                offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            }
            models[name] = (prefab, scale, offset);
            return models[name];
        }

        /// <summary>The model's yaw on the map: its front faces the camera like the rendered pictures did (checked with ModelPreview).</summary>
        const float ModelYaw = 180f;

        static readonly HashSet<Material> glowing = new HashSet<Material>();

        /// <summary>The emission maps (lit windows, cyan strips) only show with the keyword on, which the conversion lost.</summary>
        static void LightUp(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && glowing.Add(m) && m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null)
                {
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
        }

        GameObject PutModel(string name, Vector3 lotCenter, bool flip)
        {
            var (prefab, scale, offset) = Model(name);
            if (prefab == null) return null;
            var go = Instantiate(prefab, root);
            // The second look of a kind is the same model mirrored, as the pictures were.
            var turn = Quaternion.Euler(0, ModelYaw, 0);
            var mirror = flip ? new Vector3(-1, 1, 1) : Vector3.one;
            go.transform.SetPositionAndRotation(lotCenter + Vector3.up * LotTop + turn * Vector3.Scale(mirror, offset), turn);
            go.transform.localScale = mirror * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                LightUp(r);
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            return go;
        }

        static string ModelName(string art) => art.StartsWith("Planos/") ? art.Substring(7) : null;
    }
}
