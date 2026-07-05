# Freaky Nikki — Geliştirme Planı

Windows'ta aynı anda birden fazla kulaklığa (Bluetooth + kablolu karışık) ses veren minimal, tek exe'lik open source uygulama.

## 1. Mimari Özeti

```
┌─────────────────────────────────────────────────────────┐
│  Sistem sesi (Spotify, YouTube, oyun...)                │
│        │                                                │
│        ▼                                                │
│  Varsayılan çıkış cihazı (Kulaklık 1)  ◄── ekstra       │
│        │                                    gecikme YOK │
│        ▼                                                │
│  WASAPI Loopback Capture (aynı sesin kopyası)           │
│        │                                                │
│        ├──► [Resample] ─► [Volume] ─► [Delay] ─► Kulaklık 2
│        └──► [Resample] ─► [Volume] ─► [Delay] ─► Kulaklık N
└─────────────────────────────────────────────────────────┘
```

Temel prensip: **Varsayılan cihaz normal çalmaya devam eder** (sıfır ek gecikme). Uygulama sistem sesinin kopyasını yakalayıp seçilen ek cihazlara basar. Cihazlar arası senkron farkı, cihaz başına delay slider'ıyla kullanıcı tarafından ayarlanır.

Neden bu yöntem: Sanal ses sürücüsü (VB-Cable tarzı) imzalı kernel driver gerektirir; APO cihaz başına kurulum ister. WASAPI loopback tamamen user-mode çalışır, kurulum/sürücü gerektirmez — minimal bir OSS proje için tek gerçekçi seçenek.

## 2. Stack

- **.NET 8**, `net8.0-windows`, WPF (tek pencere + tray)
- **NAudio 2.x** — `WasapiLoopbackCapture`, `WasapiOut`, `MMDeviceEnumerator`
- Yayın: framework-dependent single-file exe (~2 MB, .NET 8 runtime ister) + self-contained varyant (~90 MB, hiçbir şey istemez). İkisi de Release'e eklenir.

## 3. Proje Yapısı

```
freaky-nikki/
├── design/                    # bu plan
├── src/FreakyNikki/
│   ├── FreakyNikki.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / .cs   # UI
│   ├── Audio/
│   │   ├── AudioEngine.cs      # loopback capture + yayın döngüsü
│   │   ├── OutputChannel.cs    # cihaz başına: buffer→resample→volume→delay→WasapiOut
│   │   └── DeviceMonitor.cs    # cihaz listesi + hot-plug (IMMNotificationClient)
│   ├── Settings/
│   │   └── SettingsStore.cs    # %AppData%\FreakyNikki\settings.json
│   └── Tray/TrayIcon.cs
├── .github/workflows/release.yml
├── README.md, LICENSE (MIT), CHANGELOG.md, .gitignore, .editorconfig
```

## 4. Fazlar (adım adım)

### Faz 0 — Repo iskeleti
1. `git init`, MIT LICENSE, .gitignore (VisualStudio şablonu), .editorconfig
2. README taslağı: ne yapar, kısıtlar (BT bant genişliği, senkron), kurulum
3. İlk commit

### Faz 1 — Proje iskeleti
1. `dotnet new` ile WPF projesi (`src/FreakyNikki`), NAudio paketi
2. Boş pencere açılıp kapanıyor, `dotnet build` temiz → commit

### Faz 2 — Ses motoru çekirdeği (en kritik faz)
1. `WasapiLoopbackCapture` ile varsayılan render cihazından yakalama başlat
2. `OutputChannel`: yakalanan her buffer'ı cihaza yaz:
   - `BufferedWaveProvider` (taşmada eskiyi at — `DiscardOnBufferOverflow`)
   - Format farkıysa resample (`WdlResamplingSampleProvider`; 44.1k↔48k dönüşümü şart)
   - `VolumeSampleProvider` (cihaz başına ses)
   - Delay: başa sessizlik ekleyen offset provider (0–500 ms, çalışırken değiştirilebilir)
   - `WasapiOut` shared mode, ~100 ms latency buffer (BT için güvenli başlangıç)
3. Tek bir ikinci cihaza yayın çalışır hale gelince test → commit
4. Kenar durumlar:
   - Ses yokken loopback event gelmez → sessizlikte output buffer'ı beslemeye devam et (underrun'da pad)
   - Saat kayması (drift): buffer doluluk hedefinin dışına çıkınca örnek at/ekle
   - Varsayılan cihaz değişirse capture'ı yeniden başlat

### Faz 3 — Cihaz yönetimi
1. `MMDeviceEnumerator` ile aktif render cihazlarını listele (ad + ID)
2. `IMMNotificationClient` ile tak/çıkar/varsayılan-değişti olaylarını dinle, UI'a yansıt
3. Yayın sırasında cihaz koparsa: kanalı düşür, hata gösterme yerine durum ikonu, cihaz dönerse otomatik devam et

### Faz 4 — UI
1. Tek pencere: cihaz listesi, her satırda checkbox (yayına dahil et) + volume + delay slider + durum noktası
2. Varsayılan cihaz satırı işaretli ve kilitli ("zaten çalıyor" etiketi)
3. Büyük tek Start/Stop düğmesi; kapatınca tray'e küçül, tray menüsü: Aç / Start-Stop / Çık
4. Minimal görünüm: sabit boyutlu dar pencere, tema yok, süs yok

### Faz 5 — Ayar kalıcılığı
1. `settings.json`: cihaz ID → {enabled, volume, delayMs}; pencere durumu; otomatik başlat seçeneği
2. Uygulama açılışında bilinen cihazlar için ayarları geri yükle

### Faz 6 — Sağlamlaştırma
1. Exclusive-mode çakışması, erişim reddi gibi WASAPI hatalarını yakala, satır bazında durum göster
2. Basit dosya logu (%AppData%\FreakyNikki\log.txt, döngüsel)
3. Gerçek cihazlarla test matrisi: 2×BT, BT+kablolu, cihaz çekme, uyku/uyanma

### Faz 7 — Paketleme ve Release
1. `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true` (fd + self-contained iki varyant)
2. `.github/workflows/release.yml`: `v*` tag push → windows-latest'ta build → iki exe'yi GitHub Release'e artifact olarak ekle, CHANGELOG'dan release notu
3. README'yi tamamla (ekran görüntüsü, indirme linki, SSS: "neden yankı duyuyorum → delay ayarla")
4. `v0.1.0` tag'le, ilk release

## 5. Riskler / Bilinen Kısıtlar

| Risk | Etki | Önlem |
|---|---|---|
| Cihazlar arası gecikme farkı (BT ~100–250 ms, cihaza göre değişir) | Yankı hissi | Cihaz başına delay slider; README'de anlatım |
| Tek BT adaptöründe 2 A2DP stream | Takılma/cızırtı (zayıf adaptörlerde) | Bizim kontrolümüzde değil; README'ye not |
| Saat kayması (uzun oturumda senkron bozulur) | Dakikalar içinde kayma | Faz 2.4 drift düzeltmesi |
| Sample rate uyuşmazlığı | Hışırtı/hız bozukluğu | Zorunlu resample katmanı |
| Uyku/uyanma sonrası ölü stream | Ses kesilir | Cihaz olaylarında capture/output yeniden başlatma |

## 6. Sürümleme

Semantic versioning. `v0.1.0`: çekirdek yayın + UI. `v0.2.x`: otomatik başlatma, dil desteği vb. CHANGELOG "Keep a Changelog" formatında.
