namespace MovieRental.Host.Pages;

public static partial class InfoPages
{
    private static readonly InfoPage AboutAz = new("WatchingYou haqqında",
        "Kinoteatr, film kitabxanası və öz tamaşaçılarının çəkdiyi qısametrajlı filmlər üçün ev — hamısı bir yerdə.", [
        new("Biz kimik", [
            "WatchingYou yer bronlamaq üçün saytı olan məhəllə kinoteatrı kimi başladı. Sonra bir-birinə bağlı üç şeyə çevrildi: Bakıdakı kinoteatr, klassik və yeni filmlərdən ibarət onlayn kitabxana və üzvlərin öz çəkdikləri qısa filmləri paylaşdığı icma.",
            "Hər şey bir hesabla işləyir. Eyni giriş cümə axşamına yer tutur, həftəsonu üçün film icarəyə götürür və telefonla çəkdiyin qısa filmi yükləyir.",
        ]),
        new("Kinoteatr", [
            "Hər seansın canlı yer xəritəsi var: başqaları yer seçdikcə hansı yerlərin boş olduğunu görürsən, öz yerini seçir, ödəyir və QR kodlu bilet alırsan. Kod girişdə internetsiz açılır, foyedə zəif siqnal heç vaxt mane olmur.",
            "Cədvəl bir həftə irəlini göstərir — gündüz və axşam seansları. Seansdan 48 saat əvvələdək bileti ləğv etmək olar; seansı biz ləğv etsək, pulun hamısı qaytarılır.",
        ]),
        new("Kitabxana", [
            "Kataloqdakı istənilən film üç günlük 0,50 $-a icarəyə götürülür. Gecikmə cəriməsi yoxdur: üç gün bitəndə sadəcə daha üç gün istəyib-istəmədiyini soruşuruq.",
            "Watching PRO ayda 5 $-dır və ay boyu kitabxanadakı bütün filmləri açır.",
        ]),
        new("Üzvlərin filmləri", [
            "Studio-da hər üzv qısa film yükləyə və onun necə düzəldildiyini qeyd edə bilər: əl işi, yoxsa AI ilə. Hər yükləməyə başqaları baxmazdan əvvəl Təhlükəsizlik masası tam baxır, administrator isə üç gün ərzində təsdiqləyir.",
            "Təsdiqlənmiş filmlər Əl işi və AI qalereyalarında və müəllifin profilində görünür. Gizli olanlar müəllifin öz bölməsində qalır.",
        ]),
        new("İnsanlar", [
            "Profillər Instagram-dan tanıdığın kimidir: şəkil, @istifadəçi adı, bio, izləyicilər və şəbəkə — burada filmlərdən ibarət şəbəkə. Hesab açıq və ya gizli ola bilər; gizli hesab hər izləyicini özü təsdiqləyir.",
            "Qlobusda görünməyi seçən üzvləri şəhərə görə tapmaq və birbaşa yazmaq olar. Hər kəs mesajı bloklaya və ya şikayət edə bilər, Təhlükəsizlik masası hər şikayəti oxuyur.",
        ]),
        new("Nəyə inanırıq", [
            "Reklam yoxdur, məlumat satışı yoxdur, başqa saytlarda izləmə yoxdur. Kart nömrələri heç vaxt saxlanılmır. Az tamaşaçısı olan film də blokbaster qədər diqqətə layiqdir, tamaşaçı isə hansı filmi insanın, hansını maşının düzəltdiyini bilməlidir.",
        ]),
    ]);

    private static readonly InfoPage BlogAz = new("Bloq",
        "Kinoteatrdan, kitabxanadan və studiyadan xəbərlər.", [
        new("Profillərdə filmlərdən ibarət şəbəkə", [
            "Profilin indi tanıdığın profillər kimidir: şəkil, @istifadəçi adı, bio, izləyici sayları, altında isə şəbəkə — fotolardan yox, sənin çəkdiyin filmlərdən.",
            "Paylaşılan filmləri hamı görür (gizli hesabda isə yalnız qəbul etdiyin izləyicilər). İkinci tab — Gizli — yalnız sənin üçündür. Sağdakı seçim şəbəkəni filmin necə düzəldildiyinə görə süzür: əl işi və ya AI ilə.",
        ], Meta: "Oktyabr 2026"),
        new("Gizli hesablar və izləmə istəkləri", [
            "Profil artıq gizli ola bilər. Onu izləmək istək göndərir; sahibi istəyi şəklinin üstündəki qırmızı sayğacla görür və Qəbul et və ya İmtina et ilə cavab verir. Qəbul edəndən sonra elə orada Geri izlə düyməsi çıxır.",
            "Hesabı yenidən açıq etmək gözləyən bütün istəkləri qəbul edir, heç kim cavabsız qalmır.",
        ], Meta: "Oktyabr 2026"),
        new("Qısa film necə dərc olunur", [
            "Hər yükləmə iki nəfərdən keçir. Əvvəl Təhlükəsizlik masası filmə tam baxır və yoxlama siyahısını doldurur — məzmun, hüquqlar, səs, bildirilən mənşə. Sonra administrator onu müəllifə qeydlə təsdiqləyir və ya rədd edir.",
            "Bütün yoxlama üç gün ərzində bitir. O vaxta qədər filmi yalnız müəllifi görür; o, Studio-da yoxlamanı izləyə və yoxlayanlarla danışa bilər.",
        ], Meta: "Sentyabr 2026"),
        new("Geri qaytarma, sadə dillə", [
            "Bileti seansdan 48 saat əvvələdək ləğv etmək olar; sənin üçün saxladığımız yerə görə qiymətin 30%-i tutulur. Bundan sonra yer artıq sənindir. Seansı biz ləğv etsək, məbləğin hamısı avtomatik qaytarılır.",
            "Hər seansın səhifəsi öz qaydalarını göstərir, çünki xüsusi tədbirlərdə bəzən fərqli qaydalar olur.",
        ], Meta: "Sentyabr 2026"),
    ]);

    private static readonly InfoPage CareersAz = new("Karyera",
        "Eyni anda kinoteatr, kitabxana və icma quran kiçik komanda.", [
        new("Necə işləyirik", [
            "Kiçik komandayıq, ona görə hər kəs bir işə başdan sona sahibdir: yer xəritəsini quran adam premyera gecəsi foyedə dayanıb insanların ondan necə istifadə etdiyinə də baxır.",
            "Hər şeyi yazırıq, bir-birimizin işini yoxlayırıq və ağıllı, amma bəlkə də işləyəcək həll əvəzinə sadə və işləyən həlli seçirik.",
        ]),
        new("Kimi axtarırıq", [
            "Düzgünlüyə önəm verən mühəndislər (.NET, React, SQL Server): ikiqat bron, ödənişlər və məxfilik qısa yol axtarılan yerlər deyil.",
            "Kinoteatr üçün foye və proyeksiya əməkdaşları — kassa, bar və giriş skanerində özünü rahat hiss edənlər.",
            "Təhlükəsizlik masası üçün yoxlayıcılar: filmə diqqətlə baxıb onu yazılı qaydalara görə ədalətlə qiymətləndirə bilən insanlar.",
            "Filmləri tanıyan, cədvəli və kitabxananı qurmaq istəyən proqramçılar və kuratorlar.",
        ]),
        new("Nə təklif edirik", [
            "Sənə və bir qonağına pulsuz seanslar və Watching PRO hesabı, seans cədvəlinə uyğun çevik iş saatları və ilk həftədən real məsuliyyət.",
        ]),
        new("Necə müraciət etmək olar", [
            "Özün haqqında bir neçə sətir və düzəltdiyin bir şeylə — kod, film, seans proqramı — bizə yaz. Hər müraciətə cavab veririk.",
        ]),
    ]);

    private static readonly InfoPage DevelopersAz = new("Developerlər üçün API",
        "Saytın və telefon tətbiqinin etdiyi hər şey eyni HTTP API-dən keçir. Ondan belə istifadə edilir.", [
        new("Əsaslar", [
            "API HTTPS üzərindən JSON danışır. Vaxtlar UTC, ISO 8601 formatındadır. Pul məbləği valyutası ilə birlikdə onluq ədəddir. Siyahılar səhifə-səhifə gəlir.",
            "Sayt, iOS və Android tətbiqləri və back office məhz burada təsvir olunan endpoint-lərdən istifadə edir — arxada gizli, daha yaxşı API yoxdur.",
        ]),
        new("Açıq endpoint-lər", ["Bunlar hesab tələb etmir:"], PublicEndpoints),
        new("Giriş", [
            "E-poçt və şifrə ilə daxil ol — qısaömürlü access token (JWT) və refresh token alırsan. Access token-i Bearer başlığı kimi göndər. Vaxtı bitəndə refresh token-i yeni cütə dəyiş; hər refresh token bir dəfə işləyir, iki dəfə istifadə olunsa hesab bütün cihazlardan çıxarılır, çünki bu yalnız token oğurlananda baş verir.",
        ], AuthExample),
        new("Üzv endpoint-ləri", ["Bunlar Bearer token tələb edir:"], MemberEndpoints),
        new("Xətalar", [
            "Xətalar standart Problem Details formatındadır. Doğrulama xətaları hər sahəni mesajları ilə sadalayır; 401 — yenidən daxil ol, 403 — hesabın buna icazəsi yoxdur, 404 — belə şey yoxdur, 409 — artıq mövcud olanla toqquşur (tutulmuş yer və ya istifadəçi adı).",
        ], ErrorExample),
        new("Limitlər", [
            "Giriş və qeydiyyat bir ünvandan dəqiqədə 8 cəhdə, doğrulama kodları 5 dəqiqədə 12 cəhdə icazə verir. Limiti keçəndə 429 Too Many Requests qayıdır — gözlə və yenidən cəhd et.",
            "Yükləmələr: profil şəkilləri 2 MB-a qədər (JPEG, PNG, WebP); qısa filmlər saxlanmazdan əvvəl tip və ölçüyə görə yoxlanılır.",
        ]),
        new("Real vaxt", [
            "Mesajlar, yazır göstəriciləri, yer dəyişiklikləri və bildirişlər /hubs/chat ünvanındakı SignalR hub-ı ilə gəlir. Eyni access token ilə qoşul (WebSocket başlıq daşıya bilmədiyi üçün access_token query parametri kimi).",
        ]),
        new("Tam sənədləşmə", [
            "Hər sorğu və cavab tipi ilə tam OpenAPI təsviri koddan yaradılır və administratorlar üçün /swagger ünvanında mövcuddur. WatchingYou üzərində nəsə qurursansa, bizə yaz, giriş verək.",
        ]),
    ]);

    private static readonly InfoPage TermsAz = new("İstifadə şərtləri",
        "WatchingYou-dan istifadə qaydaları. Qısadır, çünki oxunmalıdır.", [
        new("Hesabın", [
            "Yer tutmaq, film icarəyə götürmək, yükləmək və ya yazmaq üçün hesab lazımdır. Real e-poçtunu yaz — kodlar və biletlər ora göndərilir — və şifrəni heç kimə vermə. Hesabınla edilənlərə sən cavabdehsən.",
            "Hesabını istənilən vaxt Hesab səhifəsində silə bilərsən.",
        ]),
        new("Biletlər", [
            "Bilet üzərində yazılan seans, zal və yerlər üçün bir dəfə keçərlidir. QR kod girişdə yoxlanılır; istifadə olunmuş kod yenidən işləmir.",
            "Seansdan 48 saat əvvələdək ləğv etmək olar; qiymətin 30%-i tutulur. 48 saatdan az qalanda və ya seans başlayandan sonra pul qaytarılmır. Seansı biz ləğv etsək, tam məbləğ qaytarılır. Seansın öz səhifəsi fərqli qaydalar təyin edə bilər və o seansa həmin qaydalar aiddir.",
        ]),
        new("İcarə və Watching PRO", [
            "İcarə bir filmi üç gün ərzində 0,50 $-a izləməyə imkan verir; eyni qiymətə daha üç gün uzatmaq olar. Gecikmə cəriməsi yoxdur.",
            "Watching PRO ayda 5 $-dır və ödənilmiş ay ərzində bütün kitabxanaya giriş verir. Filmlər yalnız şəxsi baxış üçündür: kopyalamaq, yazmaq və ya ictimai nümayiş qadağandır.",
        ]),
        new("Yüklədiklərin", [
            "Filmlərinin hüquqları səndə qalır. Birini dərc etməklə onu gizli edənə və ya silənə qədər WatchingYou-da — qalereyalarda və profilində — göstərməyimizə icazə verirsən.",
            "Yalnız özün düzəltdiyini və ya paylaşmağa hüququn olanı yüklə və onun əl işi, yoxsa AI ilə düzəldildiyini dürüst bildir. Hər yükləmə yoxlanılır; bu qaydaları və ya qanunu pozanı rədd edə və ya silə bilərik.",
        ]),
        new("Davranış", [
            "Digər üzvləri təqib etmə, hədələmə və ya aldatma, spam göndərmə, sənə aid olmayan hesablara və ya xidmətin hissələrinə girməyə çalışma. Hər kəs bloklaya və şikayət edə bilər; Təhlükəsizlik masası hər şikayətə baxır və hesabları dayandıra bilər.",
        ]),
        new("Ödənişlər", [
            "Qiymətlər ödənişdən əvvəl göstərilir. Kart ödənişlərini Stripe emal edir; kartın tam nömrəsini heç vaxt görmürük və saxlamırıq.",
        ]),
        new("Dəyişikliklər və məsuliyyət", [
            "Xidməti və bu şərtləri dəyişə bilərik; cari versiya həmişə bu səhifədədir. Xidmətin işləməsi üçün əlimizdən gələni edirik, amma heç vaxt dayanmayacağına söz verə bilmərik. Bu şərtlərdəki heç nə qanunla sahib olduğun hüquqları məhdudlaşdırmır.",
        ]),
    ]);
}
