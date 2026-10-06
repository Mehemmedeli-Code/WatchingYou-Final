using MovieRental.Host.Infrastructure.Localization;

namespace MovieRental.Host.Pages;

/// <summary>
/// The privacy policy. Both app stores require a public URL for one, and it has to describe
/// what the system really does — so it is written from the code: what is stored, where it
/// goes, and what is deliberately not kept (the card number, for one).
///
/// Served by Razor rather than React, so a store reviewer's crawler reads it without running
/// any script. The operator's name and contact address come from configuration (Legal:*),
/// because only the business running the cinema can say what they are.
/// </summary>
public sealed class PrivacyModel(IPageShellFactory shell, ILanguageContext language, IConfiguration configuration) : AppPageModel
{
    public PrivacyText Text { get; private set; } = PrivacyText.For("en");
    public string Company { get; private set; } = "WatchingYou";
    public string ContactEmail { get; private set; } = "";
    public string Updated => "2026-09-27";

    public void OnGet()
    {
        View = shell.Create("privacy.title", "footer.note", "privacy", "privacy");
        Text = PrivacyText.For(language.Code);
        Company = configuration["Legal:CompanyName"] ?? "WatchingYou";
        ContactEmail = configuration["Legal:ContactEmail"] ?? "";
    }
}

public sealed record PrivacySection(string Heading, IReadOnlyList<string> Paragraphs);

public sealed record PrivacyText(string Title, string UpdatedLabel, string ContactLabel, IReadOnlyList<PrivacySection> Sections)
{
    public static PrivacyText For(string language) => language switch
    {
        "az" => Az,
        "ru" => Ru,
        "tr" => Tr,
        _ => En,
    };

    private static readonly PrivacyText En = new("Privacy policy", "Last updated", "Contact", [
        new("Who we are", ["This policy covers the WatchingYou website and the WatchingYou app for iOS and Android. The operator named below is responsible for your data."]),
        new("What we collect", [
            "Account: your name, e-mail address, optional phone number, and your password — stored only as a one-way hash, never in readable form.",
            "Bookings: the screenings, seats and prices of tickets you buy, your loyalty points and any promo code used.",
            "Payments: for a card payment we keep only the card brand and its last four digits for your receipt. The full card number and security code are never stored. Payments through Stripe are processed by Stripe; we receive only whether the payment succeeded.",
            "What you choose to share: messages you send to other members or to support, short films you upload, and — only if you turn it on — your city on the members' globe.",
            "Technical data: the IP address of sign-ins and requests, kept for security (for example to limit password guessing).",
        ]),
        new("What we do not do", ["We do not sell your data, show advertising, or use tracking or advertising SDKs. The app does not track you across other companies' apps or websites."]),
        new("Why we use it", ["To run your account, sell and deliver tickets, send the codes and tickets you ask for by e-mail or SMS, keep the service secure, and keep the accounting records the law requires."]),
        new("Who receives it", ["Stripe (card payments), our e-mail and SMS delivery providers (to send your codes and tickets), and our hosting provider. Each receives only what its job needs."]),
        new("On your device", ["The app and the website keep your upcoming tickets on the device so the QR codes open without internet, plus your language and theme. Signing out removes the saved tickets."]),
        new("How long we keep it", ["Your account data is kept until you delete the account. Booking and payment records are kept for as long as accounting law requires, but after deletion they no longer identify you."]),
        new("Your rights", [
            "You can see and correct your details on the Account page, and delete your account there at any time: your name, e-mail, phone and profile are erased and you are signed out everywhere.",
            "For anything else — a copy of your data, or a question about it — write to the contact address below.",
        ]),
        new("Children", ["The service is not directed at children under 13, and we do not knowingly collect their data."]),
        new("Changes", ["If this policy changes, the new version is published here with a new date."]),
    ]);

    private static readonly PrivacyText Az = new("Məxfilik siyasəti", "Son yenilənmə", "Əlaqə", [
        new("Biz kimik", ["Bu siyasət WatchingYou saytına və iOS və Android üçün WatchingYou tətbiqinə aiddir. Məlumatlarınıza görə aşağıda adı çəkilən operator cavabdehdir."]),
        new("Nə toplayırıq", [
            "Hesab: adınız, e-poçt ünvanınız, istəyə bağlı telefon nömrəniz və şifrəniz — şifrə yalnız geri çevrilməyən hash kimi saxlanılır, heç vaxt oxunaqlı formada deyil.",
            "Sifarişlər: aldığınız biletlərin seansları, yerləri və qiymətləri, loyallıq xallarınız və istifadə olunan promo kod.",
            "Ödənişlər: kartla ödənişdə çek üçün yalnız kartın növü və son dörd rəqəmi saxlanılır. Kartın tam nömrəsi və təhlükəsizlik kodu heç vaxt saxlanılmır. Stripe ilə ödənişləri Stripe emal edir; biz yalnız ödənişin uğurlu olub-olmadığını alırıq.",
            "Özünüz paylaşdıqlarınız: digər üzvlərə və ya dəstəyə göndərdiyiniz mesajlar, yüklədiyiniz qısametrajlı filmlər və — yalnız siz aktivləşdirsəniz — üzvlərin qlobusunda şəhəriniz.",
            "Texniki məlumat: girişlərin və sorğuların IP ünvanı — təhlükəsizlik üçün saxlanılır (məsələn, şifrə təxminlərini məhdudlaşdırmaq üçün).",
        ]),
        new("Nə etmirik", ["Məlumatlarınızı satmırıq, reklam göstərmirik, izləmə və ya reklam SDK-larından istifadə etmirik. Tətbiq sizi digər şirkətlərin tətbiqləri və saytları üzrə izləmir."]),
        new("Nə üçün istifadə edirik", ["Hesabınızı idarə etmək, bilet satmaq və çatdırmaq, istədiyiniz kodları və biletləri e-poçt və ya SMS ilə göndərmək, xidməti təhlükəsiz saxlamaq və qanunun tələb etdiyi mühasibat qeydlərini aparmaq üçün."]),
        new("Kimə ötürülür", ["Stripe (kart ödənişləri), e-poçt və SMS çatdırma xidmətlərimiz (kod və biletlərinizi göndərmək üçün) və hostinq provayderimiz. Hər biri yalnız öz işi üçün lazım olanı alır."]),
        new("Cihazınızda", ["Tətbiq və sayt QR kodların internetsiz açılması üçün qarşıdakı biletlərinizi, həmçinin dil və tema seçiminizi cihazda saxlayır. Hesabdan çıxanda saxlanılmış biletlər silinir."]),
        new("Nə qədər saxlayırıq", ["Hesab məlumatlarınız hesabı silənədək saxlanılır. Sifariş və ödəniş qeydləri mühasibat qanununun tələb etdiyi müddətdə saxlanılır, lakin hesab silindikdən sonra sizi müəyyənləşdirmir."]),
        new("Hüquqlarınız", [
            "Məlumatlarınızı Hesab səhifəsində görə və düzəldə, hesabınızı istənilən vaxt orada silə bilərsiniz: adınız, e-poçtunuz, telefonunuz və profiliniz silinir və bütün cihazlardan çıxış edilir.",
            "Başqa istənilən məsələ üçün — məlumatlarınızın surəti və ya sualınız — aşağıdakı ünvana yazın.",
        ]),
        new("Uşaqlar", ["Xidmət 13 yaşdan kiçik uşaqlar üçün nəzərdə tutulmayıb və biz bilərəkdən onların məlumatlarını toplamırıq."]),
        new("Dəyişikliklər", ["Siyasət dəyişərsə, yeni versiya yeni tarixlə burada dərc olunur."]),
    ]);

    private static readonly PrivacyText Ru = new("Политика конфиденциальности", "Обновлено", "Контакты", [
        new("Кто мы", ["Эта политика относится к сайту WatchingYou и приложению WatchingYou для iOS и Android. За ваши данные отвечает указанный ниже оператор."]),
        new("Что мы собираем", [
            "Аккаунт: имя, адрес электронной почты, по желанию номер телефона и пароль — только в виде необратимого хеша, никогда в читаемом виде.",
            "Бронирования: сеансы, места и цены купленных билетов, ваши бонусные баллы и использованный промокод.",
            "Платежи: при оплате картой для чека хранятся только тип карты и последние четыре цифры. Полный номер карты и код безопасности не хранятся никогда. Платежи через Stripe обрабатывает Stripe; мы получаем только результат оплаты.",
            "То, чем вы делитесь сами: сообщения другим участникам или в поддержку, загруженные короткометражки и — только если вы это включите — ваш город на глобусе участников.",
            "Технические данные: IP-адрес входов и запросов, хранится для безопасности (например, чтобы ограничить подбор пароля).",
        ]),
        new("Чего мы не делаем", ["Мы не продаём ваши данные, не показываем рекламу и не используем SDK для отслеживания или рекламы. Приложение не отслеживает вас в чужих приложениях и на чужих сайтах."]),
        new("Зачем мы их используем", ["Чтобы вести ваш аккаунт, продавать и выдавать билеты, отправлять запрошенные коды и билеты по e-mail или SMS, обеспечивать безопасность сервиса и вести учёт, которого требует закон."]),
        new("Кому передаются", ["Stripe (оплата картой), наши сервисы доставки e-mail и SMS (для отправки кодов и билетов) и хостинг-провайдер. Каждый получает только то, что нужно для его задачи."]),
        new("На вашем устройстве", ["Приложение и сайт хранят на устройстве ваши ближайшие билеты, чтобы QR-коды открывались без интернета, а также язык и тему. При выходе сохранённые билеты удаляются."]),
        new("Как долго мы храним", ["Данные аккаунта хранятся до его удаления. Записи о бронированиях и платежах хранятся столько, сколько требует бухгалтерское законодательство, но после удаления аккаунта они вас не идентифицируют."]),
        new("Ваши права", [
            "На странице «Аккаунт» можно просмотреть и исправить данные, а также в любой момент удалить аккаунт: имя, e-mail, телефон и профиль стираются, выполняется выход на всех устройствах.",
            "По любым другим вопросам — копия ваших данных или вопрос о них — пишите на адрес ниже.",
        ]),
        new("Дети", ["Сервис не предназначен для детей младше 13 лет, и мы сознательно не собираем их данные."]),
        new("Изменения", ["При изменении политики новая версия публикуется здесь с новой датой."]),
    ]);

    private static readonly PrivacyText Tr = new("Gizlilik politikası", "Son güncelleme", "İletişim", [
        new("Biz kimiz", ["Bu politika WatchingYou web sitesini ve iOS ile Android için WatchingYou uygulamasını kapsar. Verilerinizden aşağıda adı geçen işletmeci sorumludur."]),
        new("Neleri topluyoruz", [
            "Hesap: adınız, e-posta adresiniz, isteğe bağlı telefon numaranız ve şifreniz — şifre yalnızca geri döndürülemez bir özet (hash) olarak saklanır, asla okunabilir biçimde değil.",
            "Rezervasyonlar: aldığınız biletlerin seansları, koltukları ve fiyatları, sadakat puanlarınız ve kullanılan promosyon kodu.",
            "Ödemeler: kartla ödemede fiş için yalnızca kart türü ve son dört hanesi saklanır. Kartın tam numarası ve güvenlik kodu asla saklanmaz. Stripe ile yapılan ödemeleri Stripe işler; biz yalnızca ödemenin başarılı olup olmadığını alırız.",
            "Kendi paylaştıklarınız: diğer üyelere veya desteğe gönderdiğiniz mesajlar, yüklediğiniz kısa filmler ve — yalnızca siz açarsanız — üye küresindeki şehriniz.",
            "Teknik veriler: girişlerin ve isteklerin IP adresi, güvenlik için saklanır (örneğin şifre tahminlerini sınırlamak için).",
        ]),
        new("Yapmadıklarımız", ["Verilerinizi satmıyor, reklam göstermiyor, izleme veya reklam SDK'ları kullanmıyoruz. Uygulama sizi diğer şirketlerin uygulamaları ve siteleri üzerinde izlemez."]),
        new("Neden kullanıyoruz", ["Hesabınızı yürütmek, bilet satmak ve teslim etmek, istediğiniz kodları ve biletleri e-posta veya SMS ile göndermek, hizmeti güvende tutmak ve yasanın gerektirdiği muhasebe kayıtlarını tutmak için."]),
        new("Kimlerle paylaşılır", ["Stripe (kart ödemeleri), e-posta ve SMS teslim hizmetlerimiz (kodlarınızı ve biletlerinizi göndermek için) ve barındırma sağlayıcımız. Her biri yalnızca işi için gerekeni alır."]),
        new("Cihazınızda", ["Uygulama ve site, QR kodların internetsiz açılması için yaklaşan biletlerinizi, ayrıca dil ve tema tercihinizi cihazda saklar. Çıkış yaptığınızda kayıtlı biletler silinir."]),
        new("Ne kadar saklıyoruz", ["Hesap verileriniz hesabı silene kadar saklanır. Rezervasyon ve ödeme kayıtları muhasebe mevzuatının gerektirdiği süre boyunca saklanır, ancak hesap silindikten sonra sizi tanımlamaz."]),
        new("Haklarınız", [
            "Bilgilerinizi Hesap sayfasında görebilir ve düzeltebilir, hesabınızı istediğiniz zaman orada silebilirsiniz: adınız, e-postanız, telefonunuz ve profiliniz silinir ve tüm cihazlardan çıkış yapılır.",
            "Diğer her konu için — verilerinizin bir kopyası veya bir soru — aşağıdaki adrese yazın.",
        ]),
        new("Çocuklar", ["Hizmet 13 yaşından küçük çocuklara yönelik değildir ve bilerek onların verilerini toplamayız."]),
        new("Değişiklikler", ["Politika değişirse yeni sürüm yeni tarihle burada yayımlanır."]),
    ]);
}
