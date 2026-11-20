using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>
    /// The colony's mag-lev roads. Every route of the district plan is cleared ground (a dusty regolith track) from
    /// the start; a route gets its raised deck, with cyan guide lines, studs and light posts, only when a connection
    /// runs along it. A connection being laid shows its deck growing section by section behind a construction drone,
    /// and finished ones carry hover pods back and forth.
    /// </summary>
    public sealed partial class ColonyView
    {
        const float DeckTop = .1f, DeckHalf = .5f, DeckStep = .4f;
        static readonly Color32 DeckColor = new Color32(43, 49, 55, 255), DeckSide = new Color32(70, 79, 86, 255),
            DeckCurb = new Color32(150, 164, 171, 255), DeckSeam = new Color32(88, 99, 106, 255), Regolith = new Color32(139, 127, 106, 255),
            GuideLine = new Color32(28, 150, 186, 255);  // Deeper than the lamps' cyan, so the lines stay blue in daylight.

        // Island plan coordinates (local to IslandOrigin, at ground level) of every road and connection, measured once.
        static List<Vector3>[] roadLines, linkLines;
        static HashSet<int>[] linkRoads;
        static HashSet<(int, int)>[] roadKeys;
        static float[] roadDistance;

        static (int, int) Key(Vector2 pixel) => (Mathf.RoundToInt(pixel.x * 10), Mathf.RoundToInt(pixel.y * 10));

        static Vector3 Local(Vector2 pixel)
        {
            var ab = PlanAb(pixel);
            return new Vector3(ab.x, GroundLevel, -ab.y);
        }

        static void MeasureRoads()
        {
            if (roadLines != null) return;
            var roads = Island.roads;
            roadLines = new List<Vector3>[roads.Length];
            roadKeys = new HashSet<(int, int)>[roads.Length];
            for (int r = 0; r < roads.Length; r++)
            {
                roadLines[r] = new List<Vector3>();
                roadKeys[r] = new HashSet<(int, int)>();
                foreach (var p in roads[r].points) { roadLines[r].Add(Local(p)); roadKeys[r].Add(Key(p)); }
            }
            var links = Island.links;
            linkLines = new List<Vector3>[links.Length];
            linkRoads = new HashSet<int>[links.Length];
            for (int n = 0; n < links.Length; n++)
            {
                linkLines[n] = new List<Vector3>();
                var keys = new HashSet<(int, int)>();
                foreach (var p in links[n].points) { linkLines[n].Add(Local(p)); keys.Add(Key(p)); }
                // A connection runs along a road when it passes through the road's middle.
                linkRoads[n] = new HashSet<int>();
                for (int r = 0; r < roads.Length; r++)
                    if (keys.Contains(Key(roads[r].points[roads[r].points.Length / 2]))) linkRoads[n].Add(r);
            }
            // Distance from every route, on the baked height grid: the ground is cleared and lowered along them.
            var g = Features.grid;
            roadDistance = new float[g.n * g.n];
            for (int k = 0; k < roadDistance.Length; k++) roadDistance[k] = 99;
            foreach (var line in roadLines)
                foreach (var p in line)
                {
                    float a = p.x, b = -p.z;
                    int ci = Mathf.RoundToInt((a - g.a0) / g.step), cj = Mathf.RoundToInt((b - g.a0) / g.step);
                    for (int j = cj - 5; j <= cj + 5; j++)
                        for (int i = ci - 5; i <= ci + 5; i++)
                        {
                            if (i < 0 || j < 0 || i >= g.n || j >= g.n) continue;
                            float d = Mathf.Sqrt(Sq(g.a0 + i * g.step - a) + Sq(g.a0 + j * g.step - b));
                            if (d < roadDistance[j * g.n + i]) roadDistance[j * g.n + i] = d;
                        }
                }
        }

        static float Sq(float x) => x * x;

        /// <summary>How far the ground point is from the nearest route (planned or built), in tiles.</summary>
        static float RoadDistance(float a, float b)
        {
            MeasureRoads();
            var g = Features.grid;
            int i = Mathf.Clamp(Mathf.RoundToInt((a - g.a0) / g.step), 0, g.n - 1), j = Mathf.Clamp(Mathf.RoundToInt((b - g.a0) / g.step), 0, g.n - 1);
            return roadDistance[j * g.n + i];
        }

        /// <summary>The road deck stops at the district decks and landmark plazas, which have their own streets.</summary>
        static bool UnderDistrict(Vector3 local)
        {
            float a = local.x, b = -local.z;
            foreach (var sector in CampaignData.Sectors)
            {
                var c = ZoneAb(sector);
                if (Mathf.Abs(a - c.x) < 4.7f && Mathf.Abs(b - c.y) < 4.7f) return true;
                if (sector.Id != "mines" && Mathf.Abs(a - c.x + 6) < 2.15f && Mathf.Abs(b - c.y + 6) < 2.15f) return true;
            }
            return false;
        }

        /// <summary>The same line with points about <see cref="DeckStep"/> apart, and how far along it each one is.</summary>
        static List<Vector3> Resample(IList<Vector3> line, out List<float> along)
        {
            var points = new List<Vector3> { line[0] };
            along = new List<float> { 0 };
            float total = 0, carried = 0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float length = Vector3.Distance(line[i], line[i + 1]);
                total += length;
                carried += length;
                if (carried >= DeckStep || i + 2 == line.Count)
                {
                    points.Add(line[i + 1]);
                    along.Add(total);
                    carried = 0;
                }
            }
            return points;
        }

        /// <summary>
        /// The deck along points [i0, i1] of a line: a raised slab with sides down into the ground, curbs, a center
        /// seam, two cyan guide lines, studs on the curbs and light posts on alternate sides.
        /// </summary>
        static void Deck(MeshBuilder lit, MeshBuilder glow, List<Vector3> pts, List<float> along, int i0, int i1)
        {
            Vector3 Side(int k)
            {
                var d = pts[Mathf.Min(k + 1, pts.Count - 1)] - pts[Mathf.Max(k - 1, 0)];
                d.y = 0;
                return new Vector3(-d.z, 0, d.x).normalized;
            }
            void Strip(MeshBuilder mb, Vector3 p, Vector3 q, Vector3 sp, Vector3 sq, float from, float to, float y, Color32 color) =>
                mb.Quad(p + sp * from + Vector3.up * y, p + sp * to + Vector3.up * y, q + sq * to + Vector3.up * y, q + sq * from + Vector3.up * y, color);
            void Wall(MeshBuilder mb, Vector3 a, Vector3 b, float y0, float y1, Color32 color)
            {
                // Both windings: walls are seen from either side as the camera pans.
                mb.Quad(a + Vector3.up * y0, a + Vector3.up * y1, b + Vector3.up * y1, b + Vector3.up * y0, color);
                mb.Quad(b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, a + Vector3.up * y0, color);
            }
            for (int i = i0; i < i1; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1];
                bool pIn = UnderDistrict(p), qIn = UnderDistrict(q);
                if (pIn && qIn) continue;
                if (pIn || qIn)
                {
                    // Cut the section where it meets the district's edge.
                    Vector3 inside = pIn ? p : q, outside = pIn ? q : p;
                    for (int s = 0; s < 6; s++)
                    {
                        var mid = (inside + outside) / 2;
                        if (UnderDistrict(mid)) inside = mid; else outside = mid;
                    }
                    if (pIn) p = outside; else q = outside;
                }
                Vector3 sp = Side(i), sq = Side(i + 1);
                // Slab top, sides into the ground, curbs with their inner face, seam and guide lines.
                Strip(lit, p, q, sp, sq, -DeckHalf, DeckHalf, DeckTop, DeckColor);
                foreach (float s in new[] { -1f, 1f })
                {
                    Wall(lit, p + sp * (DeckHalf * s), q + sq * (DeckHalf * s), -.35f, DeckTop + .04f, DeckSide);
                    float inner = (DeckHalf - .08f) * s, outer = DeckHalf * s;
                    Strip(lit, p, q, sp, sq, Mathf.Min(inner, outer), Mathf.Max(inner, outer), DeckTop + .04f, DeckCurb);
                    Wall(lit, p + sp * inner, q + sq * inner, DeckTop, DeckTop + .04f, DeckSide);
                    Strip(glow, p, q, sp, sq, .19f * s - .025f, .19f * s + .025f, DeckTop + .004f, GuideLine);
                }
                Strip(lit, p, q, sp, sq, -.015f, .015f, DeckTop + .003f, DeckSeam);
                // Studs every 1.2 tiles; a light post every 4.8, on alternate sides.
                float d0 = along[i], d1 = along[i + 1];
                for (float m = Mathf.Ceil(d0 / 1.2f) * 1.2f; m < d1; m += 1.2f)
                {
                    var at = Vector3.Lerp(p, q, (m - d0) / Mathf.Max(.001f, d1 - d0));
                    var side = Vector3.Lerp(sp, sq, .5f);
                    int n = Mathf.RoundToInt(m / 1.2f);
                    foreach (float s in new[] { -1f, 1f })
                        glow.Box(at + side * ((DeckHalf - .04f) * s), .025f, .025f, DeckTop + .04f, DeckTop + .07f, GuideLine, GuideLine);
                    if (n % 4 == 0)
                    {
                        float s = n % 8 == 0 ? 1 : -1;
                        var post = at + side * ((DeckHalf - .04f) * s);
                        lit.Box(post, .022f, .022f, DeckTop + .04f, DeckTop + .52f, DeckCurb, DeckSide);
                        // A dark arm over the deck with a lit lens on top, seen from the camera above.
                        lit.Box(post - side * (.06f * s), .075f, .075f, DeckTop + .5f, DeckTop + .54f, DeckSide, DeckSide);
                        glow.Box(post - side * (.06f * s), .05f, .05f, DeckTop + .54f, DeckTop + .555f, Cyan, Cyan);
                    }
                }
                // Over water and the river valley, piers down to the ground.
                if (i % 3 == 0)
                {
                    float ground = TerrainHeight(p.x, -p.z);
                    if (ground < -.2f) lit.Box(p, .12f, .12f, ground - .1f - p.y, -.3f, DeckSide, DeckSide);
                }
            }
        }

        /// <summary>The decks of every road some finished connection runs along (and the harbor's pier with the port).</summary>
        void LayRoads(HashSet<int> built)
        {
            MeasureRoads();
            if (built.Count == 0) return;
            var kit = new Kit();
            foreach (int r in built)
            {
                var pts = Resample(roadLines[r], out var along);
                Deck(kit.Lit, kit.Glow, pts, along, 0, pts.Count - 1);
            }
            Place(kit, "Roads", IslandOrigin, null, false);
        }

        /// <summary>
        /// The sections a connection still has to lay, where it does not already run along a built road, each its own
        /// object to switch on as the work advances; <paramref name="ends"/> is how far along the connection each one ends.
        /// </summary>
        List<GameObject> RoadSections(int link, HashSet<int> built, List<float> ends)
        {
            MeasureRoads();
            var done = new HashSet<(int, int)>();
            foreach (int r in built) done.UnionWith(roadKeys[r]);
            var pixels = Island.links[link].points;
            var line = linkLines[link];
            var sections = new List<GameObject>();
            float total = 0;
            int start = -1;
            float startAt = 0;
            void Close(int end)
            {
                if (start < 0 || end - start < 1) { start = -1; return; }
                var run = line.GetRange(start, end - start + 1);
                var pts = Resample(run, out var along);
                for (int i = 0; i < pts.Count - 1;)
                {
                    int j = i;
                    while (j < pts.Count - 1 && along[j + 1] - along[i] < 1.6f) j++;
                    if (j == i) j = i + 1;
                    var kit = new Kit();
                    Deck(kit.Lit, kit.Glow, pts, along, i, j);
                    var go = Place(kit, "RoadSection", IslandOrigin, null, false).gameObject;
                    go.SetActive(false);
                    sections.Add(go);
                    ends.Add(startAt + along[j]);
                    i = j;
                }
                start = -1;
            }
            for (int k = 0; k + 1 < line.Count; k++)
            {
                bool laid = done.Contains(Key(pixels[k])) && done.Contains(Key(pixels[k + 1]));
                if (!laid && start < 0) { start = k; startAt = total; }
                if (laid) Close(k);
                total += Vector3.Distance(line[k], line[k + 1]);
            }
            Close(line.Count - 1);
            return sections;
        }

        /// <summary>A connection's route in the world, along its deck's top.</summary>
        static Path LinkPath(int link)
        {
            MeasureRoads();
            var path = new Path();
            foreach (var p in linkLines[link]) path.Append(IslandOrigin + p + Vector3.up * (DeckTop - GroundLevel));
            return path;
        }

        /// <summary>A hover pod: a white capsule with a cyan band and lights, its long side along +z.</summary>
        Transform HoverPod(int tint)
        {
            var k = new Kit();
            var body = tint % 2 == 0 ? White : Steel;
            k.Box(0, 0, .03f, .09f, .055f, .13f, body);
            k.Box(0, .02f, .09f, .12f, .04f, .08f, Dark);
            k.Box(0, 0, .055f, .07f, .058f, .133f, Cyan, true);
            k.Box(0, .135f, .05f, .075f, .03f, .004f, White, true);
            k.Box(0, -.135f, .05f, .075f, .035f, .004f, new Color32(255, 90, 70, 255), true);
            k.Box(0, 0, .0f, .03f, .035f, .09f, Cyan, true);
            return Place(k, "Pod", Vector3.zero, null, false);
        }
    }
}
