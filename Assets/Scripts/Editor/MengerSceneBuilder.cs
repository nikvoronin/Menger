using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Menger.EditorTools
{
    /// <summary>Creates the settings asset, materials and the ready-to-run demo scene (Menu: Menger > Build Demo Scene).</summary>
    public static class MengerSceneBuilder
    {
        const string Dir = "Assets/Menger";
        const string ScenePath = "Assets/Scenes/MengerPrototype.unity";

        [MenuItem("Menger/Build Demo Scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            var settings = LoadOrCreate<MengerSettings>(Dir + "/MengerSettings.asset", () => ScriptableObject.CreateInstance<MengerSettings>());
            var surface = Material("SurfaceMaterial", "Menger/Surface");
            var glow = Material("GlowMaterial", "Menger/Glow");
            var sky = Material("SkyMaterial", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", MengerSkyGenerator.Generate(false));
            EditorUtility.SetDirty(sky);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0, 0, -100);

            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            lightGo.transform.rotation = Quaternion.LookRotation(-new Vector3(0.35f, 0.8f, 0.5f)); // the sun: shines down from above, slightly from the side

            var gameGo = new GameObject("MengerGame");
            var game = gameGo.AddComponent<MengerGame>();
            game.settings = settings;
            game.surfaceMaterial = surface;
            game.glowMaterial = glow;
            game.skyMaterial = sky;
            RenderSettings.skybox = sky;

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Menger: demo scene saved to " + ScenePath);
        }

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var made = create();
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static Material Material(string name, string shaderName)
        {
            string path = $"{Dir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException("Shader not found: " + shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            return m;
        }
    }
}
