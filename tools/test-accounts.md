# Test hesabları

Yalnız development üçün. Bu hesablar bazaya seed ilə yazılıb, heç birinə e-poçt göndərilmir.

## Əsas hesablar

| E-poçt | Ad | İstifadəçi adı | Rol | Şifrə |
|---|---|---|---|---|
| admin@reelandrow.test | Rena Alverdiyeva | @user_fbddc7e366 | Admin,Customer | Admin1234 |
| customer@reelandrow.test | Mahammadali Babayev | @babayev | Customer | Customer1234 |
| kassa@reelandrow.test | Leyla Karimova | @user_067f890126 | Cashier | Kassa1234 |
| mkbabayev428@gmail.com | M | @user_45ec039afe | Customer | (öz şifrən) |
| security@reelandrow.test | Kamran Hasanli | @user_47f9e3fdac | Security,Customer | Security1234 |

Seed şifrələri src/MovieRental.Host/Infrastructure/DevelopmentDatabaseBootstrapper.cs-dədir. Sonradan saytda dəyişilibsə, yeni şifrə keçərlidir.

## Demo istifadəçilər

Hamısının şifrəsi: `Demo12345` (DemoPeople.Password). Bir-birini izləyirlər, aralarında mesajlar və filmlər var.

| E-poçt | Ad | İstifadəçi adı | Hesab |
|---|---|---|---|
| aigerim.bekova.wydemo@gmail.com | Aigerim Bekova | @aigerim.bekova | açıq |
| aliya.nurlanovna.wydemo@gmail.com | Aliya Nurlanovna | @aliya.nurlanovna | açıq |
| anna.smirnova.wydemo@gmail.com | Анна Смирнова | @anna.smirnova | açıq |
| aysel.quliyeva.wydemo@gmail.com | Aysel Quliyeva | @aysel.quliyeva | açıq |
| daniyar.sadykov.wydemo@gmail.com | Daniyar Sadykov | @daniyar.sadykov | açıq |
| elif.demir.wydemo@gmail.com | Elif Demir | @elif.demir | gizli |
| elvin.mammadli.wydemo@gmail.com | Elvin Məmmədli | @elvin.mammadli | açıq |
| emre.kaya.wydemo@gmail.com | Emre Kaya | @emre.kaya | açıq |
| giorgi.kapanadze.wydemo@gmail.com | Giorgi Kapanadze | @giorgi.kapanadze | açıq |
| ivan.petrov.wydemo@gmail.com | Иван Петров | @ivan.petrov | gizli |
| james.carter.wydemo@gmail.com | James Carter | @james.carter | açıq |
| kamal.huseynov.wydemo@gmail.com | Kamal Hüseynov | @kamal.huseynov | açıq |
| lena.fischer.wydemo@gmail.com | Lena Fischer | @lena.fischer | açıq |
| leyla.rzayeva.wydemo@gmail.com | Leyla Rzayeva | @leyla.rzayeva | açıq |
| mike.johnson.wydemo@gmail.com | Mike Johnson | @mike.johnson | açıq |
| murad.aliyev.wydemo@gmail.com | Murad Əliyev | @murad.aliyev | açıq |
| nigar.hasanova.wydemo@gmail.com | Nigar Həsənova | @nigar.hasanova | gizli |
| nino.beridze.wydemo@gmail.com | Nino Beridze | @nino.beridze | açıq |
| olena.kovalenko.wydemo@gmail.com | Olena Kovalenko | @olena.kovalenko | açıq |
| rashad.novruzov.wydemo@gmail.com | Rəşad Novruzov | @rashad.novruzov | açıq |
| sabina.ismayilova.wydemo@gmail.com | Sabina İsmayılova | @sabina.ismayilova | gizli |
| sophia.martin.wydemo@gmail.com | Sophia Martin | @sophia.martin | açıq |
| tural.qasimov.wydemo@gmail.com | Tural Qasımov | @tural.qasimov | açıq |
| zeynep.yilmaz.wydemo@gmail.com | Zeynep Yılmaz | @zeynep.yilmaz | açıq |

## Telefondan yoxlamaq (eyni Wi-Fi)

1. Bir dəfə, **administrator** kimi açılmış PowerShell-də firewall-da portları aç:

   ```powershell
   New-NetFirewallRule -DisplayName "WatchingYou dev" -Direction Inbound -Protocol TCP -LocalPort 7139,5139,5174 -Action Allow -Profile Private
   ```

2. Telefonda əvvəlcə **https://<kompüterin IP-si>:7139** aç. Sertifikat xəbərdarlığı çıxacaq (dev sertifikatı yalnız localhost üçündür): "Advanced → Proceed" seç. Sayt telefonda açılır.
3. Telefon versiyası: **http://<kompüterin IP-si>:5174/mobile.html**. API-ni 2-ci addımda qəbul etdiyin ünvandan çağırır, ona görə 2-ci addım əvvəl olmalıdır.

Kompüterin IP-si: `ipconfig` → "IPv4 Address" (indi 192.168.1.66).

Firewall qaydasını silmək: `Remove-NetFirewallRule -DisplayName "WatchingYou dev"`