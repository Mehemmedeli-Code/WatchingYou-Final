/**
 * The watch-order guides. Written for this site, in its four languages; each entry points at
 * a film in the catalogue by its slug, so the page can show its poster and open it.
 */

export type Lang = "az" | "en" | "ru" | "tr";
type Words = Record<Lang, string>;

export interface GuideEntry {
  slug: string;
  title: string;
  year: number;
  note: Words;
}

export interface Guide {
  slug: string;
  /** The entry whose poster is the guide's cover. */
  cover: string;
  published: string;
  title: Words;
  teaser: Words;
  intro: Record<Lang, string[]>;
  /** What the order is: by release, by story, or a way in. */
  orderNote: Words;
  entries: GuideEntry[];
}

export const GUIDES: Guide[] = [
  {
    slug: "star-wars",
    cover: "star-wars-a-new-hope-1977",
    published: "2026-10-01",
    title: {
      az: "Ulduz Döyüşləri hansı sıra ilə izlənməlidir?",
      en: "In what order should you watch Star Wars?",
      ru: "В каком порядке смотреть «Звёздные войны»?",
      tr: "Star Wars hangi sırayla izlenmeli?",
    },
    teaser: {
      az: "Orijinal üçlük: hər şeyin başladığı üç film.",
      en: "The original trilogy: the three films where it all began.",
      ru: "Оригинальная трилогия: три фильма, с которых всё началось.",
      tr: "Orijinal üçleme: her şeyin başladığı üç film.",
    },
    intro: {
      az: [
        "1977-ci ildə çıxan ilk film kinoda bütün bir dövrü dəyişdi: xüsusi effektlər, musiqi və nağıl quruluşu sonrakı onilliklərin blokbasterlərinə nümunə oldu.",
        "Sonradan prekvellər və davam filmləri gəldi, amma ilk dəfə izləyən üçün ən yaxşı başlanğıc hələ də orijinal üçlükdür — həm çıxış, həm də hekayə sırası eynidir.",
      ],
      en: [
        "The first film, in 1977, changed a whole era of cinema: its effects, its music and its fairy-tale shape became the template for decades of blockbusters.",
        "Prequels and sequels came later, but for a first viewing the best way in is still the original trilogy — release order and story order are the same here.",
      ],
      ru: [
        "Первый фильм 1977 года изменил целую эпоху кино: спецэффекты, музыка и сказочная структура стали образцом для блокбастеров на десятилетия вперёд.",
        "Позже вышли приквелы и сиквелы, но для первого знакомства лучше всего начать с оригинальной трилогии — здесь порядок выхода совпадает с порядком сюжета.",
      ],
      tr: [
        "1977'deki ilk film sinemada bir dönemi değiştirdi: efektleri, müziği ve masal kurgusu sonraki on yılların gişe filmlerine örnek oldu.",
        "Önce-hikâyeler ve devam filmleri sonradan geldi, ama ilk izleyiş için en iyi giriş hâlâ orijinal üçleme — burada çıkış sırası ile hikâye sırası aynı.",
      ],
    },
    orderNote: { az: "Çıxış sırası = hekayə sırası", en: "Release order = story order", ru: "Порядок выхода = порядок сюжета", tr: "Çıkış sırası = hikâye sırası" },
    entries: [
      { slug: "star-wars-a-new-hope-1977", title: "Star Wars: A New Hope", year: 1977, note: {
        az: "Kənd uşağı Luk Skayuoker bir mesajın ardınca qalaktikanı xilas etməyə çıxır. Hər şeyin başladığı nöqtə.",
        en: "Farm boy Luke Skywalker follows a message into a fight for the galaxy. Where everything starts.",
        ru: "Фермерский парень Люк Скайуокер идёт за посланием и попадает в борьбу за галактику. Здесь всё начинается.",
        tr: "Çiftçi çocuk Luke Skywalker bir mesajın peşinden galaksi için verilen savaşa katılır. Her şeyin başladığı yer." } },
      { slug: "the-empire-strikes-back-1980", title: "The Empire Strikes Back", year: 1980, note: {
        az: "Daha qaranlıq və daha cəsarətli: üçlüyün ən güclü hissəsi sayılır və bütün sonluğu ilə yadda qalır.",
        en: "Darker and bolder — often called the best of the three, and remembered for its ending.",
        ru: "Мрачнее и смелее — часто называют лучшим фильмом трилогии, а его финал помнят все.",
        tr: "Daha karanlık ve daha cesur; çoğu zaman üçlemenin en iyisi sayılır, finaliyle akılda kalır." } },
      { slug: "return-of-the-jedi-1983", title: "Return of the Jedi", year: 1983, note: {
        az: "Hekayə bağlanır: ata ilə oğulun son qarşılaşması və İmperiyaya qarşı son döyüş.",
        en: "The story closes: father and son meet for the last time, and the Rebellion makes its final stand.",
        ru: "История завершается: последняя встреча отца и сына и решающая битва против Империи.",
        tr: "Hikâye kapanır: baba ile oğulun son karşılaşması ve İmparatorluğa karşı son savaş." } },
    ],
  },
  {
    slug: "lord-of-the-rings",
    cover: "the-lord-of-the-rings-the-fellowship-of-the-ring-2001",
    published: "2026-10-02",
    title: {
      az: "Üzüklərin Hökmdarı: üçlüyün izlənmə sırası",
      en: "The Lord of the Rings: the trilogy in order",
      ru: "«Властелин колец»: трилогия по порядку",
      tr: "Yüzüklerin Efendisi: üçleme sırasıyla",
    },
    teaser: {
      az: "Bir üzük, doqquz yoldaş və üç film boyunca bir səyahət.",
      en: "One ring, nine companions and a journey across three films.",
      ru: "Одно кольцо, девять спутников и путешествие длиной в три фильма.",
      tr: "Bir yüzük, dokuz yol arkadaşı ve üç film süren bir yolculuk.",
    },
    intro: {
      az: [
        "Piter Ceksonun üçlüyü əslində bir böyük filmin üç hissəsidir: üçü də ard-arda çəkilib və hekayə birindən digərinə kəsilmədən keçir.",
        "Ona görə sıranı pozmaq olmaz. Vaxtınız varsa, genişləndirilmiş versiyalar dünyanı daha dərin göstərir.",
      ],
      en: [
        "Peter Jackson's trilogy is really one long film in three parts: shot back to back, with the story running straight from one into the next.",
        "So the order is not optional. If you have the time, the extended editions open the world up even further.",
      ],
      ru: [
        "Трилогия Питера Джексона — по сути один большой фильм в трёх частях: её снимали подряд, и сюжет без перерыва переходит из одной части в другую.",
        "Поэтому порядок нарушать нельзя. Если есть время, расширенные версии показывают мир ещё глубже.",
      ],
      tr: [
        "Peter Jackson'ın üçlemesi aslında üç parçalı tek bir uzun film: arka arkaya çekildi ve hikâye birinden diğerine kesintisiz geçiyor.",
        "Bu yüzden sıra bozulmaz. Vaktiniz varsa uzatılmış versiyonlar dünyayı daha da derinleştirir.",
      ],
    },
    orderNote: { az: "Ancaq bu sıra ilə", en: "Only in this order", ru: "Только в этом порядке", tr: "Yalnızca bu sırayla" },
    entries: [
      { slug: "the-lord-of-the-rings-the-fellowship-of-the-ring-2001", title: "The Fellowship of the Ring", year: 2001, note: {
        az: "Frodo üzüyü alır və yoldaşlar dəstəsi yola çıxır. Dünyanın və qəhrəmanların tanış olduğu hissə.",
        en: "Frodo takes the ring and the fellowship sets out. The part where you meet the world and its people.",
        ru: "Фродо получает кольцо, и братство отправляется в путь. Здесь знакомятся с миром и героями.",
        tr: "Frodo yüzüğü alır ve kardeşlik yola çıkar. Dünyayla ve kahramanlarla tanıştığınız bölüm." } },
      { slug: "the-lord-of-the-rings-the-two-towers-2002", title: "The Two Towers", year: 2002, note: {
        az: "Dəstə dağılıb: hekayə üç yolla gedir və Helm dərəsindəki döyüşlə zirvəyə çatır.",
        en: "The fellowship is broken: the story splits three ways and builds to the battle of Helm's Deep.",
        ru: "Братство распалось: сюжет идёт тремя линиями и подходит к битве при Хельмовой Пади.",
        tr: "Kardeşlik dağılmıştır: hikâye üç yola ayrılır ve Miğfer Dibi savaşında doruğa ulaşır." } },
      { slug: "the-lord-of-the-rings-the-return-of-the-king-2003", title: "The Return of the King", year: 2003, note: {
        az: "Son yürüş: Qondorun müdafiəsi və Hüküm dağına doğru son addımlar. 11 Oskarla bağlanan final.",
        en: "The last march: the defence of Gondor and the final steps to Mount Doom. A finale that took 11 Oscars.",
        ru: "Последний поход: оборона Гондора и последние шаги к Роковой горе. Финал, получивший 11 «Оскаров».",
        tr: "Son yürüyüş: Gondor'un savunması ve Hüküm Dağı'na son adımlar. 11 Oscar'la kapanan final." } },
    ],
  },
  {
    slug: "dune",
    cover: "dune-part-two-2024",
    published: "2026-10-03",
    title: {
      az: "Dune filmləri hansı sıra ilə izlənməlidir?",
      en: "Dune: which film comes first?",
      ru: "«Дюна»: какой фильм смотреть первым?",
      tr: "Dune filmleri hangi sırayla izlenmeli?",
    },
    teaser: {
      az: "Arrakis səhrası, ədviyyat və Pol Atreydesin yolu — iki hissədə.",
      en: "The desert of Arrakis, the spice and Paul Atreides' path — in two parts.",
      ru: "Пустыня Арракиса, пряность и путь Пола Атрейдеса — в двух частях.",
      tr: "Arrakis çölü, baharat ve Paul Atreides'in yolu — iki bölümde.",
    },
    intro: {
      az: [
        "Deni Vilnöv Frenk Herbertin romanını iki filmə böldü. Birinci hissə dünyanı və siyasəti qurur, ikinci hissə isə səhrada başlayan dəyişikliyi göstərir.",
        "İkinci film birincinin bitdiyi andan başlayır, ona görə ayrı-ayrılıqda yox, ard-arda izləmək daha yaxşıdır.",
      ],
      en: [
        "Denis Villeneuve split Frank Herbert's novel in two. The first part builds the world and its politics; the second follows the change that starts in the desert.",
        "Part Two picks up the moment Part One ends, so they are best watched back to back.",
      ],
      ru: [
        "Дени Вильнёв разделил роман Фрэнка Герберта на два фильма. Первый строит мир и его политику, второй показывает перемену, начавшуюся в пустыне.",
        "Вторая часть начинается ровно там, где закончилась первая, поэтому их лучше смотреть подряд.",
      ],
      tr: [
        "Denis Villeneuve, Frank Herbert'in romanını ikiye böldü. İlk bölüm dünyayı ve siyaseti kurar, ikincisi çölde başlayan değişimi anlatır.",
        "İkinci bölüm, ilkinin bittiği andan başlar; bu yüzden art arda izlemek en iyisi.",
      ],
    },
    orderNote: { az: "Ard-arda izləyin", en: "Watch back to back", ru: "Смотрите подряд", tr: "Art arda izleyin" },
    entries: [
      { slug: "dune-2021", title: "Dune", year: 2021, note: {
        az: "Atreydes ailəsi Arrakisə gəlir və tələyə düşür. Böyük miqyaslı, sakit və həyəcanlı başlanğıc.",
        en: "House Atreides arrives on Arrakis and walks into a trap. A vast, patient, gripping opening.",
        ru: "Дом Атрейдесов прибывает на Арракис и попадает в ловушку. Масштабное, неторопливое и захватывающее начало.",
        tr: "Atreides Hanedanı Arrakis'e gelir ve tuzağa düşer. Devasa, sabırlı ve sürükleyici bir başlangıç." } },
      { slug: "dune-part-two-2024", title: "Dune: Part Two", year: 2024, note: {
        az: "Pol fremenlərlə birləşir və intiqam yolu ilə gedir. Döyüşlər, qum qurdları və ağır seçimlər.",
        en: "Paul joins the Fremen and takes the road of revenge. Battles, sandworms and heavy choices.",
        ru: "Пол присоединяется к фрименам и выбирает путь мести. Битвы, песчаные черви и тяжёлый выбор.",
        tr: "Paul Fremenlere katılır ve intikam yoluna girer. Savaşlar, kum solucanları ve ağır seçimler." } },
    ],
  },
  {
    slug: "godfather",
    cover: "the-godfather-1972",
    published: "2026-10-04",
    title: {
      az: "Xaç Atası: Korleone ailəsinin hekayəsi hansı sıra ilə?",
      en: "The Godfather: the Corleone saga in order",
      ru: "«Крёстный отец»: сага о Корлеоне по порядку",
      tr: "Baba: Corleone ailesinin hikâyesi sırasıyla",
    },
    teaser: {
      az: "Kino tarixinin ən böyük ailə dramı — iki hissədə, iki dövrdə.",
      en: "Cinema's great family drama — two films, two eras.",
      ru: "Величайшая семейная драма в истории кино — два фильма, две эпохи.",
      tr: "Sinema tarihinin en büyük aile draması — iki film, iki dönem.",
    },
    intro: {
      az: [
        "Frensis Ford Koppolanın filmləri bir ailənin və bir gücün necə dəyişdiyini anladır. İkinci hissə eyni zamanda həm davam, həm də prekveldir: Mayklın hekayəsi gənc Vito Korleonenin hekayəsi ilə növbələşir.",
        "Buna görə ilk dəfə izləyirsinizsə, çıxış sırasına əməl edin — ikinci film birincini bildiyinizə arxalanır.",
      ],
      en: [
        "Francis Ford Coppola's films follow how a family, and a kind of power, changes. Part II is both a sequel and a prequel: Michael's story alternates with young Vito Corleone's.",
        "On a first viewing, keep to release order — the second film counts on you knowing the first.",
      ],
      ru: [
        "Фильмы Фрэнсиса Форда Копполы рассказывают, как меняются семья и власть. Вторая часть — одновременно сиквел и приквел: история Майкла чередуется с историей молодого Вито Корлеоне.",
        "При первом просмотре держитесь порядка выхода — второй фильм рассчитывает, что вы знаете первый.",
      ],
      tr: [
        "Francis Ford Coppola'nın filmleri bir ailenin ve bir gücün nasıl değiştiğini anlatır. İkinci film hem devam hem önce-hikâyedir: Michael'ın hikâyesi genç Vito Corleone'ninkiyle iç içe geçer.",
        "İlk kez izliyorsanız çıkış sırasını koruyun — ikinci film birincisini bildiğinizi varsayar.",
      ],
    },
    orderNote: { az: "Çıxış sırası ilə", en: "In release order", ru: "В порядке выхода", tr: "Çıkış sırasıyla" },
    entries: [
      { slug: "the-godfather-1972", title: "The Godfather", year: 1972, note: {
        az: "Vito Korleone ailəsini qorumağa çalışır, ən kiçik oğlu Mayk isə istəmədiyi bir dünyaya çəkilir.",
        en: "Vito Corleone tries to protect his family while his youngest, Michael, is drawn into a world he wanted no part of.",
        ru: "Вито Корлеоне пытается защитить семью, а младший сын Майкл втягивается в мир, от которого хотел держаться подальше.",
        tr: "Vito Corleone ailesini korumaya çalışırken en küçük oğlu Michael hiç istemediği bir dünyaya çekilir." } },
      { slug: "the-godfather-part-ii-1974", title: "The Godfather Part II", year: 1974, note: {
        az: "Mayklın gücü artdıqca yalnızlığı da artır; paralel olaraq gənc Vitonun Nyu-Yorkdakı yolu göstərilir.",
        en: "As Michael's power grows, so does his solitude; alongside it, young Vito's rise in New York.",
        ru: "Чем больше власти у Майкла, тем он одиноче; параллельно — путь молодого Вито в Нью-Йорке.",
        tr: "Michael'ın gücü arttıkça yalnızlığı da artar; paralelinde genç Vito'nun New York'taki yükselişi." } },
    ],
  },
  {
    slug: "alien",
    cover: "alien-1979",
    published: "2026-10-05",
    title: {
      az: "Yadplanetli (Alien) filmləri hansı sıra ilə?",
      en: "Alien: where to start and what comes next",
      ru: "«Чужой»: с чего начать и что дальше",
      tr: "Alien filmleri hangi sırayla?",
    },
    teaser: {
      az: "Kosmosda dəhşət, sonra döyüş: iki fərqli rejissor, bir qəhrəman.",
      en: "Horror in space, then war: two directors, one heroine.",
      ru: "Ужас в космосе, потом война: два режиссёра, одна героиня.",
      tr: "Uzayda korku, sonra savaş: iki yönetmen, bir kahraman.",
    },
    intro: {
      az: [
        "Ridli Skottun 1979-cu il filmi yavaş və boğucu bir dəhşət filmidir. Ceyms Kameronun 1986-cı il davamı isə janrı dəyişib aksiyaya çevirir.",
        "İkisini də çıxış sırası ilə izləyin: Ellen Riplinin hekayəsi birbaşa davam edir və ikinci film birincinin sükutundan güc alır.",
      ],
      en: [
        "Ridley Scott's 1979 film is slow, airless horror. James Cameron's 1986 sequel changes genre and turns it into action.",
        "Watch them in release order: Ellen Ripley's story carries straight on, and the second film draws its power from the first one's silence.",
      ],
      ru: [
        "Фильм Ридли Скотта 1979 года — медленный, удушающий хоррор. Продолжение Джеймса Кэмерона 1986 года меняет жанр и превращает его в боевик.",
        "Смотрите в порядке выхода: история Эллен Рипли продолжается напрямую, а второй фильм черпает силу из тишины первого.",
      ],
      tr: [
        "Ridley Scott'ın 1979 filmi yavaş ve boğucu bir korku filmi. James Cameron'ın 1986 devamı ise türü değiştirip aksiyona çeviriyor.",
        "Çıkış sırasıyla izleyin: Ellen Ripley'nin hikâyesi doğrudan devam eder ve ikinci film gücünü ilkinin sessizliğinden alır.",
      ],
    },
    orderNote: { az: "Çıxış sırası ilə", en: "In release order", ru: "В порядке выхода", tr: "Çıkış sırasıyla" },
    entries: [
      { slug: "alien-1979", title: "Alien", year: 1979, note: {
        az: "Nostromo gəmisinin heyəti siqnalın ardınca gedir və gəmiyə nəsə gətirir. Səs-küysüz, gərgin klassika.",
        en: "The crew of the Nostromo answer a signal and bring something back aboard. A quiet, tense classic.",
        ru: "Экипаж «Ностромо» откликается на сигнал и приносит что-то на борт. Тихая, напряжённая классика.",
        tr: "Nostromo mürettebatı bir sinyale gider ve gemiye bir şey getirir. Sessiz, gergin bir klasik." } },
      { slug: "aliens-1986", title: "Aliens", year: 1986, note: {
        az: "Ripli dəniz piyadaları ilə həmin planetə qayıdır. Daha böyük, daha sürətli, daha çox canavar.",
        en: "Ripley goes back with a squad of marines. Bigger, faster, and far more of them.",
        ru: "Рипли возвращается на планету с отрядом морпехов. Больше, быстрее и куда больше чудовищ.",
        tr: "Ripley bir deniz piyadesi birliğiyle aynı gezegene döner. Daha büyük, daha hızlı, çok daha fazlası." } },
    ],
  },
  {
    slug: "blade-runner",
    cover: "blade-runner-2049-2017",
    published: "2026-10-06",
    title: {
      az: "Blade Runner: iki film, otuz beş il",
      en: "Blade Runner: two films, thirty-five years apart",
      ru: "«Бегущий по лезвию»: два фильма с разницей в 35 лет",
      tr: "Blade Runner: iki film, otuz beş yıl",
    },
    teaser: {
      az: "Yağışlı Los-Anceles, replikantlar və insan olmağın nə demək olduğu.",
      en: "Rain-soaked Los Angeles, replicants and what it means to be human.",
      ru: "Дождливый Лос-Анджелес, репликанты и вопрос о том, что значит быть человеком.",
      tr: "Yağmurlu Los Angeles, replikantlar ve insan olmanın anlamı.",
    },
    intro: {
      az: [
        "Ridli Skottun 1982-ci il filmi elmi fantastikanın görünüşünü dəyişdi. 2017-ci ildə Deni Vilnöv eyni dünyaya otuz il sonra qayıtdı.",
        "İkinci film birincinin suallarına cavab axtarır və onun personajlarına qayıdır, ona görə əvvəlcə 1982-ci il filmini izləyin.",
      ],
      en: [
        "Ridley Scott's 1982 film changed how science fiction looks. In 2017 Denis Villeneuve went back to the same world, thirty years on.",
        "The second film answers the first one's questions and returns to its people, so start with 1982.",
      ],
      ru: [
        "Фильм Ридли Скотта 1982 года изменил облик научной фантастики. В 2017 году Дени Вильнёв вернулся в тот же мир спустя тридцать лет.",
        "Второй фильм ищет ответы на вопросы первого и возвращается к его героям, поэтому начинайте с 1982 года.",
      ],
      tr: [
        "Ridley Scott'ın 1982 filmi bilim kurgunun görünüşünü değiştirdi. 2017'de Denis Villeneuve aynı dünyaya otuz yıl sonra döndü.",
        "İkinci film ilkinin sorularına cevap arar ve karakterlerine geri döner; bu yüzden 1982'den başlayın.",
      ],
    },
    orderNote: { az: "Əvvəlcə 1982", en: "1982 first", ru: "Сначала 1982", tr: "Önce 1982" },
    entries: [
      { slug: "blade-runner-1982", title: "Blade Runner", year: 1982, note: {
        az: "Rik Dekard qaçaq replikantları tapmalıdır. Neon, yağış və yaddaşlar haqqında film.",
        en: "Rick Deckard has to hunt down runaway replicants. A film about neon, rain and memory.",
        ru: "Рик Декард должен выследить беглых репликантов. Фильм о неоне, дожде и памяти.",
        tr: "Rick Deckard kaçak replikantları bulmak zorundadır. Neon, yağmur ve anılar üzerine bir film." } },
      { slug: "blade-runner-2049-2017", title: "Blade Runner 2049", year: 2017, note: {
        az: "Yeni bleyd-raner K köhnə bir sirri tapır və Dekardın izinə düşür. Ağır, gözəl və düşündürücü.",
        en: "A new blade runner, K, uncovers an old secret that leads him to Deckard. Slow, beautiful, haunting.",
        ru: "Новый бегущий по лезвию, К, раскрывает старую тайну, которая ведёт его к Декарду. Медленно, красиво, завораживающе.",
        tr: "Yeni bir blade runner olan K, eski bir sırrı keşfeder ve Deckard'ın izini sürer. Ağır, güzel ve etkileyici." } },
    ],
  },
  {
    slug: "nolan",
    cover: "inception-2010",
    published: "2026-10-07",
    title: {
      az: "Kristofer Nolanın filmləri: hansı sıra ilə izləmək olar?",
      en: "Christopher Nolan's films, in order",
      ru: "Фильмы Кристофера Нолана по порядку",
      tr: "Christopher Nolan filmleri hangi sırayla?",
    },
    teaser: {
      az: "Zaman, yaddaş və böyük ekran: kataloqdakı yeddi Nolan filmi.",
      en: "Time, memory and the big screen: seven Nolan films from the catalogue.",
      ru: "Время, память и большой экран: семь фильмов Нолана из каталога.",
      tr: "Zaman, hafıza ve büyük ekran: katalogdaki yedi Nolan filmi.",
    },
    intro: {
      az: [
        "Nolanın filmləri bir seriya deyil, amma eyni suallara qayıdır: zaman necə işləyir, yaddaşa nə qədər inanmaq olar, insan nə vaxt öz yalanına inanır.",
        "Çıxış sırası ilə izləmək rejissorun necə böyüdüyünü göstərir — kiçik, ağıllı triller olan Mementodan böyük tarixi dram olan Oppenheimerə qədər.",
      ],
      en: [
        "Nolan's films are not a series, but they keep returning to the same questions: how time works, how far memory can be trusted, when a person starts believing their own lie.",
        "Watching in release order shows a director growing — from the small, clever thriller Memento to the vast historical drama Oppenheimer.",
      ],
      ru: [
        "Фильмы Нолана — не серия, но они возвращаются к одним и тем же вопросам: как устроено время, можно ли доверять памяти, когда человек начинает верить собственной лжи.",
        "Просмотр в порядке выхода показывает, как рос режиссёр — от небольшого умного триллера «Помни» до масштабной исторической драмы «Оппенгеймер».",
      ],
      tr: [
        "Nolan'ın filmleri bir seri değil ama hep aynı sorulara döner: zaman nasıl işler, hafızaya ne kadar güvenilir, insan kendi yalanına ne zaman inanır.",
        "Çıkış sırasıyla izlemek, küçük ve zekice bir gerilim olan Memento'dan devasa bir tarih draması olan Oppenheimer'a uzanan gelişimi gösterir.",
      ],
    },
    orderNote: { az: "Çıxış sırası ilə", en: "In release order", ru: "В порядке выхода", tr: "Çıkış sırasıyla" },
    entries: [
      { slug: "memento-2000", title: "Memento", year: 2000, note: {
        az: "Yaddaşını itirən adam qatili axtarır, hekayə isə tərsinə danışılır. Nolanı tanıtdıran film.",
        en: "A man who cannot form new memories hunts a killer, told backwards. The film that made Nolan's name.",
        ru: "Человек, не способный запоминать новое, ищет убийцу, а история рассказана задом наперёд. Фильм, сделавший Нолану имя.",
        tr: "Yeni anı oluşturamayan bir adam katili arar; hikâye tersten anlatılır. Nolan'ı tanıtan film." } },
      { slug: "the-prestige-2006", title: "The Prestige", year: 2006, note: {
        az: "İki sehrbazın rəqabəti dəlilik həddinə çatır. Sonunda hər şeyi yenidən düşünməyə məcbur edir.",
        en: "Two magicians' rivalry turns into obsession. The ending makes you rethink everything.",
        ru: "Соперничество двух фокусников превращается в одержимость. Финал заставляет пересмотреть всё.",
        tr: "İki sihirbazın rekabeti saplantıya dönüşür. Final her şeyi yeniden düşündürür." } },
      { slug: "the-dark-knight-2008", title: "The Dark Knight", year: 2008, note: {
        az: "Betmen Coker ilə üz-üzə gəlir. Superqəhrəman filmlərinin ölçüsünü dəyişən kriminal dram.",
        en: "Batman meets the Joker. A crime drama that changed what superhero films could be.",
        ru: "Бэтмен против Джокера. Криминальная драма, изменившая представление о фильмах про супергероев.",
        tr: "Batman, Joker'le karşı karşıya. Süper kahraman filmlerinin çıtasını değiştiren bir suç draması." } },
      { slug: "inception-2010", title: "Inception", year: 2010, note: {
        az: "Yuxunun içində yuxu: fikir oğurlayan dəstə ən çətin işini görür.",
        en: "A dream within a dream: a team that steals ideas takes on its hardest job.",
        ru: "Сон во сне: команда, ворующая идеи, берётся за самое сложное дело.",
        tr: "Rüya içinde rüya: fikir çalan bir ekip en zor işine girişir." } },
      { slug: "interstellar-2014", title: "Interstellar", year: 2014, note: {
        az: "Ölməkdə olan Yerdən ulduzlara yolçuluq — və ata ilə qızı arasında zamanla uzanan bağ.",
        en: "A journey from a dying Earth to the stars — and a bond between father and daughter stretched by time.",
        ru: "Путешествие с умирающей Земли к звёздам — и связь отца с дочерью, растянутая временем.",
        tr: "Ölmekte olan Dünya'dan yıldızlara bir yolculuk — ve zamanla gerilen bir baba-kız bağı." } },
      { slug: "dunkirk-2017", title: "Dunkirk", year: 2017, note: {
        az: "Quru, dəniz və hava: üç fərqli zamanda danışılan bir xilasetmə əməliyyatı.",
        en: "Land, sea and air: one evacuation told across three timelines.",
        ru: "Суша, море и воздух: одна эвакуация, рассказанная в трёх временных линиях.",
        tr: "Kara, deniz ve hava: üç zaman çizgisinde anlatılan tek bir tahliye." } },
      { slug: "oppenheimer-2023", title: "Oppenheimer", year: 2023, note: {
        az: "Atom bombasını yaradan alimin yüksəlişi və bədəli. Nolanın ən böyük dramı.",
        en: "The rise, and the cost, of the scientist behind the atomic bomb. Nolan's largest drama.",
        ru: "Взлёт и цена учёного, создавшего атомную бомбу. Самая масштабная драма Нолана.",
        tr: "Atom bombasının arkasındaki bilim insanının yükselişi ve bedeli. Nolan'ın en büyük draması." } },
    ],
  },
  {
    slug: "ghibli",
    cover: "spirited-away-2001",
    published: "2026-10-08",
    title: {
      az: "Studio Ghibli: haradan başlamalı?",
      en: "Studio Ghibli: where to begin",
      ru: "Studio Ghibli: с чего начать",
      tr: "Studio Ghibli: nereden başlamalı?",
    },
    teaser: {
      az: "Hayao Miyazakinin dörd filmi, ən yumşağından ən sərtinə doğru.",
      en: "Four Hayao Miyazaki films, from the gentlest to the fiercest.",
      ru: "Четыре фильма Хаяо Миядзаки — от самого мягкого к самому суровому.",
      tr: "Hayao Miyazaki'nin dört filmi, en yumuşağından en sertine.",
    },
    intro: {
      az: [
        "Ghibli filmləri bir-biri ilə əlaqəli deyil, hər biri ayrıca bir dünyadır. Ona görə burada sıra hekayəyə görə yox, ilk dəfə izləyənin rahatlığına görə qurulub.",
        "Uşaqlar üçün sakit bir başlanğıcdan, təbiət və insan haqqında daha ağır bir hekayəyə doğru gedirik.",
      ],
      en: [
        "Ghibli films are not connected; each one is a world of its own. So the order here is not about story but about a first-time viewer's ease.",
        "We go from a calm beginning that children love to a darker tale about nature and people.",
      ],
      ru: [
        "Фильмы Ghibli не связаны между собой: каждый — отдельный мир. Поэтому порядок здесь выбран не по сюжету, а для удобства того, кто смотрит впервые.",
        "Мы идём от спокойного начала, которое любят дети, к более тяжёлой истории о природе и людях.",
      ],
      tr: [
        "Ghibli filmleri birbirine bağlı değil; her biri ayrı bir dünya. Bu yüzden sıra burada hikâyeye göre değil, ilk kez izleyenin rahatlığına göre kuruldu.",
        "Çocukların sevdiği sakin bir başlangıçtan doğa ve insan üzerine daha karanlık bir hikâyeye ilerliyoruz.",
      ],
    },
    orderNote: { az: "Başlanğıc üçün sıra", en: "A way in", ru: "Порядок для начала", tr: "Başlangıç için sıra" },
    entries: [
      { slug: "my-neighbor-totoro-1988", title: "My Neighbor Totoro", year: 1988, note: {
        az: "İki bacı kənddə meşə ruhu Totoro ilə tanış olur. Studiyanın simvolu, ən isti başlanğıc.",
        en: "Two sisters in the countryside meet Totoro, the spirit of the forest. The studio's emblem and the warmest start.",
        ru: "Две сестры в деревне знакомятся с Тоторо, духом леса. Символ студии и самое тёплое начало.",
        tr: "Kırsalda iki kız kardeş orman ruhu Totoro ile tanışır. Stüdyonun simgesi ve en sıcak başlangıç." } },
      { slug: "spirited-away-2001", title: "Spirited Away", year: 2001, note: {
        az: "Çihiro ruhlar dünyasına düşür və valideynlərini xilas etməlidir. Oskar qazanan şah əsər.",
        en: "Chihiro wanders into the spirit world and must save her parents. The Oscar-winning masterpiece.",
        ru: "Тихиро попадает в мир духов и должна спасти родителей. Шедевр, получивший «Оскар».",
        tr: "Chihiro ruhlar dünyasına düşer ve ailesini kurtarmak zorundadır. Oscar'lı başyapıt." } },
      { slug: "howl-s-moving-castle-2004", title: "Howl's Moving Castle", year: 2004, note: {
        az: "Lənətlənmiş Sofi sehrbaz Haulun gəzən qəsrinə düşür. Sevgi və müharibə haqqında nağıl.",
        en: "Cursed Sophie finds her way into the wizard Howl's walking castle. A fairy tale about love and war.",
        ru: "Заколдованная Софи попадает в ходячий замок волшебника Хаула. Сказка о любви и войне.",
        tr: "Lanetlenen Sophie, büyücü Howl'un yürüyen şatosuna düşer. Aşk ve savaş üzerine bir masal." } },
      { slug: "princess-mononoke-1997", title: "Princess Mononoke", year: 1997, note: {
        az: "Meşənin tanrıları ilə insanların savaşı. Ən sərt və ən böyük Ghibli filmi — sona saxlayın.",
        en: "The gods of the forest at war with people. The fiercest and grandest Ghibli — save it for last.",
        ru: "Война лесных богов с людьми. Самый суровый и масштабный фильм Ghibli — оставьте его напоследок.",
        tr: "Orman tanrılarıyla insanların savaşı. En sert ve en büyük Ghibli filmi — sona saklayın." } },
    ],
  },
];
