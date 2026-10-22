# WatchingYou mobil tətbiqi (iOS + Android)

Tətbiq **Capacitor** ilə qurulub: saytın React kodunu təkrar istifadə edir, həqiqi iOS və Android
tətbiqi kimi yığılır. Yalnız **kino biletləri** satılır — film icarəsi tətbiqdə yoxdur, çünki Apple
və Google tətbiqdaxili rəqəmsal məzmunu (icarə) yalnız öz ödəniş sistemləri ilə satmağa icazə verir.
Kino bileti isə real həyatda istifadə olunan xidmətdir və kartla ödənilə bilər.

## Tətbiqdə nə var

| Tab | Nə edir |
|---|---|
| **Filmlər** | Seanslar, yer xəritəsi, 3D zal görünüşü, ödəniş |
| **Biletlərim** | QR kodlu biletlər — telefonda saxlanılır, **internetsiz də açılır** |
| **Qapı** | *Yalnız Security və Admin üçün:* kamera ilə bileti skan et, böyük ✓ / ✗ cavabı |
| **Hesab** | Giriş, qeydiyyat, profil, loyallıq xalları, **hesabı silmək**, məxfilik siyasəti |

- Dil (AZ/EN/RU/TR) və tema (tünd/açıq) yuxarıdan dəyişilir.
- **Xatırlatma:** seansdan 1 saat əvvəl telefonun öz bildirişi gəlir (server və Firebase lazım deyil).
  Bildirişə basanda Biletlərim açılır. İcazə yalnız ilk bilet olanda soruşulur.
- **Stripe:** ödəniş tətbiqdaxili brauzerdə açılır; bağlayıb tətbiqə qayıdanda bilet özü təsdiqlənir.
- Android-in "geri" düyməsi əvvəlcə Filmlərə qaytarır, sonra tətbiqdən çıxır.

**Mağaza tələbləri — hazırdır:** hesabı silmək (Apple 5.1.1(v), Google Play) və
`/privacy` məxfilik siyasəti səhifəsi (4 dildə). `appsettings.json` → `Legal` bölməsində
**şirkət adını və əlaqə e-poçtunu** mütləq doldurun — səhifədə görünür.

## Fayllar

| Yer | Nədir |
|---|---|
| `client/mobile.html`, `client/src/mobile/` | Tətbiqin ekranları (3 tab) |
| `client/src/lib/platform.ts` | Sayt və tətbiq arasındakı fərqlər — bir yerdə |
| `client/capacitor.config.ts` | Tətbiqin adı və kimliyi: `az.watchingyou.cinema` |
| `client/android/`, `client/ios/` | Yerli Android və iOS layihələri |
| `client/resources/` | İkon və açılış ekranının mənbə şəkilləri |
| `codemagic.yaml` | Buludda yığım və mağazaya göndərmə (iOS üçün Mac lazım deyil) |
| `.github/workflows/android-apk.yml` | Mağazasız test APK-sı (GitHub-da) |
| `docs/STORE_LISTING.md` | Mağaza mətnləri 4 dildə, məxfilik anketlərinin cavabları |

> ⚠️ `appId` (`az.watchingyou.cinema`) mağazaya ilk yükləmədən sonra **dəyişdirilə bilməz**.
> Başqa ad istəyirsinizsə, indi `client/capacitor.config.ts`-də dəyişin.

## 1. Kompüterdə sınamaq

```bash
cd client
npm run dev:mobile
```

Brauzerdə `http://localhost:5174/mobile.html` açın (F12 → telefon görünüşü). Sayt
(`https://localhost:7139`) eyni anda işləməlidir — tətbiq onun API-sinə qoşulur.

## 2. Serveri internetə çıxarmaq (məcburi)

Telefon `localhost`-a qoşula bilməz. Sayt HTTPS ilə real ünvanda işləməlidir, məsələn
`https://api.watchingyou.az`. Sonra:

- Codemagic-də `WY_API_BASE` = həmin ünvan (aşağıya bax).
- `appsettings.json` → `Mobile:AllowedOrigins` artıq tətbiqin ünvanlarını ehtiva edir
  (`capacitor://localhost` — iPhone, `https://localhost` — Android). Dəyişmək lazım deyil.
- Real ödəniş üçün Stripe-ın **live** açarını serverin gizli ayarlarına qoyun. Daxili "test ödənişi"
  real pul çəkmir — mağazada yalnız Stripe ilə satmaq olar.

### Mağazasız test (Android)

Tətbiqi dostlara və ya öz telefonunuza mağazasız qoymaq üçün:

1. GitHub → Settings → Secrets and variables → Actions → **Variables** → `WY_API_BASE` = serverin ünvanı
2. Actions → **android-test-apk** → **Run workflow**
3. Bitəndə "Artifacts" bölməsindən APK-nı endirin, telefona göndərin, açıb quraşdırın
   (telefon "naməlum mənbə" icazəsi istəyəcək)

## 3. Hesablar (bunları yalnız siz aça bilərsiniz)

1. **Apple Developer Program** — ildə $99: <https://developer.apple.com/programs/>
2. **Google Play Console** — birdəfəlik $25: <https://play.google.com/console>
3. **Codemagic** — pulsuz plan kifayətdir: <https://codemagic.io> (GitHub ilə daxil olun)
4. Layihəni **GitHub**-a yükləyin (şəxsi repo olar) və Codemagic-ə qoşun.

## 4. Mağazalarda tətbiq yaratmaq

- **App Store Connect** → Apps → "+" → Bundle ID: `az.watchingyou.cinema`. Tətbiqin **Apple ID**
  nömrəsini (App Information səhifəsində) qeyd edin.
- **Google Play Console** → Create app → paket adı `az.watchingyou.cinema`.

## 5. Codemagic ayarları (bir dəfə)

Codemagic → Team settings:

| Nə | Harada | Dəyər |
|---|---|---|
| Qrup `app_config` | Environment variables | `WY_API_BASE` = serverin ünvanı, `APP_STORE_APPLE_ID` = tətbiqin Apple ID-si |
| Android açarı | Code signing → Android keystores | Yeni açar yaradın (Codemagic özü yarada bilər), adı: `watchingyou_upload_key` |
| Qrup `google_play` | Environment variables | `GCLOUD_SERVICE_ACCOUNT_CREDENTIALS` = Play Console service account JSON |
| App Store açarı | Integrations → App Store Connect | API açarı, adı: `WatchingYou ASC key` |

> 🔐 Android açarının (keystore) ehtiyat nüsxəsini saxlayın. İtirsəniz, tətbiqi yeniləyə bilməzsiniz.

Hər addımın şəkilli izahı: <https://docs.codemagic.io/yaml-quick-start/building-a-native-ios-app/>
və <https://docs.codemagic.io/yaml-code-signing/signing-android/>.

## 6. Yığmaq və göndərmək

```bash
git tag app-v1.0.0
git push origin app-v1.0.0
```

`app-v` ilə başlayan hər teq hər iki yığımı başladır:

- **iOS** → TestFlight-a gedir. Oradan App Store-a təqdim edirsiniz.
- **Android** → Google Play-in "Internal testing" bölməsinə qaralama kimi gedir.

> Google Play **ilk** yükləməni əl ilə istəyir: ilk dəfə Codemagic-də yığılmış `.aab` faylını
> yükləyin və Play Console-da əl ilə göndərin. Sonrakılar avtomatik gedir.

## 7. Mağaza üçün hazırlamaq lazım olanlar

Hazır mətnlər (ad, təsvir, açar sözlər — 4 dildə, limitlərə uyğun), Apple yoxlayıcısı üçün qeyd və
hər iki məxfilik anketinin cavabları: **[STORE_LISTING.md](STORE_LISTING.md)**.

Sizə qalan:
- `appsettings.json` → `Legal`: real şirkət adı və izlənilən əlaqə e-poçtu
- Məxfilik siyasətinin URL-i: `https://<domeniniz>/privacy` — hər iki mağazaya yazın
- Ekran görüntüləri (telefon ölçüsündə): Filmlər, yer xəritəsi, ödəniş, Biletlərim/QR
- Yoxlayıcının test hesablarının serverdə olduğuna əmin olun

## Sonrakı addımlar (istəyə görə)

- Serverdən push bildirişləri (məs. "seans ləğv olundu") — Firebase + APNs açarı tələb edir.
  Seansdan əvvəlki xatırlatma artıq işləyir və buna ehtiyac duymur.
- Biletləri Apple Wallet / Google Wallet-ə əlavə etmək — Apple Pass Type ID sertifikatı tələb edir
