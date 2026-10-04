// Virtual OR - editor helpers: create the scene and configure builds.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VirtualOR.EditorTools
{
    public static class VirtualORMenu
    {
        const string ScenePath = "Assets/VirtualOR/VirtualOR.unity";

        [MenuItem("Virtual OR/Create and open the Virtual OR scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool present = false; foreach (var s in list) if (s.path == ScenePath) present = true;
            if (!present) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[VirtualOR] Scene created at " + ScenePath + ". Press Play: the operating room is built from code.");
        }

        [MenuItem("Virtual OR/Configure WebGL build (for a demo link)")]
        public static void ConfigureWebGL()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;   // works on GitHub Pages / any static host
            PlayerSettings.productName = "ViviOR";
            PlayerSettings.companyName = "HackYeah 2026";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.stripEngineCode = false;   // the scene is built from code at runtime; stripping removed CapsuleCollider etc.
            Debug.Log("[VirtualOR] WebGL settings applied. File > Build Settings > WebGL > Switch Platform > Build.");
        }

        // One click: WebGL build with the full-window ViviOR template straight into website/sim/, which the website opens
        // (simUrl 'sim/' in website/vor-website.html). Host the whole website/ folder (GitHub Pages, Netlify...).
        // Same build with function names kept in browser stack traces (for diagnosing WebGL-only crashes).
        public static void BuildIntoWebsiteDebug() { debugSymbols = true; try { BuildIntoWebsite(); } finally { debugSymbols = false; } }
        static bool debugSymbols;

        [MenuItem("Virtual OR/Build WebGL into the website (website/sim)")]
        public static void BuildIntoWebsite()
        {
            if (!System.IO.File.Exists(ScenePath)) CreateScene();
            ConfigureWebGL();
            PlayerSettings.WebGL.template = "PROJECT:ViviOR";
            PlayerSettings.WebGL.debugSymbolMode = debugSymbols ? WebGLDebugSymbolMode.Embedded : WebGLDebugSymbolMode.Off;
            string outDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../website/sim"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = outDir, target = BuildTarget.WebGL, options = BuildOptions.None });
            Debug.Log("[VirtualOR] WebGL build " + report.summary.result + " -> " + outDir + " (" + report.summary.totalSize / (1024 * 1024) + " MB, " + report.summary.totalTime + ")");
        }
    }
}
