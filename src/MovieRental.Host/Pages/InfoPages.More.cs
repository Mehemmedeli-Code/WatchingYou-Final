namespace MovieRental.Host.Pages;

// The phone app and the page for people without an account, in the four languages.
public static partial class InfoPages
{
    private static readonly InfoPage AppEn = new("The WatchingYou app",
        "The cinema in your pocket: tickets that open without signal, reminders before the show, and the library on the go.", [
        new("Films", ["Browse what is on in the cinema and in the library, watch trailers, book seats on the live seat map and rent films — the same account as the website."]),
        new("My tickets", [
            "Every ticket you buy is kept on the phone with its QR code, so it opens at the door even in a basement with no signal. Signing out removes them from the device.",
            "The app reminds you before a show starts, so the popcorn queue never costs you the opening scene.",
        ]),
        new("Account", ["Sign in, register, confirm your e-mail or phone, change your profile and delete your account — all from the app."]),
        new("For staff", ["Security staff get a Door tab that scans tickets at the entrance; a ticket that has already been used is refused on the spot."]),
        new("Get it", ["The app is being prepared for the App Store and Google Play. Until then, the website works on any phone and can be added to the home screen from the browser menu."]),
    ]);

    private static readonly InfoPage AppAz = new("WatchingYou tətbiqi",
        "Cibində kinoteatr: siqnalsız açılan biletlər, seansdan əvvəl xatırlatma və yolda kitabxana.", [
        new("Filmlər", ["Kinoteatrda və kitabxanada nə olduğuna bax, treylerləri izlə, canlı yer xəritəsində yer tut və film icarəyə götür — saytdakı eyni hesabla."]),
        new("Biletlərim", [
            "Aldığın hər bilet QR kodu ilə telefonda saxlanılır, ona görə siqnal olmayan zirzəmidə belə girişdə açılır. Hesabdan çıxanda cihazdan silinir.",
            "Tətbiq seans başlamazdan əvvəl xatırladır, popkorn növbəsi sənə ilk səhnəni itirtməsin.",
        ]),
        new("Hesab", ["Daxil ol, qeydiyyatdan keç, e-poçtunu və ya telefonunu təsdiqlə, profilini dəyiş və hesabını sil — hamısı tətbiqdən."]),
        new("Əməkdaşlar üçün", ["Təhlükəsizlik əməkdaşlarının Giriş tabı var: biletləri qapıda skan edir, artıq istifadə olunmuş bilet dərhal rədd edilir."]),
        new("Necə əldə etmək olar", ["Tətbiq App Store və Google Play üçün hazırlanır. O vaxta qədər sayt istənilən telefonda işləyir və brauzer menyusundan ana ekrana əlavə edilə bilər."]),
    ]);

    private static readonly InfoPage AppRu = new("Приложение WatchingYou",
        "Кинотеатр в кармане: билеты, которые открываются без сети, напоминания перед сеансом и фильмотека в дороге.", [
        new("Фильмы", ["Смотрите, что идёт в кино и в библиотеке, трейлеры, бронируйте места на живой схеме зала и берите фильмы напрокат — с тем же аккаунтом, что и на сайте."]),
        new("Мои билеты", [
            "Каждый купленный билет хранится в телефоне вместе с QR-кодом, поэтому он открывается у входа даже в подвале без сети. При выходе из аккаунта билеты удаляются с устройства.",
            "Приложение напомнит о сеансе заранее, чтобы очередь за попкорном не стоила вам первой сцены.",
        ]),
        new("Аккаунт", ["Вход, регистрация, подтверждение e-mail или телефона, изменение профиля и удаление аккаунта — всё в приложении."]),
        new("Для сотрудников", ["У сотрудников безопасности есть вкладка «Вход» для сканирования билетов; уже использованный билет сразу отклоняется."]),
        new("Как установить", ["Приложение готовится к публикации в App Store и Google Play. А пока сайт работает на любом телефоне, и его можно добавить на главный экран из меню браузера."]),
    ]);

    private static readonly InfoPage AppTr = new("WatchingYou uygulaması",
        "Cebindeki sinema: çekmeden açılan biletler, seanstan önce hatırlatma ve yolda kütüphane.", [
        new("Filmler", ["Sinemada ve kütüphanede neler olduğuna bak, fragmanları izle, canlı koltuk haritasında yer ayır ve film kirala — web sitesiyle aynı hesapla."]),
        new("Biletlerim", [
            "Aldığın her bilet QR koduyla telefonda saklanır; çekmeyen bir bodrumda bile kapıda açılır. Çıkış yapınca cihazdan silinir.",
            "Uygulama seans başlamadan önce hatırlatır, patlamış mısır sırası sana açılış sahnesini kaçırtmaz.",
        ]),
        new("Hesap", ["Giriş yap, kayıt ol, e-postanı veya telefonunu doğrula, profilini değiştir ve hesabını sil — hepsi uygulamadan."]),
        new("Personel için", ["Güvenlik personelinin bir Kapı sekmesi var: biletleri girişte tarar, kullanılmış bir bilet anında reddedilir."]),
        new("Nasıl edinilir", ["Uygulama App Store ve Google Play için hazırlanıyor. O zamana kadar web sitesi her telefonda çalışır ve tarayıcı menüsünden ana ekrana eklenebilir."]),
    ]);

    private static readonly InfoPage NonUsersEn = new("Contacts and people without an account",
        "What WatchingYou knows about people who have never signed up — and what it does not.", [
        new("We do not upload contacts", ["Neither the website nor the app asks for your address book. We never import phone numbers or e-mail addresses of your friends, and nobody can be invited or found through someone else's contacts."]),
        new("If you have no account", [
            "You can see what is showing and which seats are free without signing up. For that we keep only what every web server keeps: the address a request came from, for a short time, to protect the site from abuse.",
            "We store no profile, no cookie for advertising and nothing that identifies you by name.",
        ]),
        new("If someone mentions you", ["Members can write about anything in messages and help chats. If a message names you or shares something about you that you want removed, write to us and the Security desk will look at it."]),
        new("If an address was used without your consent", ["If you received an e-mail from us but never signed up, someone typed your address. The account cannot be used until the code sent to you is confirmed, so ignore the e-mail — or tell us and we will delete the unconfirmed account."]),
    ]);

    private static readonly InfoPage NonUsersAz = new("Kontaktlar və hesabı olmayanlar",
        "WatchingYou heç vaxt qeydiyyatdan keçməyən insanlar haqqında nə bilir və nə bilmir.", [
        new("Kontaktları yükləmirik", ["Nə sayt, nə də tətbiq ünvan kitabçanı istəmir. Dostlarının telefon nömrələrini və ya e-poçt ünvanlarını heç vaxt götürmürük, heç kim başqasının kontaktları vasitəsilə dəvət oluna və ya tapıla bilməz."]),
        new("Hesabın yoxdursa", [
            "Qeydiyyatsız da afişaya və boş yerlərə baxa bilərsən. Bunun üçün yalnız hər veb serverin saxladığını saxlayırıq: sorğunun gəldiyi ünvanı, qısa müddətə, saytı sui-istifadədən qorumaq üçün.",
            "Heç bir profil, reklam cookie-si və ya səni adla tanıdan heç nə saxlamırıq.",
        ]),
        new("Kimsə səndən danışırsa", ["Üzvlər mesajlarda və dəstək söhbətlərində istənilən mövzuda yaza bilər. Mesajda sənin adın çəkilirsə və ya silinməsini istədiyin bir şey paylaşılıbsa, bizə yaz — Təhlükəsizlik masası baxacaq."]),
        new("Ünvanın icazəsiz istifadə olunubsa", ["Qeydiyyatdan keçmədiyin halda bizdən e-poçt almısansa, kimsə sənin ünvanını yazıb. Sənə göndərilən kod təsdiqlənənə qədər hesab işləmir — e-poçta fikir vermə və ya bizə yaz, təsdiqlənməmiş hesabı silək."]),
    ]);

    private static readonly InfoPage NonUsersRu = new("Контакты и люди без аккаунта",
        "Что WatchingYou знает о людях, которые никогда не регистрировались, — и чего не знает.", [
        new("Мы не загружаем контакты", ["Ни сайт, ни приложение не запрашивают вашу адресную книгу. Мы никогда не импортируем номера телефонов и адреса почты ваших друзей, и никого нельзя пригласить или найти через чужие контакты."]),
        new("Если у вас нет аккаунта", [
            "Афишу и свободные места можно смотреть без регистрации. Для этого мы храним только то, что хранит любой веб-сервер: адрес, с которого пришёл запрос, недолго и только для защиты сайта от злоупотреблений.",
            "Мы не храним профиль, рекламные cookie и ничего, что называло бы вас по имени.",
        ]),
        new("Если о вас упоминают", ["Участники могут писать о чём угодно в сообщениях и чатах поддержки. Если сообщение называет вас или раскрывает что-то, что вы хотите удалить, напишите нам — служба безопасности разберётся."]),
        new("Если ваш адрес использовали без согласия", ["Если вы получили от нас письмо, но не регистрировались, кто-то ввёл ваш адрес. Аккаунт не работает, пока не подтверждён присланный вам код, — просто проигнорируйте письмо или напишите нам, и мы удалим неподтверждённый аккаунт."]),
    ]);

    private static readonly InfoPage NonUsersTr = new("Kişiler ve hesabı olmayanlar",
        "WatchingYou'nun hiç kayıt olmamış insanlar hakkında bildikleri — ve bilmedikleri.", [
        new("Kişileri yüklemiyoruz", ["Ne web sitesi ne de uygulama adres defterini ister. Arkadaşlarının telefon numaralarını veya e-posta adreslerini asla almayız; kimse başkasının kişileri üzerinden davet edilemez veya bulunamaz."]),
        new("Hesabın yoksa", [
            "Vizyondakileri ve boş koltukları kayıt olmadan görebilirsin. Bunun için yalnızca her web sunucusunun tuttuğunu tutarız: isteğin geldiği adresi, kısa bir süre, siteyi kötüye kullanıma karşı korumak için.",
            "Profil, reklam çerezi veya seni adınla tanımlayan hiçbir şey saklamayız.",
        ]),
        new("Biri senden bahsederse", ["Üyeler mesajlarda ve destek sohbetlerinde her konuda yazabilir. Bir mesaj adını anıyorsa veya kaldırılmasını istediğin bir şey paylaşıyorsa bize yaz; Güvenlik masası inceler."]),
        new("Adresin izinsiz kullanıldıysa", ["Kayıt olmadığın hâlde bizden e-posta aldıysan, biri adresini yazmış demektir. Sana gönderilen kod onaylanana kadar hesap kullanılamaz; e-postayı yok say ya da bize yaz, onaylanmamış hesabı silelim."]),
    ]);
}
