using Colonia09.Simulation;
using UnityEngine;

namespace Colonia09.Presentation
{
    /// <summary>
    /// The 3D landmark on each zone's plaza: what the place is for. Built from simple solids in the colony's colors,
    /// with a part that moves while the zone is linked (radar, excavator arm, cranes, the core's globe).
    /// The plaza is about 4.4 tiles across; the camera sees it from +x, -z, so the tall parts stand at the back (-x, +z).
    /// </summary>
    public sealed partial class ColonyView
    {
        Transform Landmark3D(CampaignSector sector, Vector3 at, bool running)
        {
            Transform t;
            switch (sector.Id)
            {
                case "landing": t = LandingPad(at, running); break;
                case "camp": t = Camp(at); break;
                case "well": t = WaterWorks(at); break;
                case "greenhouse": t = Greenhouses(at); break;
                case "mines": t = OpenPit(at, running); break;
                case "workshop": t = Workshop(at, running); break;
                case "port": t = Harbor(at, running); break;
                default: t = CommandCore(at, running); break;
            }
            if (!running) Dim(t);
            return t;
        }

        /// <summary>A zone not yet linked: its landmark stands grey and unlit.</summary>
        static void Dim(Transform t)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(block);
                block.SetColor("_Color", r.name == "Glow" ? new Color(.25f, .25f, .3f) : new Color(.62f, .63f, .68f));
                r.SetPropertyBlock(block);
            }
        }

        Transform LandingPad(Vector3 at, bool running)
        {
            var k = new Kit();
            k.Cyl(.3f, -.3f, 1.75f, 0, .08f, Dark, 32);
            k.Cone(.3f, -.3f, 1.5f, 1.5f, .08f, .1f, Yellow, 40, true);
            k.Box(.05f, -.3f, .08f, .1f, .07f, .45f, White);
            k.Box(.55f, -.3f, .08f, .1f, .07f, .45f, White);
            k.Box(.3f, -.3f, .08f, .1f, .25f, .06f, White);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6;
                k.Cyl(.3f + Mathf.Cos(a) * 1.65f, -.3f + Mathf.Sin(a) * 1.65f, .05f, .08f, .13f, Warm, 6, true);
            }
            // Control tower at the back corner.
            k.Cyl(-1.45f, 1.45f, .3f, 0, 2.4f, White, 16);
            k.Cyl(-1.45f, 1.45f, .5f, 2.4f, 2.8f, new Color32(111, 184, 208, 255), 16, true);
            k.Cyl(-1.45f, 1.45f, .56f, 2.8f, 2.9f, Dark, 16);
            k.Dome(1.45f, 1.45f, .5f, 0, White, .8f);
            // A shuttle resting on the pad.
            k.Box(.3f, -.3f, .1f, .42f, .22f, .55f, White);
            k.Cone(.3f, -.9f, .2f, .02f, .25f, .25f, White, 10);
            k.Box(.3f, -.1f, .12f, .2f, .55f, .2f, Steel);
            k.Box(.3f, .25f, .42f, .7f, .03f, .18f, Steel);
            k.Box(.3f, .27f, .15f, .3f, .14f, .03f, Cyan, true);
            var t = Place(k, "LandingPad", at);
            var dish = new Kit();
            dish.Cyl(0, 0, .05f, 0, .2f, Steel, 8);
            dish.Box(0, 0, .2f, .26f, .32f, .06f, White);
            dish.Box(.3f, 0, .26f, .32f, .03f, .03f, Red, true);
            var radar = Place(dish, "Radar", at + new Vector3(-1.45f, 2.9f, 1.45f), t);
            if (running) Spin(radar, Vector3.up, 1.6f);
            return t;
        }

        Transform Camp(Vector3 at)
        {
            var k = new Kit();
            foreach (var (x, z, along) in new[] { (-.9f, .9f, true), (.9f, .9f, false), (-.9f, -.6f, false) })
            {
                float hx = along ? .85f : .45f, hz = along ? .45f : .85f;
                k.Box(x, z, 0, .35f, hx, hz, White);
                k.Box(x, z, .35f, .5f, hx * .92f, hz * .92f, Steel);
                k.Box(x + hx + .005f, z, .12f, .26f, .005f, hz * .5f, Cyan, true);
            }
            // Antenna mast with a dish, and crates by the door.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                k.Beam(new Vector3(1.1f + Mathf.Cos(a) * .25f, 0, -1.0f + Mathf.Sin(a) * .25f), new Vector3(1.1f, 3, -1.0f), .04f, Steel);
            }
            k.Dome(1.1f, -1.0f, .3f, 2.3f, White, .35f);
            k.Cyl(1.1f, -1.0f, .06f, 3.0f, 3.1f, Red, 8, true);
            k.Box(.2f, -1.4f, 0, .32f, .16f, .16f, new Color32(196, 154, 90, 255));
            k.Box(-.2f, -1.5f, 0, .24f, .12f, .12f, new Color32(196, 154, 90, 255));
            return Place(k, "Camp", at);
        }

        Transform WaterWorks(Vector3 at)
        {
            var k = new Kit();
            foreach (var (x, z) in new[] { (-1.1f, 1.0f), (1.0f, 1.1f), (-1.1f, -1.0f) })
            {
                k.Cyl(x, z, .75f, 0, 2.0f, White, 20);
                k.Cone(x, z, .77f, .77f, .55f, .62f, Cyan, 20, true);
                k.Cone(x, z, .77f, .77f, 1.35f, 1.42f, Cyan, 20, true);
                k.Dome(x, z, .75f, 2.0f, Steel, .35f);
            }
            k.Cyl(1.0f, -1.0f, .85f, 0, .2f, White, 24);
            k.Cyl(1.0f, -1.0f, .72f, .2f, .22f, new Color32(47, 142, 163, 255), 24, true);
            k.Cyl(1.0f, -1.0f, .05f, .22f, .75f, Cyan, 8, true);
            k.Beam(new Vector3(-.35f, .45f, 1.05f), new Vector3(.25f, .45f, 1.05f), .14f, new Color32(44, 127, 192, 255));
            k.Beam(new Vector3(-1.1f, .45f, .25f), new Vector3(-1.1f, .45f, -.25f), .14f, new Color32(44, 127, 192, 255));
            return Place(k, "WaterWorks", at);
        }

        Transform Greenhouses(Vector3 at)
        {
            var k = new Kit();
            foreach (float x in new[] { -1.4f, 0f, 1.4f })
            {
                k.Box(x, 0, 0, .08f, .6f, 2.0f, Concrete);
                k.Box(x, 0, .08f, .22f, .45f, 1.9f, GreenLight, true);
                for (float z = -1.8f; z <= 1.81f; z += .6f)
                    for (int s = 0; s < 6; s++)
                    {
                        float a0 = Mathf.PI * s / 6, a1 = Mathf.PI * (s + 1) / 6;
                        k.Beam(new Vector3(x + Mathf.Cos(a0) * .55f, .08f + Mathf.Sin(a0) * .55f, z), new Vector3(x + Mathf.Cos(a1) * .55f, .08f + Mathf.Sin(a1) * .55f, z), .03f, Steel);
                    }
                k.Beam(new Vector3(x, .63f, -1.85f), new Vector3(x, .63f, 1.85f), .04f, Steel);
            }
            return Place(k, "Greenhouses", at);
        }

        Transform OpenPit(Vector3 at, bool running)
        {
            var k = new Kit();
            var rubble = new Color32(140, 112, 86, 255);
            for (int i = 0; i < 6; i++) k.Dome(-.6f + i * .25f, .6f - (i % 2) * .3f, .16f, 0, i % 2 == 0 ? rubble : Rust, .7f);
            // A dump truck and the excavator's body; a conveyor climbs out of the pit to the rim.
            k.Box(.9f, -.7f, .08f, .3f, .35f, .2f, Yellow);
            k.Box(1.15f, -.7f, .3f, .45f, .1f, .18f, Dark);
            foreach (var (x, z) in new[] { (.65f, -.88f), (1.15f, -.88f), (.65f, -.52f), (1.15f, -.52f) }) k.Cyl(x, z, .08f, 0, .1f, Dark, 10);
            k.Box(-.1f, .1f, 0, .12f, .3f, .38f, Dark);
            k.Box(-.1f, .1f, .12f, .45f, .32f, .32f, Yellow);
            k.Box(.02f, -.02f, .45f, .7f, .14f, .14f, Yellow);
            k.Box(.17f, -.02f, .5f, .64f, .005f, .1f, Cyan, true);
            k.Beam(new Vector3(-.8f, .3f, .8f), new Vector3(-2.4f, 1.6f, 2.4f), .22f, Dark);
            k.Beam(new Vector3(-.8f, .42f, .8f), new Vector3(-2.4f, 1.72f, 2.4f), .12f, Yellow);
            var t = Place(k, "OpenPit", at);
            var boom = new Kit();
            boom.Beam(Vector3.zero, new Vector3(.6f, .55f, -.5f), .09f, Yellow);
            boom.Beam(new Vector3(.6f, .55f, -.5f), new Vector3(.85f, -.1f, -.7f), .07f, Yellow);
            boom.Box(.85f, -.7f, -.25f, -.05f, .14f, .14f, Dark);
            var arm = Place(boom, "Boom", at + new Vector3(.1f, .45f, -.1f), t);
            if (running) Spin(arm, new Vector3(1, 0, 1).normalized, 1.1f, 14);
            return t;
        }

        Transform Workshop(Vector3 at, bool running)
        {
            var k = new Kit();
            foreach (var (x, z, w, d) in new[] { (-.9f, .8f, .9f, 1.2f), (1.0f, .95f, .75f, 1.0f) })
            {
                k.Box(x, z, 0, 1.1f, w, d, Concrete);
                for (int s = 0; s < 5; s++)
                    k.Box(x, z, 1.1f + s * .08f, 1.18f + s * .08f, w * (1 - s * .18f), d, Steel);
                k.Box(x + w + .005f, z, .1f, .85f, .005f, d * .55f, Dark);
                for (int s = 0; s < 6; s++) k.Box(x + w + .01f, z - d * .5f + s * d * .2f, .88f, .96f, .005f, d * .07f, Yellow);
                k.Box(x, z - d - .005f, .55f, .7f, w * .7f, .005f, Cyan, true);
            }
            k.Cyl(-1.6f, 1.6f, .14f, 0, 2.2f, Steel, 10);
            k.Cyl(-1.6f, 1.6f, .16f, 2.0f, 2.1f, Red, 10, true);
            // Container yard in the front corner, under a gantry crane.
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 2; j++)
                    for (int lvl = 0; lvl < 1 + (i + j) % 2; lvl++)
                        k.Box(.3f + i * .6f, -.9f - j * .55f, lvl * .32f, lvl * .32f + .3f, .27f, .22f, Containers[(i * 2 + j + lvl) % Containers.Length]);
            foreach (float z in new[] { -.55f, -1.75f })
            {
                k.Beam(new Vector3(-.15f, 0, z), new Vector3(-.15f, 1.6f, z), .07f, Yellow);
                k.Beam(new Vector3(1.95f, 0, z), new Vector3(1.95f, 1.6f, z), .07f, Yellow);
            }
            var t = Place(k, "Workshop", at);
            var gantry = new Kit();
            gantry.Beam(new Vector3(-1.05f, 0, .6f), new Vector3(1.05f, 0, .6f), .09f, Yellow);
            gantry.Beam(new Vector3(-1.05f, 0, -.6f), new Vector3(1.05f, 0, -.6f), .09f, Yellow);
            gantry.Box(0, 0, -.05f, .15f, .2f, .7f, Yellow);
            gantry.Box(0, 0, -.6f, -.05f, .015f, .015f, Steel);
            gantry.Box(0, 0, -.7f, -.6f, .2f, .15f, Dark);
            var crane = Place(gantry, "Gantry", at + new Vector3(.9f, 1.6f, -1.15f), t);
            if (running) Spin(crane, Vector3.up, .6f, 8);
            return t;
        }

        Transform Harbor(Vector3 at, bool running)
        {
            var k = new Kit();
            k.Box(0, 0, 0, .1f, 2.1f, 2.1f, Concrete);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if ((i + j) % 3 != 0)
                        for (int lvl = 0; lvl < 1 + (i * j) % 2; lvl++)
                            k.Box(-1.2f + i * .7f, .2f + j * .55f, .1f + lvl * .32f, .4f + lvl * .32f, .3f, .22f, Containers[(i * 3 + j + lvl) % Containers.Length]);
            // A small lighthouse at the front corner.
            k.Cyl(1.4f, -1.4f, .22f, .1f, 1.8f, White, 12);
            k.Cone(1.4f, -1.4f, .23f, .23f, .7f, .9f, Hazard, 12);
            k.Cyl(1.4f, -1.4f, .2f, 1.8f, 2.05f, Warm, 12, true);
            k.Cone(1.4f, -1.4f, .26f, .02f, 2.05f, 2.3f, Dark, 12);
            foreach (float z in new[] { -1.1f, .1f })
            {
                k.Beam(new Vector3(-1.6f, .1f, z), new Vector3(-1.6f, 2.0f, z), .07f, Yellow);
                k.Beam(new Vector3(.4f, .1f, z), new Vector3(.4f, 2.0f, z), .07f, Yellow);
            }
            var t = Place(k, "Harbor", at);
            var boom = new Kit();
            boom.Beam(new Vector3(-1.1f, 0, 0), new Vector3(1.8f, 0, 0), .12f, Yellow);
            boom.Box(-.9f, 0, .05f, .4f, .3f, .4f, White);
            boom.Box(.9f, 0, -.65f, -.05f, .015f, .015f, Steel);
            boom.Box(.9f, 0, -.95f, -.65f, .28f, .2f, Containers[0]);
            var crane = Place(boom, "HarborCrane", at + new Vector3(-.6f, 2.05f, -.5f), t);
            if (running) Spin(crane, Vector3.up, .45f, 35);
            Ship(t);
            return t;
        }

        /// <summary>A freighter moored at the end of the port's pier, out at sea.</summary>
        void Ship(Transform parent)
        {
            var pier = Island.roads[Island.roads.Length - 1].points;
            var end = PlanAb(pier[pier.Length - 1]);
            var before = PlanAb(pier[pier.Length - 4]);
            var along = World(end.x, end.y, 0) - World(before.x, before.y, 0);
            along.y = 0;
            along.Normalize();
            var k = new Kit();
            k.Box(0, 0, -.3f, .35f, 2.0f, .55f, new Color32(36, 56, 74, 255));
            k.Box(0, 0, .3f, .36f, 2.02f, .56f, Hazard);
            k.Cone(2.0f, 0, .55f, .05f, -.3f, .35f, new Color32(36, 56, 74, 255), 4);
            k.Box(-1.3f, 0, .35f, .95f, .45f, .45f, White);
            k.Box(-1.3f, 0, .95f, 1.2f, .32f, .32f, White);
            k.Box(-.84f, 0, .7f, .82f, .005f, .3f, Cyan, true);
            for (int i = 0; i < 3; i++) k.Box(-.3f + i * .65f, 0, .35f, .75f, .28f, .4f, Containers[(i + 1) % Containers.Length]);
            var side = new Vector3(-along.z, 0, along.x);
            var ship = Place(k, "Ship", World(end.x, end.y, Features.sea) + side * 1.2f - along * 1.5f, parent);
            ship.rotation = Quaternion.LookRotation(side, Vector3.up) * Quaternion.Euler(0, -90, 0);
        }

        Transform CommandCore(Vector3 at, bool running)
        {
            var k = new Kit();
            k.Cyl(0, 0, 2.1f, 0, .16f, Concrete, 40);
            k.Cyl(0, 0, 1.85f, .16f, .26f, White, 40);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                float x = Mathf.Cos(a) * 1.4f, z = Mathf.Sin(a) * 1.4f;
                k.Cyl(x, z, .36f, .26f, .34f, White, 16);
                k.Cyl(x, z, .3f, .34f, .36f, new Color32(47, 142, 163, 255), 16, true);
                k.Cyl(x, z, .04f, .36f, .62f, Cyan, 6, true);
            }
            k.Cyl(0, 0, .62f, .26f, 4.9f, White, 24);
            for (float y = 1f; y < 4.6f; y += 1f) k.Cone(0, 0, .64f, .64f, y, y + .08f, Cyan, 24, true);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                k.Box(Mathf.Cos(a) * .63f, Mathf.Sin(a) * .63f, .6f, 4.6f, .06f, .06f, Warm, true);
            }
            k.Cyl(0, 0, .9f, 4.9f, 5.08f, Steel, 24);
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2 / 3 + .3f;
                k.Beam(new Vector3(Mathf.Cos(a) * 1.75f, .26f, Mathf.Sin(a) * 1.75f), new Vector3(Mathf.Cos(a) * 1.25f, 3.4f, Mathf.Sin(a) * 1.25f), .1f, Steel);
            }
            k.Cone(0, 0, 1.32f, 1.32f, 3.36f, 3.46f, Cyan, 48, true);
            k.Cone(0, 0, 1.15f, 1.15f, 3.5f, 3.56f, Steel, 48);
            var t = Place(k, "CommandCore", at);
            // The holographic globe over the tower: rings of light that turn.
            var globe = new Kit();
            for (int m = 0; m < 4; m++)
            {
                var turn = Quaternion.Euler(0, m * 45, 0);
                for (int s = 0; s < 16; s++)
                {
                    float a0 = s * Mathf.PI / 8, a1 = (s + 1) * Mathf.PI / 8;
                    globe.Beam(turn * new Vector3(Mathf.Cos(a0) * .85f, Mathf.Sin(a0) * .85f, 0), turn * new Vector3(Mathf.Cos(a1) * .85f, Mathf.Sin(a1) * .85f, 0), .035f, Cyan, true);
                }
            }
            foreach (float y in new[] { -.45f, 0f, .45f })
            {
                float r = Mathf.Sqrt(.85f * .85f - y * y);
                for (int s = 0; s < 16; s++)
                {
                    float a0 = s * Mathf.PI / 8, a1 = (s + 1) * Mathf.PI / 8;
                    globe.Beam(new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r), new Vector3(Mathf.Cos(a1) * r, y, Mathf.Sin(a1) * r), .03f, Cyan, true);
                }
            }
            var holo = Place(globe, "Globe", at + Vector3.up * 6.0f, t, false);
            if (running) Spin(holo, Vector3.up, .5f);
            return t;
        }
    }
}
