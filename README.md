# LPN-VR — Laparoskopik Parsiyel Nefrektomi VR Simülatörü (Meta Quest 3S)

Sol böbrek alt pol tümörü için **transperitoneal laparoskopik parsiyel nefrektomi** eğitim simülatörü.
WebXR + Three.js ile yazılmıştır; Quest 3S'in kendi tarayıcısında (Meta Quest Browser) kurulum gerektirmeden çalışır.
Aynı uygulama masaüstünde fare/klavye ile de oynanabilir.

> Eğitim amaçlıdır. Anatomi, literatürdeki erişkin ortalama ölçülere göre prosedürel olarak modellenmiştir;
> hasta-spesifik planlama veya klinik karar için kullanılmamalıdır.

## İçerik

| Mod | Açıklama |
|---|---|
| **Anatomi Atlası** | Retroperitonun 2.6× büyütülmüş modeli; Latince/Türkçe etiketler, sistem aç/kapa (yağ, kemik, GİS, kas), şeffaf böbrekte pelvikaliseal sistem ve segmental arterler. VR'da grip ile tutup çevirilir. |
| **Ameliyat** | Ameliyathane, 60° sağ lateral dekübit hasta, 4 trokar, karşıda laparoskopi monitörü. Kontrolcüler alet kollarıdır; alet ucu trokar **fulkrumu** üzerinden hareket eder (el sağa → uç sola). |

### Cerrahi adımlar (11)
1. Toldt beyaz çizgisi insizyonu → inen kolon + mezokolon + pankreas kuyruğu medialize olur
2. Gonadal venin tanımlanması, üreterin kavranıp askıya alınması
3. Hiler diseksiyon — renal ven, sonra posterior-süperiordaki renal arter (VAP sırası)
4. Gerota fasyasının açılması, tümör çevresi defatlama
5. İntraoperatif laparoskopik US (canlı, prosedürel B-mod görüntü monitörde PIP)
6. Bulldog ile renal arter klempi → **WIT** sayacı başlar (yalnız ven klempi = konjesyon uyarısı; klempsiz eksizyon da mümkün → kanama)
7. Soğuk makasla 360° eksizyon (marj ölçümü: 2–5 mm ideal; kapsül ihlali = pozitif sınır)
8. İç kat renorafi (sürekli dikiş, 4 nokta)
9. Dış kat renorafi — kayan Hem-o-lok tekniği (3 dikiş) + hemostatik bolster
10. Klempin kaldırılması, reperfüzyon, hemostaz kontrolü
11. Spesimenin endobag'e alınması → **Performans raporu**

### Ölçülen metrikler
Toplam süre, WIT, tahmini kan kaybı (ml), cerrahi sınır (ortalama/minimum mm), damar yaralanmaları
(renal ven/arter, aorta, VCI, gonadal, adrenal, lomber ven), üreter ve kolon termal hasarı,
görüş alanı dışında enerji kullanımı, "trifecta" (negatif sınır + WIT < 25 dk + majör komplikasyon yok), 100 üzerinden skor.

Diseksiyon gerçek zamanlıdır: koter/makas yağ ve periton yüzeyinde delik açar (shader tabanlı), hedef yapılar
ancak **kamera açısından görünür hale geldiğinde** "açığa çıkmış" sayılır. Alet ucu dokuya bastığında doku çöker,
kontrolcü titreşir. Kanamalar damlacık + göllenme ile görünür; aspiratör temizler, bipolar küçük damarları,
Hem-o-lok majör damarları durdurur.

## Anatomik model (hasta koordinatları: +X sol, +Y kraniyal, +Z anterior; orijin L1 korpusu)

| Yapı | Modeldeki değer | Kaynak/ölçü mantığı |
|---|---|---|
| Sol böbrek | 11 × 6 × 3.5 cm, T12–L3, hilus L1 | Gray's Anatomy; BT morfometri ortalamaları |
| Böbrek yönelimi | uzun eksen etrafında 30° (hilus anteromedial), üst pol 15° medial, 12° posterior | Klasik anatomi tarifleri |
| Hilus sırası | anteriordan posteriora Ven – Arter – Pelvis | |
| Sol renal ven | Ø ~10 mm, ~8 cm, aortanın önünden SMA altından VCI'ya | gonadal (inf.), adrenal (sup.), lomber (post.) dalları |
| Sol renal arter | Ø ~6 mm, L1 düzeyi, venin posterior-süperiorunda; hilusta apikal/üst/orta/alt + posterior segmental dallar | |
| Üreter | psoas önünde; gonadal ven proksimalde medialinde, aşağıda önünden çaprazlar | |
| Aorta / VCI | Ø 19 / 21 mm; bifurkasyon L4 | |
| Komşuluklar | dalak (superolateral), pankreas kuyruğu (hilus önü), inen kolon (lateral kenar önü), adrenal (üst pol medial), psoas, quadratus lumborum, 10–12. kostalar | |
| Tümör | 3.0 cm, alt pol anterior, >%50 ekzofitik, toplayıcı sisteme ~5 mm → **R.E.N.A.L. 1+1+2+1 = 5a** | cT1a |
| Portlar | kamera: umbilikus lateral-kraniyal (12 mm), sol el: subkostal MCL (12 mm), sağ el: umbilikus altı lateral rektus (12 mm), asistan 5 mm | standart transperitoneal sol LPN |

Kod: [src/anatomy.js](src/anatomy.js) (tüm ölçüler ve yorumlar), [src/room.js](src/room.js) (pozisyon, portlar).

## Yerel Quest uygulaması (Unity 6)
Aynı simülatörün Unity 6000.3 LTS + OpenXR ile yazılmış yerel APK sürümü [`unity/LPN-VR`](unity/LPN-VR) klasöründedir
(anatomi, adımlar, skor birebir aynı). Kurulum ve Quest 3S'e yükleme: [unity/LPN-VR/README.md](unity/LPN-VR/README.md).

## Kontroller

**Quest 3S (VR)**
- Kontrolcüler = laparoskopik alet kolları. **Tetik**: kullan (kavra / kes / koter / klips).
- **A** (sağ) / **X** (sol): alet değiştir. Sağ: hook, makas, bipolar, bulldog, portegü, Hem-o-lok, aspiratör, US. Sol: grasper, aspiratör, bipolar, US.
- **Sol çubuk**: kamerayı kaydır (asistan) · **Sağ çubuk ↑↓**: zoom (skop ilerlet/geri çek)
- **B**: anatomik etiketler · **Y**: menü (ışınla seç: rehber, adımı atla, konumlandır)
- **İki çubuğa birlikte bas**: hastayı/trokarları ellerinizin o anki konumuna göre yeniden konumlandır.
  Ameliyat başlarken ellerinizi rahat çalışma pozisyonunda, omuz genişliğinde tutun.

**Masaüstü**
- Fare: aktif aletin ucu · Sol tık / Boşluk: tetik · Sağ tık veya Q/E: el · 1–8 / Tab: alet · Tekerlek: derinlik
- W A S D: kamera · R / F (veya Ctrl+tekerlek): zoom · L: etiket · G: rehber · O: ameliyathane görünümü · N: adımı atla · M: menü

## Çalıştırma

### Bilgisayarda
```bash
powershell -ExecutionPolicy Bypass -File serve.ps1 -Port 8080
```
Ardından tarayıcıda `http://localhost:8080`. (Three.js r160 jsDelivr CDN'den yüklenir; internet gerekir.)

### Quest 3S'e yükleme
WebXR yalnız **HTTPS** veya **localhost** üzerinde çalışır. İki yol:

**A) GitHub Pages (önerilen, kablosuz)**
1. Bu klasörü bir GitHub deposuna gönderin, *Settings → Pages → Deploy from branch (main, /root)*.
2. Quest Browser'da `https://<kullanıcı>.github.io/<depo>/` adresini açın → **VR'a gir**.
3. Tarayıcı menüsünden "Uygulama olarak yükle" (PWA) ile kütüphaneye ekleyebilirsiniz.

**B) USB kablo ile (geliştirici modu)**
1. Quest'te geliştirici modunu açın (Meta Horizon uygulaması), Meta Quest Developer Hub veya `adb` kurun.
2. Bilgisayarda `serve.ps1`'i çalıştırın, sonra:
```bash
adb reverse tcp:8080 tcp:8080
```
3. Quest Browser'da `http://localhost:8080` → **VR'a gir**.

**Karma gerçeklik**: "Karma gerçeklik (passthrough)" düğmesi Quest 3S'in renkli passthrough'u ile sanal hastayı odanıza yerleştirir.

**Bağımsız APK (isteğe bağlı)**: PWA'yı [PWABuilder](https://www.pwabuilder.com) → *Meta Quest* paketiyle APK'ya çevirip MQDH ile yükleyebilirsiniz.

## Dosyalar
```
index.html              arayüz, import map, masaüstü HUD
src/main.js             render döngüsü, XR oturumu, kontrolcüler, masaüstü girişi, kalibrasyon
src/anatomy.js          prosedürel anatomi (ölçüler ve kaynak notlarıyla)
src/room.js             ameliyathane, hasta pozisyonu, trokarlar, monitör
src/instruments.js      aletler ve fulkrum kinematiği
src/sim.js              adım motoru, diseksiyon, kanama, iskemi, renorafi, skor, US görüntüsü
src/util.js             tüp geometrisi, prosedürel dokular, doku shader'ı (deformasyon + delik)
src/ui.js, audio.js     VR panelleri / HTML HUD, sentetik sesler
tests/autoplay.js       tüm ameliyatı masaüstü giriş yoluyla otomatik oynatan test
```
Test: masaüstü modunda konsola `await (await import('/tests/autoplay.js')).autoplay()` yazın;
11 adımın tamamlandığını ve raporu döndürür.

## Bilinen sınırlamalar ve sonraki adımlar
- **Hasta-spesifik anatomi**: Şu an prosedürel. BT segmentasyonundan (ör. KiTS23 veri seti, 3D Slicer → GLB)
  böbrek/tümör/damar ağacı yüklenerek gerçek vaka kullanılabilir; `buildAnatomy` GLB yükleyiciyle değiştirilebilir.
- **Doku fiziği** basitleştirilmiştir (yüzey çökmesi + shader delikleri; hacimsel kesme, iplik fiziği, düğüm atma yok).
- **Haptik**: yalnız kontrolcü titreşimi; kuvvet geri bildirimi yok.
- 30° optik ve skop rotasyonu modellenmedi; kamera asistan tarafından hedefe otomatik yönlendirilir.
- Geçerlilik çalışması (yüz/içerik/yapı geçerliliği; uzman vs. acemi karşılaştırması) yapılmadı.
