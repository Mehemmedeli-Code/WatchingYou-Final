namespace MovieRental.Host.Pages;

public static partial class InfoPages
{
    private static readonly InfoPage AboutTr = new("WatchingYou hakkında",
        "Bir sinema, bir film kütüphanesi ve kendi izleyicilerinin çektiği kısa filmler için bir ev — hepsi tek yerde.", [
        new("Biz kimiz", [
            "WatchingYou, koltuk ayırmak için bir web sitesi olan bir mahalle sineması olarak başladı. Zamanla birbirine bağlı üç şeye dönüştü: Bakü'deki sinema, klasik ve yeni filmlerden oluşan çevrimiçi bir kütüphane ve üyelerin kendi çektikleri kısa filmleri yayımladığı bir topluluk.",
            "Her şey tek hesapla çalışır. Aynı giriş cuma akşamı için koltuk ayırır, hafta sonu için film kiralar ve telefonla çektiğin kısa filmi yükler.",
        ]),
        new("Sinema", [
            "Her seansın canlı bir koltuk haritası var: başkaları seçtikçe hangi koltukların boş olduğunu görür, kendi koltuğunu seçer, öder ve QR kodlu bir bilet alırsın. Kod kapıda internetsiz açılır; lobideki zayıf sinyal asla engel olmaz.",
            "Program bir hafta ilerisini gösterir — gündüz ve akşam seansları. Seanstan 48 saat öncesine kadar bilet iptal edilebilir; seansı biz iptal edersek paranın tamamı iade edilir.",
        ]),
        new("Kütüphane", [
            "Katalogdaki her film üç günlüğüne 0,50 $'a kiralanabilir. Gecikme ücreti yok: üç gün dolduğunda sadece üç gün daha isteyip istemediğini sorarız.",
            "Watching PRO ayda 5 $'dır ve ay boyunca kütüphanedeki bütün filmleri açar.",
        ]),
        new("Üyelerin filmleri", [
            "Studio'da her üye kısa film yükleyebilir ve nasıl yapıldığını belirtir: el emeği mi, yapay zekâ ile mi. Her yüklemeyi başkaları görmeden önce Güvenlik masası baştan sona izler, bir yönetici de üç gün içinde onaylar.",
            "Onaylanan filmler El Emeği ve Yapay Zekâ galerilerinde ve yazarın profilinde görünür. Gizli olanlar yazarın kendi bölümünde kalır.",
        ]),
        new("İnsanlar", [
            "Profiller Instagram'dan bildiğin gibi: fotoğraf, @kullanıcı adı, biyografi, takipçiler ve bir ızgara — burada filmlerden oluşan bir ızgara. Hesaplar açık veya gizli olabilir; gizli hesap her takipçiyi kendisi onaylar.",
            "Kürede görünmeyi seçen üyeler şehre göre bulunabilir ve onlara doğrudan yazılabilir. Herkes bir mesajı engelleyebilir veya şikâyet edebilir; Güvenlik masası her şikâyeti okur.",
        ]),
        new("Neye inanıyoruz", [
            "Reklam yok, veri satışı yok, başka sitelerde takip yok. Kart numaraları asla saklanmaz. Az izleyicisi olan bir film de gişe filmi kadar özeni hak eder; izleyici de hangi filmi bir insanın, hangisini bir makinenin yaptığını bilmeyi hak eder.",
        ]),
    ]);

    private static readonly InfoPage BlogTr = new("Blog",
        "Sinemadan, kütüphaneden ve stüdyodan haberler.", [
        new("Profillerde film ızgarası", [
            "Profilin artık bildiğin profiller gibi: fotoğraf, @kullanıcı adı, biyografi, takipçi sayıları ve altlarında bir ızgara — fotoğraflardan değil, senin yaptığın filmlerden.",
            "Paylaşılan filmleri herkes görür (gizli hesapta yalnızca kabul ettiğin takipçiler). İkinci sekme, Gizli, yalnızca senin içindir. Sağdaki seçim ızgarayı filmin nasıl yapıldığına göre süzer: el emeği veya yapay zekâ.",
        ], Meta: "Ekim 2026"),
        new("Gizli hesaplar ve takip istekleri", [
            "Profil artık gizli olabilir. Onu takip etmek bir istek gönderir; sahibi bunu fotoğrafındaki kırmızı sayaçla görür ve Kabul et ya da Reddet ile cevaplar. Kabul edince hemen orada Geri takip et düğmesi çıkar.",
            "Hesabı yeniden açık yapmak bekleyen bütün istekleri kabul eder; kimse cevapsız kalmaz.",
        ], Meta: "Ekim 2026"),
        new("Bir kısa film nasıl yayımlanır", [
            "Her yükleme iki kişiden geçer. Önce Güvenlik masası filmi baştan sona izler ve bir kontrol listesi doldurur — içerik, haklar, ses, beyan edilen köken. Sonra bir yönetici filmi yazara bir notla onaylar veya reddeder.",
            "Bütün inceleme üç gün içinde biter. O zamana kadar filmi yalnızca yazarı görür; Studio'da incelemeyi takip edip inceleyenlerle konuşabilir.",
        ], Meta: "Eylül 2026"),
        new("İadeler, sade bir dille", [
            "Bilet seanstan 48 saat öncesine kadar iptal edilebilir; senin için ayırdığımız koltuk karşılığında fiyatın %30'u kesilir. Sonrasında koltuk kesin olarak senindir. Seansı biz iptal edersek tutarın tamamı otomatik iade edilir.",
            "Her seansın sayfası kendi kurallarını gösterir; özel etkinliklerde kurallar bazen farklıdır.",
        ], Meta: "Eylül 2026"),
    ]);

    private static readonly InfoPage CareersTr = new("Kariyer",
        "Aynı anda bir sinema, bir kütüphane ve bir topluluk kuran küçük bir ekip.", [
        new("Nasıl çalışıyoruz", [
            "Küçüğüz, bu yüzden herkes bir işin baştan sona sahibidir: koltuk haritasını yapan kişi gala gecesi lobide durup insanların onu nasıl kullandığını da izler.",
            "Her şeyi yazıya döker, birbirimizin işini inceler ve belki çalışacak zekice çözüm yerine çalışan basit çözümü seçeriz.",
        ]),
        new("Kimi arıyoruz", [
            "Doğruluğa önem veren mühendisler (.NET, React, SQL Server): çift rezervasyon, ödemeler ve gizlilik kestirme yol aranacak yerler değildir.",
            "Gişe, bar ve giriş tarayıcısında rahat çalışan lobi ve gösterim personeli.",
            "Güvenlik masası için inceleyiciler: bir filmi dikkatle izleyip yazılı kurallara göre adil değerlendirebilen insanlar.",
            "Filmleri bilen, programı ve kütüphaneyi kurmak isteyen programcılar ve küratörler.",
        ]),
        new("Ne sunuyoruz", [
            "Sana ve bir misafirine ücretsiz seanslar ve Watching PRO hesabı, seans programına uygun esnek çalışma saatleri ve ilk haftadan gerçek sorumluluk.",
        ]),
        new("Nasıl başvurulur", [
            "Kendin hakkında birkaç satır ve yaptığın bir şeyle — kod, film, gösterim programı — bize yaz. Her başvuruya cevap veriyoruz.",
        ]),
    ]);

    private static readonly InfoPage DevelopersTr = new("Geliştiriciler için API",
        "Web sitesinin ve telefon uygulamasının yaptığı her şey aynı HTTP API'den geçer. Nasıl kullanılacağı burada.", [
        new("Temeller", [
            "API, HTTPS üzerinden JSON konuşur. Zamanlar UTC, ISO 8601 biçimindedir. Para, para birimiyle birlikte ondalık bir sayıdır. Listeler sayfa sayfa gelir.",
            "Web sitesi, iOS ve Android uygulamaları ve arka ofis tam olarak burada anlatılan uç noktaları kullanır — arkalarında gizli, daha iyi bir API yoktur.",
        ]),
        new("Açık uç noktalar", ["Bunlar hesap gerektirmez:"], PublicEndpoints),
        new("Giriş", [
            "E-posta ve şifreyle giriş yap; kısa ömürlü bir access token (JWT) ve bir refresh token alırsın. Access token'ı Bearer başlığıyla gönder. Süresi dolunca refresh token'ı yeni bir çiftle değiştir; her refresh token bir kez çalışır, iki kez kullanılırsa hesap her yerden çıkarılır, çünkü bu yalnızca token çalındığında olur.",
        ], AuthExample),
        new("Üye uç noktaları", ["Bunlar Bearer token gerektirir:"], MemberEndpoints),
        new("Hatalar", [
            "Hatalar standart Problem Details biçimindedir. Doğrulama hataları her alanı mesajlarıyla listeler; 401 — yeniden giriş yap, 403 — hesabın buna yetkisi yok, 404 — böyle bir şey yok, 409 — var olan bir şeyle çakışıyor (dolu koltuk veya kullanıcı adı).",
        ], ErrorExample),
        new("Sınırlar", [
            "Giriş ve kayıt bir adresten dakikada 8 denemeye, doğrulama kodları 5 dakikada 12 denemeye izin verir. Sınır aşılınca 429 Too Many Requests döner — bekle ve tekrar dene.",
            "Yüklemeler: profil fotoğrafları 2 MB'a kadar (JPEG, PNG, WebP); kısa filmler saklanmadan önce tür ve boyuta göre kontrol edilir.",
        ]),
        new("Gerçek zamanlı", [
            "Mesajlar, yazıyor göstergeleri, koltuk değişiklikleri ve bildirimler /hubs/chat adresindeki SignalR hub'ı üzerinden gelir. Aynı access token ile bağlan (WebSocket başlık taşıyamadığı için access_token sorgu parametresi olarak).",
        ]),
        new("Tam başvuru", [
            "Her istek ve yanıt türüyle tam OpenAPI açıklaması koddan üretilir ve yöneticiler için /swagger adresindedir. WatchingYou üzerinde bir şey geliştiriyorsan bize yaz, erişim verelim.",
        ]),
    ]);

    private static readonly InfoPage TermsTr = new("Kullanım koşulları",
        "WatchingYou'yu kullanma kuralları. Kısa, çünkü okunmaları gerekiyor.", [
        new("Hesabın", [
            "Koltuk ayırmak, film kiralamak, yüklemek veya mesaj yazmak için hesap gerekir. Gerçek e-posta adresini ver — kodlar ve biletler oraya gönderilir — ve şifreni kimseyle paylaşma. Hesabınla yapılanlardan sen sorumlusun.",
            "Hesabını istediğin zaman Hesap sayfasından silebilirsin.",
        ]),
        new("Biletler", [
            "Bilet üzerinde yazan seans, salon ve koltuklar için bir kez geçerlidir. QR kod kapıda kontrol edilir; kullanılmış bir kod tekrar çalışmaz.",
            "Seanstan 48 saat öncesine kadar iptal edebilirsin; fiyatın %30'u kesilir. 48 saatten az kala veya seans başladıktan sonra iade yapılmaz. Seansı biz iptal edersek tam ücret iade edilir. Bir seansın kendi sayfası farklı kurallar belirleyebilir; o seans için onlar geçerlidir.",
        ]),
        new("Kiralama ve Watching PRO", [
            "Kiralama bir filmi üç gün boyunca 0,50 $'a izlemeni sağlar; aynı fiyata üç gün daha uzatabilirsin. Gecikme ücreti yoktur.",
            "Watching PRO ayda 5 $'dır ve ödenen ay boyunca bütün kütüphaneye erişim verir. Filmler yalnızca kişisel izleme içindir: kopyalamak, kaydetmek veya herkese açık göstermek yasaktır.",
        ]),
        new("Yüklediklerin", [
            "Filmlerinin hakları sende kalır. Birini yayımlayarak, onu gizli yapana veya silene kadar WatchingYou'da — galerilerde ve profilinde — göstermemize izin verirsin.",
            "Yalnızca kendi yaptığını veya paylaşma hakkın olanı yükle ve el emeği mi yapay zekâ ile mi yapıldığını dürüstçe belirt. Her yükleme incelenir; bu kuralları veya yasayı çiğneyen her şeyi reddedebilir veya kaldırabiliriz.",
        ]),
        new("Davranış", [
            "Diğer üyeleri taciz etme, tehdit etme veya kandırma; spam gönderme; sana ait olmayan hesaplara veya hizmetin bölümlerine girmeye çalışma. Herkes engelleyebilir ve şikâyet edebilir; Güvenlik masası her şikâyeti inceler ve hesapları askıya alabilir.",
        ]),
        new("Ödemeler", [
            "Fiyatlar ödemeden önce gösterilir. Kart ödemelerini Stripe işler; kartın tam numarasını asla görmez ve saklamayız.",
        ]),
        new("Değişiklikler ve sorumluluk", [
            "Hizmeti ve bu koşulları değiştirebiliriz; güncel sürüm her zaman bu sayfadadır. Hizmetin çalışması için elimizden geleni yaparız, ancak hiç kesilmeyeceğine söz veremeyiz. Bu koşullardaki hiçbir şey yasal haklarını sınırlamaz.",
        ]),
    ]);
}
