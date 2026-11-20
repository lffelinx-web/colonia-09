using System;
using System.Collections.Generic;
using Colonia09.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Colonia09.Presentation
{
    /// <summary>
    /// Where things are on the 3D island and how the camera looks at it: a light perspective from the south-east,
    /// panned and zoomed by touch. Save files keep CampaignData's tile grid; each zone is placed on its district.
    /// </summary>
    public sealed partial class ColonyView
    {
        [Serializable] sealed class RoadCurve { public Vector2[] points; }
        [Serializable] sealed class IslandLayout
        {
            public float width, height, pixelsPerTile;
            public Vector2[] centers;
            public RoadCurve[] links, roads, viaducts;
        }

        // The district plan is drawn in top-left pixels of the 2D plate (Tools/Render/draw_alpine_districts.py).
        static IslandLayout layout;
        static IslandLayout Island => layout ?? (layout = JsonUtility.FromJson<IslandLayout>(
            Resources.Load<TextAsset>("Island/IslandLayout").text));

        static Vector3 LocalTile(CampaignSector sector, float dx, float dy)
        {
            var c = ZoneAb(sector);
            return World(c.x + dx, c.y + dy, PaveTop);
        }

        public static Vector3 TileWorld(float x, float y)
        {
            // Zones, and the landmark plazas just outside them, follow their district on the island.
            var sector = CampaignData.ZoneNear(x, y);
            if (sector == null) return World(x - 31, y - 31, 0);
            var (cx, cy) = CampaignData.ZoneCenter(sector);
            float dx = x - cx, dy = y - cy;
            var c = ZoneAb(sector);
            bool inZone = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) <= CampaignData.ZoneRadius + .5f;
            bool onPlaza = Mathf.Max(Mathf.Abs(dx + 6), Mathf.Abs(dy + 6)) <= 2.3f && sector.Id != "mines";
            float h = inZone || onPlaza ? PaveTop : Mathf.Max(TerrainHeight(c.x + dx, c.y + dy), sector.Id == "mines" ? -3 : 0);
            return World(c.x + dx, c.y + dy, h);
        }

        public bool ScreenToTile(Vector2 screen, out int x, out int y)
        {
            ApplyCamera();
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            float plane = IslandOrigin.y + LotTop;
            x = y = -1;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (plane - ray.origin.y) / ray.direction.y;
            if (t < 0) return false;
            var hit = ray.origin + ray.direction * t - IslandOrigin;
            float a = hit.x, b = -hit.z;
            foreach (var sector in CampaignData.Sectors)
            {
                var c = ZoneAb(sector);
                int dx = Mathf.RoundToInt(a - c.x), dy = Mathf.RoundToInt(b - c.y);
                if (Math.Abs(dx) > CampaignData.ZoneRadius || Math.Abs(dy) > CampaignData.ZoneRadius) continue;
                var (cx, cy) = CampaignData.ZoneCenter(sector);
                x = cx + dx;
                y = cy + dy;
                // A tap anywhere on a 2 x 2 lot means the lot, named by its anchor tile.
                if (CampaignData.LotAnchor(x, y) is (int ax, int ay)) (x, y) = (ax, ay);
                return true;
            }
            return false;
        }

        void DrawIsland() => BuildWorld();

        /// <summary>Sun, sky light and the sea's backdrop for the hour: warm day, blue moonlit night.</summary>
        void LightIsland()
        {
            if (sun != null)
            {
                sun.intensity = Mathf.Lerp(1.25f, .32f, Nightness);
                sun.color = Color.Lerp(new Color(1f, .95f, .86f), new Color(.55f, .66f, 1f), Nightness);
            }
            var ambient = Color.Lerp(new Color(.52f, .58f, .66f), new Color(.1f, .14f, .26f), Nightness);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(ambient);
            RenderSettings.ambientProbe = sh;
            if (waterMat != null) waterMat.SetColor("_Emission", Color.Lerp(new Color(.02f, .07f, .09f), new Color(.0f, .02f, .05f), Nightness));
            cam.backgroundColor = Color.Lerp((Color)new Color32(16, 74, 96, 255), new Color32(0, 18, 40, 255), Nightness);
        }

        // ---- Camera ----------------------------------------------------------------------

        /// <summary>A light perspective: tilted down this much, a narrow lens, looking from the south-east.</summary>
        const float Pitch = 42f, Fov = 30f, Yaw = -45f;
        const float CloseZoom = 2.6f;
        /// <summary>Zoom that fits a whole district across the screen.</summary>
        float FocusZoom => Mathf.Max(6.5f, 6.2f / Mathf.Max(.3f, cam != null ? cam.aspect : .5f));
        static readonly Quaternion CamRotation = Quaternion.Euler(Pitch, Yaw, 0);
        /// <summary>The part of the ground plan the camera may look at, in island tiles.</summary>
        const float IslandReach = 52f;

        public bool Distant => zoom > Mathf.Max(15f, FocusZoom * 1.5f);
        /// <summary>The whole island across the screen: it spans about 60 tiles from side to side.</summary>
        float OverviewZoom => Mathf.Max(31f / Mathf.Max(.3f, cam.aspect), 24f) * Screen.height / Mathf.Max(1, Screen.height - insetTop - insetBottom);

        /// <summary>World units per screen pixel at the point the camera looks at.</summary>
        float UnitsPerPixel => 2 * zoom / Mathf.Max(1, Screen.height);

        /// <summary>The ground (a, b) shift that moves the view by this many screen pixels.</summary>
        Vector2 ScreenToGround(Vector2 pixels)
        {
            var right = CamRotation * Vector3.right;
            var ahead = CamRotation * Vector3.forward;
            ahead.y = 0;
            ahead.Normalize();
            var move = (right * pixels.x + ahead * (pixels.y / Mathf.Sin(Pitch * Mathf.Deg2Rad))) * UnitsPerPixel;
            return new Vector2(move.x, -move.z);
        }

        void ApplyCamera()
        {
            cam.orthographic = false;
            cam.fieldOfView = Fov;
            cam.nearClipPlane = .5f;
            cam.farClipPlane = 2500;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.SetRenderer(1);
            float distance = zoom / Mathf.Tan(Fov * .5f * Mathf.Deg2Rad);
            // Shadows must reach past everything in view, or their edge shows as a dark wedge that follows the camera.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
                pipeline.shadowDistance = distance + zoom * 3f;
            cam.transform.SetPositionAndRotation(World(center.x, center.y, 0) - CamRotation * Vector3.forward * distance, CamRotation);
        }

        public void Focus(CampaignSector sector)
        {
            if (Distant || zoom < CloseZoom) zoom = FocusZoom;
            center = ZoneAb(sector) + ScreenToGround(new Vector2(0, (insetBottom - insetTop) / 2));
            Clamp();
        }

        /// <summary>For the automated screenshots: the middle of a connection's road, close up.</summary>
        public void LookAtLink(int link, float closeZoom)
        {
            var path = LinkPath(link);
            var middle = path.At(path.Length * .5f) - IslandOrigin;
            center = new Vector2(middle.x, -middle.z);
            zoom = closeZoom;
            Clamp();
        }

        public void Overview()
        {
            zoom = OverviewZoom;
            center = ScreenToGround(new Vector2(0, (insetBottom - insetTop) / 2));
            Clamp();
        }

        public void Pan(Vector2 screenDelta)
        {
            center -= ScreenToGround(screenDelta);
            Clamp();
        }

        public void Zoom(float factor)
        {
            zoom = Mathf.Clamp(zoom * factor, CloseZoom, OverviewZoom);
            Clamp();
        }

        void Clamp()
        {
            zoom = Mathf.Clamp(zoom, CloseZoom, OverviewZoom);
            center.x = Mathf.Clamp(center.x, -IslandReach, IslandReach);
            center.y = Mathf.Clamp(center.y, -IslandReach, IslandReach);
        }

        /// <summary>
        /// Sprites still standing on the island (landmarks, lamps, lights) face the camera; ground marks lie flat.
        /// A tile is a unit square here, and the sprites were drawn for tiles one unit wide on screen, so they grow by √2.
        /// </summary>
        void Place3D(SpriteRenderer r, bool flat)
        {
            var t = r.transform;
            const float Root2 = 1.41421356f;
            if (flat)
            {
                t.rotation = Quaternion.Euler(90, 45, 0);
                var s = t.localScale.x;
                t.localScale = new Vector3(s * Root2, s * 2 * Root2, 1);
                t.position += Vector3.up * .03f;
                return;
            }
            t.rotation = CamRotation;
            t.localScale *= 1.1f;
            // Pulled toward the camera so the lower half of the picture, drawn below its ground point, is not buried.
            t.position -= CamRotation * Vector3.forward * (1.2f * t.localScale.y);
        }
    }
}
