// Tek tıkla Quest 3S proje kurulumu: URP, OpenXR (Meta Quest), Android oyuncu ayarları, sahne, katman, shader referansları.
// Proje ilk açıldığında otomatik çalışır; sonradan "LPN-VR ▸ 1. Projeyi Kur (Quest 3S)" menüsüyle yeniden çalıştırılabilir.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;

namespace LPNVR.EditorTools
{
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        const string Root = "Assets/LPNVR";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const string ShaderRefsPath = Root + "/Resources/LPNShaders.asset";
        const string DoneFlag = "ProjectSettings/LPNVR.setup";

        static ProjectSetup()
        {
            if (Application.isBatchMode || File.Exists(DoneFlag)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += () => Run(true); return; }
                Run(true);
            };
        }

        [MenuItem("LPN-VR/1. Projeyi Kur (Quest 3S)", priority = 1)]
        public static void RunMenu() => Run(false);

        public static void Run(bool auto)
        {
            try
            {
                Configure();
                bool needRestart = InputHandlerChanged;
                string msg = "Kurulum tamamlandı.\n\n• Render: URP (MSAA 4x)\n• XR: OpenXR + Meta Quest desteği, Touch / Touch Plus kontrolcüler\n• Android: IL2CPP, ARM64, Vulkan, min API 32\n• Sahne: " + ScenePath +
                    "\n\nEditor'da başlık olmadan Play → masaüstü (fare/klavye) modu.\nQuest 3S: USB ile bağlayıp 'LPN-VR ▸ 2. Quest'e Derle ve Çalıştır'." +
                    (needRestart ? "\n\nGiriş sistemi ayarı değişti: Unity'yi yeniden başlatın." : "");
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    if (EditorUtility.DisplayDialog("LPN-VR", msg + "\n\nAktif platform Android'e geçirilsin mi? (Quest için gerekli, birkaç dakika sürebilir)", "Android'e geç", "Sonra"))
                        EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildTargetGroup.Android, BuildTarget.Android);
                }
                else EditorUtility.DisplayDialog("LPN-VR", msg, "Tamam");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("LPN-VR kurulum hatası", e.Message + "\n\nAyrıntı Console'da. Paketlerin içe aktarımı bitince 'LPN-VR ▸ 1. Projeyi Kur' menüsünü tekrar çalıştırın.", "Tamam");
            }
        }

        static void Configure()
        {
            EnsureFolders();
            CreateShaderRefs();
            ConfigureURP();
            ConfigureLayer();
            ConfigurePlayer();
            ConfigureXR(BuildTargetGroup.Android, new[] { "MetaQuestFeature", "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile", "MetaQuestTouchProControllerProfile" });
            ConfigureXR(BuildTargetGroup.Standalone, new[] { "OculusTouchControllerProfile", "MetaQuestTouchPlusControllerProfile" });
            CreateScene();
            AssetDatabase.SaveAssets();
            File.WriteAllText(DoneFlag, System.DateTime.Now.ToString("o"));
            Debug.Log("LPN-VR: proje kurulumu tamamlandı (URP + OpenXR Meta Quest + Android).");
        }

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Resources", "Scenes", "Settings" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{f}")) AssetDatabase.CreateFolder(Root, f);
        }

        static void CreateShaderRefs()
        {
            var refs = AssetDatabase.LoadAssetAtPath<ShaderRefs>(ShaderRefsPath);
            if (!refs) { refs = ScriptableObject.CreateInstance<ShaderRefs>(); AssetDatabase.CreateAsset(refs, ShaderRefsPath); }
            refs.shaders = new[] { "LPN/Tissue", "LPN/Unlit", "LPN/Points", "LPN/Monitor" }.Select(Shader.Find).ToArray();
            if (refs.shaders.Any(s => s == null)) throw new System.Exception("LPN shader'ları derlenemedi (URP paketi içe aktarıldı mı?)");
            EditorUtility.SetDirty(refs);
        }

        static void ConfigureURP()
        {
            var cur = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            UniversalRenderPipelineAsset urp = cur;
            if (!urp)
            {
                const string dataPath = Root + "/Settings/LPN_Renderer.asset", urpPath = Root + "/Settings/LPN_URP.asset";
                urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
                if (!urp)
                {
                    var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(data, dataPath);
                    urp = UniversalRenderPipelineAsset.Create(data);
                    AssetDatabase.CreateAsset(urp, urpPath);
                }
                GraphicsSettings.defaultRenderPipeline = urp;
                int cnt = QualitySettings.names.Length, curQ = QualitySettings.GetQualityLevel();
                for (int i = 0; i < cnt; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = urp; }
                QualitySettings.SetQualityLevel(curQ, false);
            }
            // Quest için hafif ayarlar
            urp.msaaSampleCount = 4;
            urp.supportsHDR = false;
            urp.renderScale = 1;
            urp.supportsCameraDepthTexture = false;
            urp.supportsCameraOpaqueTexture = false;
            urp.shadowDistance = 0;
            EditorUtility.SetDirty(urp);
        }

        static void ConfigureLayer()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var l = layers.GetArrayElementAtIndex(LPNApp.LAYER_LAP);
            if (string.IsNullOrEmpty(l.stringValue)) { l.stringValue = "Lap"; tm.ApplyModifiedProperties(); }
        }

        static bool InputHandlerChanged;
        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "SBU";
            PlayerSettings.productName = "LPN-VR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "tr.edu.sbu.lpnvr");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            // Giriş: hem Input System (XR kontrolcüleri) hem eski Input Manager (masaüstü yedek)
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var ih = ps.FindProperty("activeInputHandler");
            if (ih != null && ih.intValue != 2) { ih.intValue = 2; ps.ApplyModifiedProperties(); InputHandlerChanged = true; }
        }

        static void ConfigureXR(BuildTargetGroup g, string[] features)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (!perTarget)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            if (!perTarget.HasSettingsForBuildTarget(g)) perTarget.CreateDefaultSettingsForBuildTarget(g);
            if (!perTarget.HasManagerSettingsForBuildTarget(g)) perTarget.CreateDefaultManagerSettingsForBuildTarget(g);
            var s = perTarget.SettingsForBuildTarget(g);
            s.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(s.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", g);
            EditorUtility.SetDirty(perTarget);

            FeatureHelpers.RefreshFeatures(g);
            var ox = OpenXRSettings.GetSettingsForBuildTargetGroup(g);
            if (!ox) { Debug.LogWarning("LPN-VR: OpenXR ayarları bulunamadı: " + g); return; }
            foreach (var f in ox.GetFeatures())
                if (features.Contains(f.GetType().Name)) f.enabled = true;
            if (g == BuildTargetGroup.Android) ox.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(ox);
        }

        static void CreateScene()
        {
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("LPN-VR");
                go.AddComponent<AudioSource>();
                go.AddComponent<Sfx>();
                go.AddComponent<LPNApp>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        }

        // ---------------------------------------------------------------- derleme
        [MenuItem("LPN-VR/2. Quest'e Derle ve Çalıştır", priority = 2)]
        public static void BuildAndRun() => Build(true);

        [MenuItem("LPN-VR/3. Yalnız APK Derle", priority = 3)]
        public static void BuildApk() => Build(false);

        static void Build(bool run)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("LPN-VR", "Önce aktif platformu Android yapın (File ▸ Build Profiles ▸ Android ▸ Switch Platform).", "Tamam");
                return;
            }
            Directory.CreateDirectory("Builds");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/LPN-VR.apk",
                target = BuildTarget.Android,
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"LPN-VR derleme: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.outputPath}");
        }

        // Komut satırı / CI, iki ayrı Unity çalıştırması gerekir: giriş sistemi ayarı (activeInputHandler) ancak Editor yeniden
        // açılınca derleme tanımlarına yansır; aynı oturumda derlenirse Editor ile oyuncu sınıf düzeni uyuşmaz.
        //   1) Unity -batchmode -quit -buildTarget Android -executeMethod LPNVR.EditorTools.ProjectSetup.SetupCI
        //   2) Unity -batchmode -quit -buildTarget Android -executeMethod LPNVR.EditorTools.ProjectSetup.BuildCI [-customBuildPath yol.apk]
        // CI: kurulum + ekran görüntüleri (Shots/*.png)
        public static void ShotsCI() { Configure(); Shots.Run(); }

        public static void SetupCI()
        {
            Configure();
            SelfTest.Run();
            Debug.Log("LPN-VR: CI kurulumu tamam" + (InputHandlerChanged ? " (giriş sistemi ayarı değişti; derleme ayrı çalıştırmada yapılmalı)" : ""));
        }

        public static void BuildCI()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-customBuildPath");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/LPN-VR.apk";
            if (!path.EndsWith(".apk")) path = Path.Combine(path, "LPN-VR.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            Configure();
            if (InputHandlerChanged) { Debug.LogError("LPN-VR: giriş sistemi ayarı bu oturumda değişti; önce SetupCI ile ayrı bir çalıştırma yapın."); EditorApplication.Exit(1); return; }
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });
            Debug.Log($"LPN-VR derleme: {report.summary.result}, {report.summary.totalErrors} hata, {report.summary.outputPath}");
            EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
