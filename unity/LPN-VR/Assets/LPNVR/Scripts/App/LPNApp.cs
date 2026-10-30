// LPN-VR — Laparoskopik Parsiyel Nefrektomi simülatörü (Meta Quest 3S, OpenXR) — main.js'in Unity karşılığı.
// Tüm sahne çalışma zamanında prosedürel olarak kurulur; sahnede yalnız bu bileşen bulunur
// (yoksa RuntimeInitializeOnLoadMethod ile kendini oluşturur).
// Başlık takılı değilse (Editor'da Play) masaüstü fare/klavye moduyla çalışır.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static LPNVR.U;

namespace LPNVR
{
    [RequireComponent(typeof(AudioSource))]
    public class LPNApp : MonoBehaviour, IProcHost
    {
        public static float TestDt; // yalnız başlıksız test
        public const int LAYER_LAP = 6; // karın içi (yalnız laparoskop kamerası görür)

        // ---------------------------------------------------------------- durum
        public string mode = "menu", activeHand = "right";
        public bool labelsOn, xrActive, vrMenuOpen, orView;
        public readonly Dictionary<string, bool> atlasVis = new Dictionary<string, bool>
        { ["labels"] = true, ["fat"] = true, ["inner"] = false, ["bone"] = true, ["gi"] = true, ["muscle"] = true };

        public Procedure proc;
        public readonly Dictionary<string, Instrument> instr = new Dictionary<string, Instrument>();
        Anatomy A, A2;
        Room R;
        UIManager ui;
        Sfx sfx;
        Transform flipRoot, worldRoot, rig, atlasRoot, atlasPivot;
        Camera mainCam, lapCam;
        RenderTexture rt;
        LabelSet surgLabels, atlasLabels;
        VRPanel diag; float diagT, diagLogT; public string lastErr = "-";
        Part usPlane;
        Texture2D usTex; Color32[] usPx;
        const int US_W = 256, US_H = 200;
        readonly XRHand[] hands = { new XRHand("left"), new XRHand("right") };
        readonly Dictionary<string, (LineRenderer line, GameObject body)> ctrlVis = new Dictionary<string, (LineRenderer, GameObject)>();

        // laparoskop kamera durumu (hasta-yerel)
        Vector3 camTarget = V(0.1f, -0.02f, 0.02f), camOffset;
        float camDist = 0.11f;
        const float LAP_EXPOSURE = 1.05f; // laparoskop kamerasının otomatik pozlaması

        // ---------------------------------------------------------------- IProcHost
        public Camera LapCam => lapCam;
        public Sfx Audio => sfx;
        public void Toast(string text, string kind) { ui.AddToast(text, kind); }
        public void StepChanged(Procedure p) { camOffset = Vector3.zero; camDist = p.Cur?.camDist ?? 0.11f; UpdateSurgLabels(); ui.dirty = true; }
        public void ShowReport(Procedure.Report r) => ui.ShowReport(r);
        public bool IsVisible(Vector3 w)
        {
            var v = lapCam.WorldToViewportPoint(w);
            return v.x > 0 && v.x < 1 && v.y > 0 && v.y < 1 && v.z > lapCam.nearClipPlane && v.z < lapCam.farClipPlane;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<LPNApp>()) return;
            var go = new GameObject("LPN-VR");
            go.AddComponent<LPNApp>();
        }

        // ---------------------------------------------------------------- kurulum
        void Start()
        {
            Application.targetFrameRate = 90;
            sfx = GetComponent<Sfx>();
            if (!sfx) sfx = gameObject.AddComponent<Sfx>();
            xrActive = UnityEngine.XR.XRSettings.isDeviceActive;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.enabled = false;

            // S-uzayı (sağ-elli, web sürümüyle aynı sayılar) → Unity dünyası
            flipRoot = Group("S-space (flip)", null).transform;
            flipRoot.localScale = new Vector3(1, 1, -1);
            worldRoot = Group("World UI", null).transform;

            A = new Anatomy(flipRoot);
            SetLayer(A.root.gameObject, LAYER_LAP);
            R = new Room(flipRoot, A);
            // dış görüşte görünmesi gerekenler (cilt, örtü, trokarlar) — geri kalan karın içi yalnız laparoskopta
            SetLayer(A.meshes["skin"].gameObject, 0);
            SetLayer(R.torsoDrape.gameObject, 0);
            foreach (var p in R.ports.Values) SetLayer(p.obj.gameObject, 0);

            // XR rig + ana kamera
            rig = Group("XR Rig", null).transform;
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(rig, false);
            mainCam = camGo.AddComponent<Camera>();
            mainCam.nearClipPlane = 0.01f; mainCam.farClipPlane = 50;
            mainCam.clearFlags = CameraClearFlags.SolidColor; mainCam.backgroundColor = Hex(0xcfd6d8);
            mainCam.cullingMask = ~(1 << LAYER_LAP);
            camGo.AddComponent<AudioListener>();
            if (xrActive)
            {
                var tpd = camGo.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("hmdPos", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
                tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("hmdRot", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
                tpd.ignoreTrackingState = true;
                tpd.positionInput.action.Enable(); tpd.rotationInput.action.Enable();
                SetFloorOrigin();
            }
            else { camGo.transform.position = SW(V(0, 1.55f, 0.35f)); camGo.transform.rotation = Quaternion.LookRotation(Vector3.forward); }

            // Laparoskop kamerası (karın içi + dış)
            var lapGo = new GameObject("Laparoscope");
            lapCam = lapGo.AddComponent<Camera>();
            lapCam.fieldOfView = 70; lapCam.nearClipPlane = 0.003f; lapCam.farClipPlane = 1.2f;
            lapCam.clearFlags = CameraClearFlags.SolidColor; lapCam.backgroundColor = Hex(0x1d2a31);
            lapCam.cullingMask = (1 << LAYER_LAP) | 1;
            lapCam.stereoTargetEye = StereoTargetEyeMask.None;
            lapCam.depth = -10;
            rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "LapRT", antiAliasing = 4 };
            lapCam.targetTexture = rt;
            lapCam.aspect = 16f / 9f;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;

            // Aletler (normal dünya)
            var instRoot = Group("Instruments", null).transform;
            instr["left"] = new Instrument(instRoot, "left");
            instr["right"] = new Instrument(instRoot, "right");

            // Monitör ekranı + US resim-içinde-resim (aynalanmasın diye karşı-çevrilmiş çerçevede)
            var monUnflip = Room.CounterFlip(R.monitor);
            var screenMat = new Material(Mats.MonitorShader); screenMat.SetTexture("_MainTex", rt);
            var screen = Part.Make("screen", Quad(0.96f, 0.54f), screenMat, monUnflip);
            screen.transform.localPosition = V(0, 0, -0.026f);
            usTex = new Texture2D(US_W, US_H, TextureFormat.RGBA32, false) { name = "US", filterMode = FilterMode.Bilinear };
            usPx = new Color32[US_W * US_H];
            var usMat = new Material(Mats.MonitorShader); usMat.SetTexture("_MainTex", usTex);
            usPlane = Part.Make("usPIP", Quad(0.26f, 0.2f), usMat, monUnflip);
            usPlane.transform.localPosition = V(0.33f, -0.15f, -0.031f);
            usPlane.gameObject.SetActive(false);
            // Tanı satırı (monitörün altında): giriş/alet/doku durumu — cihazda sorun ayıklamak için
            diag = new VRPanel("diag", monUnflip, 0.96f, 0.17f, 1400);
            diag.go.transform.localPosition = V(0, -0.39f, -0.03f);
            diag.title.gameObject.SetActive(false);
            VRPanel.Place(diag.body.rectTransform, 24, 10, diag.W - 48, diag.H - 20);
            diag.body.fontSize = 26;

            // UI
            var infoUnflip = Room.CounterFlip(R.panelAnchor);
            ui = new UIManager(worldRoot, infoUnflip);
            ui.hudUS.texture = usTex;
            ui.info.go.transform.localRotation = Quaternion.identity;

            // Atlas (ayrı, büyütülmüş kopya)
            atlasRoot = Group("atlasRoot", flipRoot).transform;
            atlasPivot = Group("atlasPivot", atlasRoot).transform;
            A2 = new Anatomy(atlasPivot, "atlasAnatomy");
            A2.root.localPosition = V(-0.04f, 0, -0.01f);
            atlasRoot.localScale = Vector3.one * 2.6f;
            A2.meshes["skin"].gameObject.SetActive(false); A2.meshes["wall"].gameObject.SetActive(false);
            A2.meshes["fatRetro"].gameObject.SetActive(false); A2.meshes["latPer"].gameObject.SetActive(false);
            Mats.SetTransparent(A2.meshes["meso"].material, true, 0.35f);
            atlasLabels = new LabelSet(A2, worldRoot, 0.00014f, 0);
            atlasRoot.gameObject.SetActive(false);

            surgLabels = new LabelSet(A, worldRoot, 0.00016f, LAYER_LAP);
            surgLabels.SetAll(false);

            // Kontrolcü görselleri (menü/atlas)
            foreach (var h in hands)
            {
                var body = Part.Make("ctrl-" + h.hand, Geo.Box(0.03f, 0.025f, 0.09f), Mats.Std(0x2a2f36, 0.5f), null);
                var lgo = new GameObject("ray-" + h.hand);
                var line = lgo.AddComponent<LineRenderer>();
                line.sharedMaterial = Mats.Unlit(VRPanel.Accent); line.positionCount = 2; line.widthMultiplier = 0.002f; line.useWorldSpace = true;
                ctrlVis[h.hand] = (line, body.gameObject);
                body.gameObject.SetActive(false); lgo.SetActive(false);
            }

            ui.DrawMenu(); ui.DrawAtlas(atlasVis);
            SetMode("menu");
            if (xrActive) Invoke(nameof(ReplaceMenu), 0.6f); // başlık pozu ilk karelerde henüz gelmemiş olabilir
        }

        void OnDestroy() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        static MeshData Quad(float w, float h)
        {
            // -Z yüzüne bakan dörtgen (Unity kuralı), UV soldan sağa
            var g = new MeshData();
            g.pos.AddRange(new[] { V(-w / 2, -h / 2, 0), V(-w / 2, h / 2, 0), V(w / 2, h / 2, 0), V(w / 2, -h / 2, 0) });
            g.nor.AddRange(Enumerable.Repeat(V(0, 0, -1), 4));
            g.uv.AddRange(new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            g.idx.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
            return g;
        }

        static void SetFloorOrigin()
        {
            var subs = new List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.TrySetTrackingOriginMode(UnityEngine.XR.TrackingOriginModeFlags.Floor);
        }

        // S-uzayı ↔ dünya (FlipRoot orijinde, dönüşsüz)
        static Vector3 SW(Vector3 s) => new Vector3(s.x, s.y, -s.z);
        static Quaternion QS(Quaternion q) => new Quaternion(-q.x, -q.y, q.z, q.w); // dünya ↔ S dönüşü (yansıma eşleniği)

        // ---------------------------------------------------------------- ışıklar (her kamera için)
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == lapCam)
            {
                // Laparoskop ışık kaynağı: skopa bağlı spot (0xfff1e0, 2.6, menzil 0.8, koni 1.0 rad, penumbra 0.75, azalma 1)
                var t = lapCam.transform;
                Shader.SetGlobalVector("_LpnLightPos", new Vector4(t.position.x, t.position.y, t.position.z, 1));
                Shader.SetGlobalVector("_LpnLightDir", t.forward);
                Shader.SetGlobalVector("_LpnLightColor", Hex(0xfff1e0).linear * 2.6f);
                Shader.SetGlobalVector("_LpnSpot", new Vector4(0.8f, Mathf.Cos(1.0f), Mathf.Cos(1.0f * (1 - 0.75f)), 1));
                Shader.SetGlobalVector("_LpnSky", Hex(0xffe8e0).linear * 0.3f);
                Shader.SetGlobalVector("_LpnGround", Hex(0x502020).linear * 0.3f);
                Shader.SetGlobalFloat("_LpnExposure", LAP_EXPOSURE);
            }
            else
            {
                // Ameliyathane: yarıküre (1.6) + yönlü ışık (1.5), konum (1, 3, 2)
                Shader.SetGlobalVector("_LpnLightPos", Vector4.zero);
                // laminer akım tavanından aşağı inen ılık-beyaz ışık + açık renkli duvar/zeminden yansıyan ortam ışığı
                Shader.SetGlobalVector("_LpnLightDir", -SW(V(0.35f, 3, 0.9f)).N());
                Shader.SetGlobalVector("_LpnLightColor", Hex(0xfff6ea).linear * 1.7f);
                Shader.SetGlobalVector("_LpnSpot", Vector4.zero);
                Shader.SetGlobalVector("_LpnSky", Hex(0xf1f5f7).linear * 1.15f);
                Shader.SetGlobalVector("_LpnGround", Hex(0xb4babd).linear * 0.85f); // açık zeminden sekme
                Shader.SetGlobalFloat("_LpnExposure", 1.0f);
            }
        }

        // ---------------------------------------------------------------- prosedür
        void NewProcedure()
        {
            proc = new Procedure(this, A);
            camTarget = proc.Cur.focus.Value; camOffset = Vector3.zero;
            camDist = proc.Cur.camDist;
            UpdateSurgLabels();
        }

        void UpdateSurgLabels()
        {
            surgLabels.SetAll(labelsOn);
            if (proc?.Cur == null) { surgLabels.Apply(); return; }
            var f = proc.Cur.focus.Value;
            foreach (var it in surgLabels.items)
            {
                var L = it.L; float d = Vector3.Distance(L.pos, f);
                it.visible = (d < 0.075f && L.system != "bone" && L.system != "muscle") || (L.system == "muscle" && d < 0.05f);
            }
            surgLabels.Apply();
        }

        void UpdateLapCam(float dt)
        {
            if (proc?.Cur == null) return;
            var goal = proc.Cur.focus.Value + camOffset;
            camTarget = Vector3.Lerp(camTarget, goal, 1 - Mathf.Exp(-dt * 2.5f));
            var portW = R.ports["camera"].obj.position;
            var tW = A.root.TransformPoint(camTarget);
            var dir = tW - portW; float dist = dir.magnitude; dir /= dist;
            float ins = Clamp(dist - camDist, 0.02f, 0.22f);
            lapCam.transform.position = portW + dir * ins;
            lapCam.transform.rotation = Quaternion.LookRotation(tW - lapCam.transform.position, Vector3.up);
        }

        void PanCamera(float dx, float dy) // ekran düzleminde (metre)
        {
            var v = lapCam.transform.right * dx + lapCam.transform.up * dy;
            if (v.sqrMagnitude < 1e-12f) return;
            camOffset += A.root.InverseTransformVector(v).N() * Mathf.Sqrt(dx * dx + dy * dy);
            if (camOffset.magnitude > 0.08f) camOffset = camOffset.N() * 0.08f;
        }

        // ---------------------------------------------------------------- mod geçişleri
        void SetMode(string m)
        {
            mode = m;
            R.root.gameObject.SetActive(m != "atlas");
            atlasRoot.gameObject.SetActive(m == "atlas");
            ui.menu.go.SetActive(m == "menu");
            ui.atlas.go.SetActive(m == "atlas");
            ui.info.go.SetActive(m == "surgery");
            foreach (var i in instr.Values) i.root.gameObject.SetActive(m == "surgery");
            if (m == "atlas") { ApplyAtlasVis(); PlaceAtlas(); }
            if (m == "surgery")
            {
                if (proc == null) NewProcedure();
                if (xrActive) Invoke(nameof(Calibrate), 0.4f); else InitDeskTips();
            }
            if (m == "menu") { PlaceInFront(ui.menu.go.transform, 0.9f, -0.05f); ui.DrawMenu(); }
            if (!xrActive)
            {
                if (m == "atlas") { orbitTarget = SW(V(0, 1.3f, -0.6f)); SetOrbitFrom(SW(V(0, 1.4f, 0.45f))); }
                if (m == "menu") { orbitTarget = SW(V(0, 1.0f, -0.5f)); SetOrbitFrom(SW(V(0.6f, 1.7f, 0.6f))); PlaceInFront(ui.menu.go.transform, 0.9f, -0.05f); }
            }
            ApplyDesktopView();
        }

        void ApplyDesktopView()
        {
            bool surg = mode == "surgery" && proc != null;
            bool lapFull = !xrActive && surg && !orView;
            lapCam.targetTexture = lapFull ? null : rt;
            lapCam.depth = lapFull ? 10 : -10;
            lapCam.aspect = lapFull ? (float)Screen.width / Screen.height : 16f / 9f;
            mainCam.enabled = !lapFull;
            ui.HudVisible(!xrActive);
            string help = mode == "surgery"
                ? "Fare: alet ucu · Sol tık/Boşluk: tetik · Sağ tık: el değiştir · Tekerlek: derinlik (Shift: zoom) · Q/E: sol/sağ el · Tab/1-8: alet · WASD: kamera · R/F: zoom · L: etiketler · G: rehber · N: adımı atla · O: ameliyathane görünümü · M: menü"
                : mode == "atlas" ? "Sürükle: döndür · Sağ sürükle: kaydır · Tekerlek: yakınlaştır · Paneldeki düğmelere tıklayın"
                : "Paneldeki düğmelere tıklayın · Sürükle: görünümü döndür";
            ui.SetHud(mode == "surgery", help);
        }

        void HeadPose(out Vector3 p, out Quaternion q) { p = mainCam.transform.position; q = mainCam.transform.rotation; }

        void PlaceInFront(Transform obj, float dist, float dy = 0)
        {
            HeadPose(out var p, out var q);
            var f = q * Vector3.forward; f.y = 0; f = f.N();
            obj.position = p + f * dist; obj.position = new Vector3(obj.position.x, p.y + dy, obj.position.z);
            obj.rotation = Quaternion.LookRotation(obj.position - new Vector3(p.x, obj.position.y, p.z)); // uGUI: kameradan uzağa bakan +Z
        }

        void PlaceAtlas()
        {
            if (xrActive)
            {
                HeadPose(out var p, out var q);
                var f = q * Vector3.forward; f.y = 0; f = f.N();
                var posW = p + f * 0.65f; posW.y = p.y - 0.2f;
                var posS = SW(posW); var headS = SW(new Vector3(p.x, posW.y, p.z));
                atlasRoot.localPosition = posS;
                atlasRoot.localRotation = U.LookAt(posS, headS, Vector3.up); // anterior yüz kullanıcıya
                var right = Vector3.Cross(Vector3.up, f);
                var pp = p + f * 0.7f - right * 0.5f; pp.y = p.y - 0.1f;
                ui.atlas.go.transform.position = pp;
                ui.atlas.go.transform.rotation = Quaternion.LookRotation(pp - new Vector3(p.x, pp.y, p.z));
            }
            else
            {
                atlasRoot.localPosition = V(0, 1.3f, -0.6f); atlasRoot.localRotation = Quaternion.identity;
                var pp = SW(V(-0.55f, 1.35f, -0.45f));
                ui.atlas.go.transform.position = pp;
                ui.atlas.go.transform.rotation = Quaternion.LookRotation(pp - SW(V(0, 1.4f, 0.45f)));
            }
            atlasPivot.localRotation = Quaternion.identity;
        }

        void ApplyAtlasVis()
        {
            var v = atlasVis;
            foreach (var k in new[] { "fat", "bone", "gi", "muscle" }) A2.SetSystem(k, v[k]);
            A2.meshes["midFat"].gameObject.SetActive(false); A2.meshes["fatRetro"].gameObject.SetActive(false); A2.meshes["latPer"].gameObject.SetActive(false); // atlasta büyük damarları örtmesin
            Mats.SetTransparent(A2.K.material, v["inner"], v["inner"] ? 0.28f : 1);
            Mats.SetTransparent(A2.meshes["tumor"].material, v["inner"], v["inner"] ? 0.5f : 1);
            atlasLabels.SetAll(v["labels"]);
            foreach (var it in atlasLabels.items) it.visible = !v.TryGetValue(it.L.system, out bool sv) || sv;
            atlasLabels.Apply();
            ui.DrawAtlas(atlasVis);
        }

        // Hasta/rig'i kullanıcının ellerine göre konumlandır (VR) — S-uzayında, web sürümüyle aynı matematik
        void Calibrate()
        {
            var L = hands[0]; var Rh = hands[1];
            if (!L.connected || !Rh.connected) { Toast("Konumlandırma için iki kontrolcü gerekli", "warn"); return; }
            if (Vector3.Distance(L.position, Rh.position) < 0.15f) { Toast("Ellerinizi omuz genişliğinde açıp tekrar deneyin (iki çubuğa basın)", "warn"); return; }
            var orr = R.root; orr.localPosition = Vector3.zero; orr.localRotation = Quaternion.identity;
            var focusS = SW(A.root.TransformPoint(V(0.075f, -0.01f, 0.015f)));
            Vector3 Neutral(string k)
            {
                var T = SW(R.ports[k].obj.position); var d = focusS - T;
                float inside = Mathf.Min(d.magnitude, Instrument.SHAFT - 0.13f);
                return T - d.N() * (Instrument.SHAFT - inside);
            }
            Vector3 NL = Neutral("left"), NR = Neutral("right");
            Vector3 CL = SW(L.position), CR = SW(Rh.position);
            float Ang(Vector3 v) => Mathf.Atan2(v.x, v.z);
            float psi = Ang(CR - CL) - Ang(NR - NL);
            var qy = Quaternion.AngleAxis(psi * Mathf.Rad2Deg, Vector3.up);
            orr.localRotation = qy;
            var midN = qy * ((NL + NR) * 0.5f);
            var midC = (CL + CR) * 0.5f;
            orr.localPosition = midC - midN;
            Toast("Hasta ellerinize göre konumlandırıldı", "good");
        }

        // ---------------------------------------------------------------- XR güncellemesi
        (float, VRPanel, string) PanelRay(Vector3 o, Vector3 d)
        {
            float best = float.MaxValue; VRPanel bp = null; string bid = null;
            foreach (var P in new[] { ui.menu, ui.atlas, ui.info })
            {
                if (!P.go.activeInHierarchy) continue;
                if (P == ui.info && !(vrMenuOpen || ui.lastReport != null)) continue;
                if (P.Hit(o, d, out float dist, out string id) && dist < best) { best = dist; bp = P; bid = id; }
            }
            return (bp == null ? -1 : best, bp, bid);
        }

        bool grabActive; string grabHand; Quaternion grabQ0, grabMq; Vector3 grabP0, grabMp;
        bool calibLatch;

        void XRUpdate(float dt)
        {
            foreach (var h in hands) h.Poll(rig);
            bool pointerMode = mode != "surgery" || vrMenuOpen || ui.lastReport != null;
            foreach (var h in hands)
            {
                var (line, body) = ctrlVis[h.hand];
                line.gameObject.SetActive(pointerMode && h.connected);
                body.SetActive(h.connected && mode != "surgery");
                if (!h.connected) continue;
                body.transform.SetPositionAndRotation(h.position + h.rotation * V(0, 0, -0.02f), h.rotation);
                if (pointerMode)
                {
                    var d = h.aimRotation * Vector3.forward;
                    var (dist, P, id) = PanelRay(h.aimPosition, d);
                    foreach (var p in new[] { ui.menu, ui.atlas, ui.info }) p.SetHover(p == P ? id : null);
                    line.SetPosition(0, h.aimPosition); line.SetPosition(1, h.aimPosition + d * (dist > 0 ? dist : 2));
                    if (h.Edge("trig", h.triggerValue > 0.55f) && P != null && id != null) OnPanelClick(P, id);
                }
                else h.Edge("trig", h.triggerValue > 0.55f);
            }
            var Lh = hands[0]; var Rh = hands[1];
            if (mode == "atlas")
            {
                // grip ile tut-çevir (S-uzayında)
                foreach (var c in hands)
                {
                    if (!c.connected) continue;
                    bool g = c.gripPressed;
                    var cq = QS(c.rotation); var cp = SW(c.position);
                    if (g && !grabActive) { grabActive = true; grabHand = c.hand; grabQ0 = cq; grabP0 = cp; grabMq = atlasRoot.localRotation; grabMp = atlasRoot.localPosition; }
                    if (grabActive && grabHand == c.hand)
                    {
                        if (!g) grabActive = false;
                        else
                        {
                            var dq = cq * Quaternion.Inverse(grabQ0);
                            atlasRoot.localRotation = dq * grabMq;
                            atlasRoot.localPosition = dq * (grabMp - grabP0) + cp;
                        }
                    }
                    float sy = c.stickValue.y;
                    if (c.hand == "right" && Mathf.Abs(sy) > 0.2f)
                        atlasRoot.localScale = Vector3.one * Mathf.Clamp(atlasRoot.localScale.x * (1 + sy * dt * 0.8f), 0.8f, 6);
                }
            }
            if (mode == "surgery")
            {
                if (Lh.connected && Lh.Edge("y", Lh.secondaryPressed)) { vrMenuOpen = !vrMenuOpen; ui.dirty = true; }
                if (Rh.connected && Rh.Edge("b", Rh.secondaryPressed)) ToggleLabels();
                if (!vrMenuOpen)
                {
                    if (Rh.connected && Rh.Edge("a", Rh.primaryPressed)) CycleTool("right");
                    if (Lh.connected && Lh.Edge("x", Lh.primaryPressed)) CycleTool("left");
                }
                if (Lh.connected && Rh.connected && Lh.stickPressed && Rh.stickPressed) { if (!calibLatch) { calibLatch = true; Calibrate(); } }
                else calibLatch = false;
                if (Lh.connected) { var s = Lh.stickValue; if (s.magnitude > 0.2f) PanCamera(s.x * dt * 0.05f, s.y * dt * 0.05f); }
                if (Rh.connected) { float ay = -Rh.stickValue.y; if (Mathf.Abs(ay) > 0.2f) camDist = Clamp(camDist + ay * dt * 0.06f, 0.035f, 0.16f); }
                // aletler
                foreach (var c in hands)
                {
                    if (!c.connected) continue;
                    var T = R.ports[c.hand].obj.position;
                    instr[c.hand].Update(c.position, T, c.rotation * Vector3.up, vrMenuOpen ? 0 : c.triggerValue);
                }
            }
        }

        void OnPanelClick(VRPanel P, string id)
        {
            sfx.Click();
            if (P == ui.menu) { if (id == "atlas") SetMode("atlas"); if (id == "surgery") SetMode("surgery"); }
            else if (P == ui.atlas) { if (id == "home") SetMode("menu"); else { atlasVis[id] = !atlasVis[id]; ApplyAtlasVis(); } }
            else if (P == ui.info)
            {
                if (id == "guide") ToggleGuide();
                if (id == "labels") ToggleLabels();
                if (id == "skip" && proc != null && !proc.finished) proc.CompleteStep(true);
                if (id == "calib") Calibrate();
                if (id == "resume") vrMenuOpen = false;
                if (id == "home") Restart();
                ui.dirty = true;
            }
        }

        void ReplaceMenu() { if (mode == "menu") PlaceInFront(ui.menu.go.transform, 0.9f, -0.05f); }
        void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        void ToggleLabels() { labelsOn = !labelsOn; UpdateSurgLabels(); ui.dirty = true; }
        void ToggleGuide() { if (proc == null) return; proc.guide = !proc.guide; proc.markers.gameObject.SetActive(proc.guide); ui.dirty = true; }
        void CycleTool(string hand, int idx = -1)
        {
            var set = Instrument.TOOLSETS[hand]; var ins = instr[hand];
            int i = idx >= 0 ? idx : (System.Array.IndexOf(set, ins.tool) + 1) % set.Length;
            if (i < set.Length) { ins.Set(set[i]); sfx.Tick(); ui.dirty = true; }
        }

        // ---------------------------------------------------------------- masaüstü girişi
        Vector3 orbitTarget; float orbitYaw, orbitPitch, orbitDist = 1;
        readonly Dictionary<string, Vector3?> deskTips = new Dictionary<string, Vector3?> { ["left"] = null, ["right"] = null };
        readonly Dictionary<string, float> deskTrig = new Dictionary<string, float> { ["left"] = 0, ["right"] = 0 };
        float deskDepth = 0.0012f; float? deskHold; bool deskDown;

        void SetOrbitFrom(Vector3 camPos)
        {
            var d = camPos - orbitTarget; orbitDist = d.magnitude;
            orbitYaw = Mathf.Atan2(d.x, d.z); orbitPitch = Mathf.Asin(d.y / orbitDist);
            ApplyOrbit();
        }
        void ApplyOrbit()
        {
            orbitPitch = Mathf.Clamp(orbitPitch, -1.4f, 1.4f);
            var off = new Vector3(Mathf.Sin(orbitYaw) * Mathf.Cos(orbitPitch), Mathf.Sin(orbitPitch), Mathf.Cos(orbitYaw) * Mathf.Cos(orbitPitch)) * orbitDist;
            mainCam.transform.position = orbitTarget + off;
            mainCam.transform.rotation = Quaternion.LookRotation(orbitTarget - mainCam.transform.position, Vector3.up);
        }
        void OrbitInput()
        {
            var md = DeskInput.MouseDelta;
            if (DeskInput.MouseHeld(0) && !pointerOverPanel) { orbitYaw += md.x * 0.005f; orbitPitch -= md.y * 0.005f; }
            if (DeskInput.MouseHeld(1))
            {
                var t = mainCam.transform;
                orbitTarget -= (t.right * md.x + t.up * md.y) * 0.001f * orbitDist;
            }
            float w = DeskInput.Wheel; if (w != 0) orbitDist = Mathf.Clamp(orbitDist * (1 + w * 0.001f), 0.2f, 5);
            ApplyOrbit();
        }

        void InitDeskTips()
        {
            foreach (var h in new[] { "left", "right" })
            {
                var T = R.ports[h].obj.position;
                var f = A.root.TransformPoint(proc.Cur.focus.Value);
                deskTips[h] = Vector3.Lerp(T, f, 0.75f);
            }
        }

        List<Part> PickList()
        {
            var l = A.pickables.Where(m => m && m.gameObject.activeInHierarchy).ToList();
            var st = proc?.st;
            if (st?.bowl != null) l.Add(st.bowl.mesh);
            if (st?.bolster) l.Add(st.bolster);
            if (st?.specimen && st.specimen.gameObject.activeInHierarchy) l.AddRange(st.specimen.GetComponentsInChildren<Part>());
            foreach (var c in new[] { st?.clamp, st?.veinClamp }) if (c != null) l.AddRange(c.obj.GetComponentsInChildren<Part>());
            return l;
        }

        static bool ValidHit(Part.Hit h)
        {
            var o = h.part; var p = h.localPoint; var tm = o.tissue;
            if (!o.gameObject.activeInHierarchy) return false;
            if (tm == null) return true;
            if (tm.holes.Count > 0 && tm.InHole(p)) return false;
            if (tm.crater.w > 0 && Vector3.Distance(p, tm.crater) < tm.crater.w + 0.0008f) return false;
            if (tm.planeCut.w > 0) { var e = tm.ellip; if (Sq(p.x / e.x) + Sq(p.y / e.y) + Sq(p.z / e.z) > 1) return false; }
            return true;
        }
        static float Sq(float x) => x * x;

        void DeskKeys()
        {
            if (DeskInput.KeyDown("m") || DeskInput.KeyDown("escape")) { Restart(); return; }
            if (mode != "surgery") return;
            if (DeskInput.KeyDown("q")) { activeHand = "left"; ui.dirty = true; }
            if (DeskInput.KeyDown("e")) { activeHand = "right"; ui.dirty = true; }
            if (DeskInput.KeyDown("tab")) CycleTool(activeHand);
            for (int i = 1; i <= 8; i++) if (DeskInput.KeyDown(i.ToString())) CycleTool(activeHand, i - 1);
            if (DeskInput.KeyDown("l")) ToggleLabels();
            if (DeskInput.KeyDown("g")) ToggleGuide();
            if (DeskInput.KeyDown("n") && proc != null && !proc.finished) proc.CompleteStep(true);
            if (DeskInput.KeyDown("o"))
            {
                orView = !orView;
                if (orView) { orbitTarget = A.root.TransformPoint(V(0.06f, -0.02f, 0.1f)); SetOrbitFrom(orbitTarget + SW(V(0, 0.5f, 0.9f))); }
                ApplyDesktopView();
            }
            if (DeskInput.MouseDown(1)) { activeHand = activeHand == "left" ? "right" : "left"; ui.dirty = true; }
        }

        bool pointerOverPanel;

        void DeskUpdate(float dt)
        {
            DeskKeys();
            if (mode != "surgery" || orView) return;
            float s = dt * 0.04f;
            if (DeskInput.Key("w") || DeskInput.Key("up")) PanCamera(0, s);
            if (DeskInput.Key("s") || DeskInput.Key("down")) PanCamera(0, -s);
            if (DeskInput.Key("a") || DeskInput.Key("left")) PanCamera(-s, 0);
            if (DeskInput.Key("d") || DeskInput.Key("right")) PanCamera(s, 0);
            if (DeskInput.Key("r")) camDist = Clamp(camDist - dt * 0.04f, 0.035f, 0.16f);
            if (DeskInput.Key("f")) camDist = Clamp(camDist + dt * 0.04f, 0.035f, 0.16f);
            float w = DeskInput.Wheel;
            if (w != 0)
            {
                if (DeskInput.Key("shift") || DeskInput.Key("ctrl")) camDist = Clamp(camDist + w * 0.00004f, 0.035f, 0.16f);
                else deskDepth = Clamp(deskDepth + w * 0.00001f, -0.008f, 0.03f);
            }
            if (DeskInput.MouseDown(0)) { deskDown = true; deskHold = null; }
            if (!DeskInput.MouseHeld(0)) deskDown = false;
            string hand = activeHand; var ins = instr[hand];
            float trig = (deskDown || DeskInput.Key("space")) ? 1 : 0;
            if (trig == 0) deskHold = null;
            var ray = lapCam.ScreenPointToRay(DeskInput.MousePos);
            var planeTools = new[] { "grasper", "bulldog", "needle", "clip", "bipolar", "suction" };
            Vector3 tip;
            if (trig > 0 && planeTools.Contains(ins.tool) && deskHold != null) tip = ray.GetPoint(deskHold.Value);
            else
            {
                // makasla eksizyonda tümör sol el ile ekarte ediliyormuş gibi arka kenar da seçilebilir
                var list = PickList().Where(m => !(ins.tool == "scissors" && proc?.Cur?.id == "excise" && m == A.meshes["tumor"])).ToList();
                var hits = Part.RaycastAll(list, ray.origin, ray.direction);
                var hh = hits.Where(ValidHit).Select(x => (Part.Hit?)x).FirstOrDefault();
                if (hh != null)
                {
                    var h = hh.Value;
                    // yüzey normali boyunca hafif yukarıda dur (dokuya dik mesafe ≈ 1.2 mm + tekerlek derinliği)
                    var n = h.normalW;
                    if (Vector3.Dot(n, ray.direction) > 0) n = -n;
                    tip = h.point + n * (deskDepth + 0.0012f);
                }
                else tip = ray.GetPoint(0.09f);
                if (trig > 0 && deskHold == null) deskHold = Vector3.Distance(tip, ray.origin);
            }
            deskTips[hand] = tip;
            deskTrig[hand] = trig;
            deskTrig[hand == "left" ? "right" : "left"] = 0;
            foreach (var hn in new[] { "left", "right" })
            {
                var I = instr[hn]; var T = R.ports[hn].obj.position;
                var P = deskTips[hn] ?? T;
                var d = (P - T).N();
                var H = P - d * (Instrument.SHAFT + I.TipOff);
                I.Update(H, T, Vector3.up, deskTrig[hn]);
            }
        }

        void DeskPanels()
        {
            if (mode == "surgery" && !orView) { pointerOverPanel = false; return; }
            var ray = mainCam.ScreenPointToRay(DeskInput.MousePos);
            var (dist, P, id) = PanelRay(ray.origin, ray.direction);
            pointerOverPanel = P != null;
            foreach (var p in new[] { ui.menu, ui.atlas, ui.info }) p.SetHover(p == P ? id : null);
            if (DeskInput.MouseDown(0) && P != null && id != null) OnPanelClick(P, id);
        }

        // ---------------------------------------------------------------- döngü
        float uiTimer, usTimer;
        readonly Dictionary<string, float> prevTrig = new Dictionary<string, float> { ["left"] = 0, ["right"] = 0 };

        void Update()
        {
            try { Tick(); }
            catch (System.Exception e) { lastErr = e.GetType().Name + ": " + e.Message; Debug.LogException(e); }
            try { Diag(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        void Diag()
        {
            diagT += Time.unscaledDeltaTime; diagLogT += Time.unscaledDeltaTime;
            if (diagT < 0.25f) return; diagT = 0;
            var sb = new System.Text.StringBuilder();
            sb.Append($"mod={mode} xr={xrActive} menü={vrMenuOpen} rapor={ui.lastReport != null}  hata: {lastErr}\n");
            foreach (var h in hands)
            {
                var I = instr[h.hand];
                string near = "-";
                if (A != null && mode == "surgery")
                {
                    float best = float.MaxValue; string bk = "";
                    foreach (var key in new[] { "latPer", "meso", "fatPeri", "fatHilar", "fatRetro" })
                    {
                        var mm = A.meshes[key]; if (!mm.Visible) continue;
                        var lp = mm.transform.InverseTransformPoint(I.tipWorld);
                        for (int i = 0; i < mm.verts.Length; i += 2) { float d = (mm.verts[i] - lp).sqrMagnitude; if (d < best) { best = d; bk = key; } }
                    }
                    near = $"{Mathf.Sqrt(best) * 1000:F0}mm({bk})";
                }
                sb.Append($"{h.hand}: bağlı={h.connected} tetik={h.triggerValue:F2} [{h.source}] alet={I.tool} aletTetik={I.trigger:F2} giriş={I.insertion * 100:F0}cm doku={near} görüş={IsVisible(I.tipWorld)}\n");
            }
            if (proc != null && proc.Cur != null)
                sb.Append($"adım={proc.Cur.id} hedef={proc.Cur.tg?.Count(t => t.done)}/{proc.Cur.tg?.Count} delik={A.meshes["latPer"].tissue?.holes.Count}  ses={(sfx && sfx.enabled)} {AudioSettings.outputSampleRate}Hz");
            diag.body.text = sb.ToString();
            diag.go.SetActive(mode == "surgery");
            if (diagLogT > 2f) { diagLogT = 0; Debug.Log("LPNDIAG " + sb.ToString().Replace("\n", " | ")); }
        }

        void Tick()
        {
            float dt = TestDt > 0 ? TestDt : Mathf.Min(0.05f, Time.deltaTime);
            if (xrActive) XRUpdate(dt);
            else
            {
                DeskPanels();
                if (mode != "surgery" || orView) OrbitInput();
                DeskUpdate(dt);
            }
            if (!lapCam.targetTexture) lapCam.aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (mode == "surgery" && proc != null)
            {
                UpdateLapCam(dt);
                var tools = new[] { "left", "right" }.Select(h =>
                {
                    var I = instr[h];
                    var o = new ToolState { hand = h, tool = I.tool, tipW = I.tipWorld, dirW = I.dir, trigger = I.trigger, prevTrigger = prevTrig[h], active = true };
                    prevTrig[h] = I.trigger; return o;
                }).ToArray();
                var hap = proc.Update(dt, tools);
                if (xrActive) { hands[0].Haptic(hap[0], 0.03f); hands[1].Haptic(hap[1], 0.03f); }
                foreach (var h in new[] { "left", "right" }) instr[h].HasLoad(new[] { "bulldog", "needle", "clip" }.Contains(instr[h].tool));
                // US
                usTimer += dt;
                var usT = tools.FirstOrDefault(t => t.tool == "us");
                bool showUS = usT != null && proc.ToK(usT.tipW).magnitude < 0.07f;
                usPlane.gameObject.SetActive(showUS);
                ui.hudUS.gameObject.SetActive(showUS && !xrActive && !orView);
                if (showUS && usTimer > 0.12f)
                {
                    usTimer = 0; usT.k = proc.ToK(usT.tipW);
                    proc.DrawUltrasound(usPx, US_W, US_H, usT);
                    usTex.SetPixels32(usPx); usTex.Apply(false);
                }
                surgLabels.Update(lapCam, 1);
            }
            if (mode == "atlas") atlasLabels.Update(mainCam, atlasRoot.localScale.x);
            uiTimer += dt;
            if (uiTimer > 0.25f || ui.dirty)
            {
                uiTimer = 0; ui.dirty = false;
                if (mode == "surgery") { if (!xrActive) ui.Hud(this); ui.DrawInfo(this); }
            }
        }
    }
}
