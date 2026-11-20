using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Colonia09.EditorTools
{
    /// <summary>
    /// Renders building models as the colony camera sees them, from the four sides, to check which side faces the
    /// player and how the materials read. Batch: -executeMethod Colonia09.EditorTools.ModelPreview.Run
    /// Output: Builds/ModelPreview/&lt;name&gt;_&lt;yaw&gt;.png
    /// </summary>
    public static class ModelPreview
    {
        static readonly string[] Names =
        {
            "Colonia09/Prefabs/N1_RefugioBasico", "Colonia09/Prefabs/N3_ViviendaFamiliar",
            "Colonia09/Extractores/Prefabs/EX01_ExtractorBasico", "Colonia09/Laboratorios/Prefabs/LB02_LaboratorioDeAnalisis",
        };

        [MenuItem("Colonia 09/Vista previa de modelos")]
        public static void Run()
        {
            var output = Path.GetFullPath("Builds/ModelPreview");
            Directory.CreateDirectory(output);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sunGo.transform.rotation = Quaternion.Euler(48, -150, 0);
            RenderSettings.ambientLight = new Color(.52f, .58f, .66f);
            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.35f, .45f, .5f);
            cam.fieldOfView = 30;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(1);
            var rt = new RenderTexture(512, 512, 24);
            cam.targetTexture = rt;
            foreach (var name in Names)
            {
                var prefab = Resources.Load<GameObject>(name);
                if (prefab == null) { Debug.LogError("MODEL_PREVIEW missing " + name); continue; }
                var go = Object.Instantiate(prefab);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float size = bounds.size.magnitude;
                foreach (int yaw in new[] { 0, 90, 180, 270 })
                {
                    go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                    var rotation = Quaternion.Euler(42, -45, 0);
                    cam.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * size * 1.9f, rotation);
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(output, Path.GetFileName(name) + "_" + yaw + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                }
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials)
                        if (m != null)
                            Debug.Log($"MODEL_PREVIEW {name} {r.name} {m.name} shader={m.shader.name} base={(m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null)} emission={(m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null)} emColor={(m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.clear)} kw={string.Join(",", m.shaderKeywords)}");
                Object.DestroyImmediate(go);
            }
            RenderTexture.active = null;
            Debug.Log("MODEL_PREVIEW_DONE " + output);
        }
    }
}
