# LPN-VR — Unity 6 / Meta Quest 3S yerel uygulaması

Web (WebXR) sürümünün Unity 6 + OpenXR ile yazılmış yerel (APK) karşılığı. Anatomi, cerrahi adımlar,
hata/komplikasyon mantığı, skor ve rapor web sürümüyle **birebir aynıdır**: tüm ölçüler ve eşik değerleri
`src/*.js` dosyalarından satır satır C#'a aktarıldı (bkz. *Port doğrulaması*).

| | |
|---|---|
| Unity | **6000.3.27f1 LTS** (Android Build Support modülüyle) |
| Render | URP 17.3 (MSAA 4×, HDR kapalı, gölge yok) — özel, hafif doku shader'ı |
| XR | OpenXR 1.19 + *Meta Quest Support*, Oculus Touch / **Touch Plus** (Quest 3/3S) profilleri |
| Giriş | Input System 1.20 (kontrolcüler), masaüstü için fare/klavye yedeği |
| Android | IL2CPP, ARM64, Vulkan, min API 32, ASTC, `tr.edu.sbu.lpnvr` |

## Kurulum (ilk kez)

1. **Unity Hub** → *Installs* → **6000.3.27f1** + *Android Build Support* (OpenJDK + SDK/NDK dahil).
2. Unity Hub → *Projects* → **Add → Add project from disk** → bu klasör (`unity/LPN-VR`).
3. Proje açılınca paketler indirilir; ardından **kurulum betiği otomatik çalışır** ve şunları yapar:
   URP varlığı, OpenXR (Android + Windows) ve Meta Quest özellikleri, oyuncu ayarları, `Lap` katmanı,
   shader referansları, `Assets/LPNVR/Scenes/Main.unity` sahnesi. Sonunda platformu Android'e geçirmeyi önerir.
   Gerekirse elle: menü **LPN-VR ▸ 1. Projeyi Kur (Quest 3S)**.
4. "Giriş sistemi ayarı değişti" uyarısı çıkarsa Unity'yi bir kez yeniden başlatın.
5. Oluşan `.meta`, `Assets/XR`, `Assets/LPNVR/{Settings,Scenes,Resources}` ve `ProjectSettings/` dosyalarını commit edin.

## Quest 3S'e yükleme

1. Quest'te **geliştirici modunu** açın (Meta Horizon uygulaması → cihaz → Geliştirici modu).
2. USB-C ile bilgisayara bağlayın, başlıkta "USB hata ayıklamaya izin ver" → *Her zaman*.
3. Unity: **LPN-VR ▸ 2. Quest'e Derle ve Çalıştır** (`Builds/LPN-VR.apk` derlenir, yüklenir, açılır).
   Yalnız APK için **LPN-VR ▸ 3. Yalnız APK Derle**; sonra Meta Quest Developer Hub veya
   `adb install -r Builds/LPN-VR.apk` ile yükleyin. Uygulama başlıkta *Kitaplık ▸ Bilinmeyen kaynaklar* altında görünür.

**Editor'da test**: başlık takılı değilken *Play* → masaüstü modu (web sürümüyle aynı fare/klavye kontrolleri).
Quest Link / Air Link ile bağlıysa Play doğrudan VR'da çalışır (Windows, OpenXR çalışma zamanı = Meta).

## APK'yı GitHub'da derlemek (Unity kurmadan)

`.github/workflows/quest-apk.yml` iş akışı projeyi GameCI ile Linux'ta derler ve APK'yı
**Actions ▸ Quest APK ▸ son çalıştırma ▸ Artifacts ▸ LPN-VR-Quest3S-apk** altında verir.
Bir kez şu repo secret'ları eklenmeli (*Settings ▸ Secrets and variables ▸ Actions*):

| Secret | Değer |
|---|---|
| `UNITY_EMAIL`, `UNITY_PASSWORD` | Unity hesabı |
| `UNITY_LICENSE` | Unity Hub'da Personal lisans etkinleştirildikten sonra `Unity_lic.ulf` dosyasının **içeriği** — Windows: `C:\ProgramData\Unity\Unity_lic.ulf`, macOS: `/Library/Application Support/Unity/Unity_lic.ulf`, Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf` |

Ardından *Actions ▸ Quest APK ▸ Run workflow* (ya da `unity/LPN-VR` altına her push). Komut satırı eşdeğeri:
`Unity -batchmode -projectPath unity/LPN-VR -buildTarget Android -executeMethod LPNVR.EditorTools.ProjectSetup.BuildCI -quit`.

## Kontroller

**Quest 3S** — web sürümüyle aynı:
- Kontrolcüler = laparoskopik alet kolları; alet ucu trokar **fulkrumu** üzerinden hareket eder. **Tetik**: kullan.
- **A / X**: alet değiştir · **B**: etiketler · **Y**: menü (ışınla seç: rehber, adımı atla, konumlandır, ana menü)
- **Sol çubuk**: kamera kaydır · **Sağ çubuk ↑↓**: zoom · **iki çubuğa birlikte bas**: hastayı ellerinize göre konumlandır
- Atlas: **grip** ile modeli tut-çevir, sağ çubuk: ölçek

**Masaüstü (Editor)**: fare = aktif alet ucu · sol tık / Boşluk: tetik · sağ tık, Q/E: el · Tab, 1–8: alet ·
tekerlek: derinlik (Shift: zoom) · WASD: kamera · R/F: zoom · L: etiket · G: rehber · N: adımı atla ·
O: ameliyathane görünümü · M/Esc: menü.

## Mimari

```
Assets/LPNVR/
  Scripts/Core/      Util (vektör, CatmullRom 'centripetal', Frenet), Geo (Three.js geometrilerinin eşleri),
                     ProcTextures (prosedürel dokular), Mats (materyal + doku durumu: delik/krater/ton),
                     Part (mesh + CPU ışın testi, Three.js taraf kurallarıyla)
  Scripts/Anatomy/   Anatomy.cs  ← anatomy.js   ·   Room.cs ← room.js
  Scripts/Sim/       Procedure.cs ← sim.js (11 adım, diseksiyon, kanama, iskemi, renorafi, skor, US)
                     Instrument.cs ← instruments.js (fulkrum kinematiği) · Effects.cs (kan/duman parçacıkları)
  Scripts/UI/        VRPanel (dünya-uzayı uGUI, ışınla tıklama), UIManager ← ui.js, Labels (Latince/Türkçe etiketler)
  Scripts/App/       LPNApp ← main.js (modlar, laparoskop kamerası, kalibrasyon, XR + masaüstü girişi),
                     XRHands (OpenXR kontrolcüler, haptik), Sfx ← audio.js (sentetik ses), DeskInput
  Shaders/           Tissue (yumuşak doku çökmesi, diseksiyon delikleri, eksizyon krateri, iskemi tonu,
                     GGX + ACES — Three.js MeshStandardMaterial'e denk), Points, Unlit, Monitor
  Editor/            ProjectSetup (tek tık kurulum + derleme menüleri)
```

**Koordinat kuralı.** Anatomi verisi web sürümüyle aynı sayılarla, sağ-elli hasta-yerel uzayda
(+X sol, +Y kraniyal, +Z anterior) tutulur ve ölçeği `(1, 1, -1)` olan tek bir kökün altına yerleştirilir;
böylece Unity'nin sol-elli dünyasında aynalanmadan görünür. Kamera, kontrolcüler, aletler ve UI paneller
bu kökün dışındadır; iki dünya arasında yalnız `TransformPoint/InverseTransformPoint` ile geçilir.
Not: Unity'nin `Vector3.normalized` değeri |v| < 1e-5 için sıfır döndürür; milimetre ölçekli anatomide bu
yanlış olduğundan kod genelinde eşiksiz `v.N()` kullanılır.

**Görüntüleme.** Karın içi yapılar `Lap` katmanındadır ve yalnız laparoskop kamerası (1280×720, MSAA 4×)
tarafından monitöre çizilir; VR görüşü yalnız ameliyathaneyi, cildi, örtüleri ve trokarları çizer (Quest GPU yükü).
Laparoskop kamerasında skopa bağlı spot ışık + otomatik pozlama (1.9), oda görüşünde yönlü ışık kullanılır.

## Port doğrulaması

Unity 6000.3.27f1'in kendi referans derlemeleri ve Roslyn derleyicisiyle (uGUI ve Input System kaynaktan derlenerek)
tüm çalışma zamanı ve Editor kodu hatasız/uyarısız derlendi. Geometri çekirdeği Three.js r160 ile sayısal olarak
karşılaştırıldı (≤1e-6 m fark): böbrek dönüşümü (Euler XZY), hiler ven noktası, renal ven eğrisinin yay-uzunluğu
örnekleri, tüp halkası köşeleri, böbrek mesh köşe/normali, kamera portu yüzey noktası ve normali.
Shader'lar ve cihaz üzerindeki davranış henüz Unity Editor / Quest 3S'te çalıştırılarak doğrulanmadı.

## Web sürümünden farklar / bilinen sınırlamalar
- Karma gerçeklik (passthrough) modu henüz yok (Meta OpenXR + AR Foundation gerektirir).
- Tümör mesh'i ikosahedron yerine UV küreden türetildi (aynı yarıçap ve yüzey pürüzü, daha yumuşak gölgeleme).
- Kabarcık (bump) dokuları kullanılmadı; renk dokuları aynı algoritmayla CPU'da üretilir.
- Atlas VR'da ön (anterior) yüz kullanıcıya bakacak şekilde konumlanır.
- Diğer sınırlamalar web sürümüyle aynı (bkz. kök README).
