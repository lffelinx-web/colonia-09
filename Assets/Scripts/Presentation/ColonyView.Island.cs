using System;
using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    public sealed partial class ColonyView
    {
        [Serializable] sealed class RoadCurve { public Vector2[] points; }
        [Serializable] sealed class IslandLayout
        {
            public float width, height, pixelsPerTile;
            public Vector2[] centers;
            public RoadCurve[] links;
        }

        // The art uses top-left pixels. Save files always use CampaignData's original grid.
        static IslandLayout layout;
        static IslandLayout Island => layout ?? (layout = JsonUtility.FromJson<IslandLayout>(
            Resources.Load<TextAsset>("Island/IslandLayout").text));
        static Vector3 IslandPoint(Vector2 pixel) => Origin + new Vector3(
            (pixel.x - Island.width / 2) / Island.pixelsPerTile,
            (Island.height / 2 - pixel.y) / Island.pixelsPerTile, 0);

        static Vector3 LocalTile(CampaignSector sector, float dx, float dy) =>
            IslandPoint(Island.centers[sector.Number - 1]) + new Vector3((dx - dy) * .5f, -(dx + dy) * .25f, 0);

        public static Vector3 TileWorld(float x, float y)
        {
            // Zones, and the landmark plazas just outside them, follow their district on the island art.
            var sector = CampaignData.ZoneNear(x, y);
            if (sector == null) return Origin + new Vector3((x - y) * .5f, -(x + y) * .25f, 0);
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            return LocalTile(sector, x - cx, y - cy);
        }

        public bool ScreenToTile(Vector2 screen, out int x, out int y)
        {
            var point = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
            foreach (var sector in CampaignData.Sectors)
            {
                var d = point - LocalTile(sector, 0, 0);
                int dx = Mathf.RoundToInt(d.x - 2 * d.y), dy = Mathf.RoundToInt(-d.x - 2 * d.y);
                if (Math.Abs(dx) > CampaignData.ZoneRadius || Math.Abs(dy) > CampaignData.ZoneRadius) continue;
                var (cx, cy) = CampaignData.ZoneCenter(sector);
                x = cx + dx;
                y = cy + dy;
                // A tap anywhere on a 2 x 2 lot means the lot, named by its anchor tile.
                if (CampaignData.LotAnchor(x, y) is (int ax, int ay)) (x, y) = (ax, ay);
                return true;
            }
            x = y = -1;
            return false;
        }

        SpriteRenderer landscape, districts, spire;
        bool dormantSpire;

        SpriteRenderer MapLayer(string name, int order)
        {
            var sprite = Resources.Load<Sprite>("Island/" + name);
            float width = Island.width / Island.pixelsPerTile;
            return Put(sprite, Origin, order, Color.white, width / sprite.bounds.size.x);
        }

        void DrawIsland()
        {
            landscape = MapLayer("AlpineIsland", -5100);
            districts = MapLayer("DistrictWorks", -5000);
            spire = null;
        }

        void PlaceSpire(CampaignSector sector, bool active)
        {
            var sprite = Resources.Load<Sprite>("Island/CivicSpire");
            var (x, y) = CampaignData.LandmarkCenter(sector);
            // The imported pivot is at the center of the circular footing.
            spire = Put(sprite, TileWorld(x, y), Depth(x + 1, y + 1) + 1, Color.white, 5.8f / (sprite.rect.width / sprite.pixelsPerUnit));
            dormantSpire = !active;
        }

        void LightIsland()
        {
            landscape.color = Color.Lerp(Color.white, new Color(.29f, .38f, .53f), Nightness);
            districts.color = Color.Lerp(Color.white, new Color(.43f, .52f, .62f), Nightness);
            if (spire != null)
                spire.color = Color.Lerp(dormantSpire ? new Color(.48f, .51f, .55f) : Color.white,
                    dormantSpire ? new Color(.26f, .33f, .42f) : new Color(.60f, .79f, .91f), Nightness);
            cam.backgroundColor = Color.Lerp((Color)new Color32(0, 72, 94, 255), new Color32(0, 21, 46, 255), Nightness);
        }

        List<GameObject> LightRoad(CampaignSector to, out Path path)
        {
            path = new Path();
            foreach (var pixel in Island.links[to.Number - 2].points) path.Append(IslandPoint(pixel));
            var markers = new List<GameObject>();
            for (float d = .35f; d < path.Length; d += .75f)
            {
                var p = path.At(d);
                var direction = (path.At(d + .10f) - p).normalized;
                var curb = p + new Vector3(-direction.y, direction.x, 0) * .10f;
                var marker = Light(curb, new Color(.28f, .86f, .95f, .85f), .12f, -5830);
                markers.Add(marker.gameObject);
            }
            return markers;
        }

        public bool Distant => zoom > 14f;
        float OverviewZoom => Mathf.Max(Island.width / (2 * Island.pixelsPerTile * cam.aspect),
            Island.height / (2 * Island.pixelsPerTile) * Screen.height / Mathf.Max(1, Screen.height - insetTop - insetBottom)) * 1.035f;

        public void Overview()
        {
            zoom = OverviewZoom;
            center = new Vector2(0, (insetTop - insetBottom) * .5f * UnitsPerPixel);
        }
    }
}
