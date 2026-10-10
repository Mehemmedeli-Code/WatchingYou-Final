using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;

namespace MovieRental.Host.Infrastructure;

/// <summary>
/// Made-up members for trying the social side by hand: profiles with films, followers, follow
/// requests and conversations. Development only, run on demand, never at startup:
///
///   MovieRental.Host.exe --seed-demo-people     add them (again: skipped if they exist)
///   MovieRental.Host.exe --remove-demo-people   remove them and everything they made
///
/// Every address ends in ".wydemo@gmail.com", which is how removal finds them. Nothing here
/// sends mail: the accounts are written straight to the database, already confirmed.
/// All of them sign in with <see cref="Password"/>.
/// </summary>
public static class DemoPeople
{
    public const string Password = "Demo12345";
    private const string Marker = ".wydemo@gmail.com";

    private sealed record Person(string Username, string FullName, string Bio, string City, string Country,
        double Lat, double Lon, bool IsPrivate = false);

    private static readonly Person[] People =
    [
        new("aysel.quliyeva", "Aysel Quliyeva", "Qısametrajlı film həvəskarı · Bakı 🎬", "Baku", "AZ", 40.4093, 49.8671),
        new("murad.aliyev", "Murad Əliyev", "Operator. Kadrlarım öz-özünə danışır.", "Baku", "AZ", 40.4093, 49.8671),
        new("nigar.hasanova", "Nigar Həsənova", "AI ilə kino eksperimentləri 🤖", "Ganja", "AZ", 40.6828, 46.3606, IsPrivate: true),
        new("elvin.mammadli", "Elvin Məmmədli", "Montajçı · gecə kinosu sevən", "Sumqayit", "AZ", 40.5897, 49.6686),
        new("leyla.rzayeva", "Leyla Rzayeva", "Sənədli film · Şəki", "Shaki", "AZ", 41.1975, 47.1706),
        new("kamal.huseynov", "Kamal Hüseynov", "Lənkəran çaylarından kadrlar", "Lankaran", "AZ", 38.7543, 48.8506),
        new("sabina.ismayilova", "Sabina İsmayılova", "Rejissor köməkçisi. Kofe + kino.", "Baku", "AZ", 40.4093, 49.8671, IsPrivate: true),
        new("tural.qasimov", "Tural Qasımov", "Stop-motion · əl işi", "Ganja", "AZ", 40.6828, 46.3606),
        new("zeynep.yilmaz", "Zeynep Yılmaz", "İstanbul'dan kısa filmler 🎞️", "Istanbul", "TR", 41.0082, 28.9784),
        new("emre.kaya", "Emre Kaya", "Görüntü yönetmeni · Ankara", "Ankara", "TR", 39.9334, 32.8597),
        new("elif.demir", "Elif Demir", "Yapay zekâ ile hikâye anlatıcılığı", "Izmir", "TR", 38.4237, 27.1428, IsPrivate: true),
        new("aliya.nurlanovna", "Aliya Nurlanovna", "Almaty · animation & AI", "Almaty", "KZ", 43.2389, 76.8897),
        new("daniyar.sadykov", "Daniyar Sadykov", "Steppe stories, shot by hand", "Astana", "KZ", 51.1694, 71.4491),
        new("aigerim.bekova", "Aigerim Bekova", "Кино — это память.", "Almaty", "KZ", 43.2389, 76.8897),
        new("nino.beridze", "Nino Beridze", "Tbilisi · 16mm lover", "Tbilisi", "GE", 41.7151, 44.8271),
        new("giorgi.kapanadze", "Giorgi Kapanadze", "Short docs about old Tbilisi", "Tbilisi", "GE", 41.7151, 44.8271),
        new("anna.smirnova", "Анна Смирнова", "Монтаж и цветокоррекция", "Moscow", "RU", 55.7558, 37.6173),
        new("ivan.petrov", "Иван Петров", "Снимаю на телефон и не стесняюсь", "Moscow", "RU", 55.7558, 37.6173, IsPrivate: true),
        new("lena.fischer", "Lena Fischer", "Berlin · experimental film", "Berlin", "DE", 52.52, 13.405),
        new("james.carter", "James Carter", "London film school, class of '27", "London", "GB", 51.5074, -0.1278),
        new("sophia.martin", "Sophia Martin", "Paris · AI-assisted animation", "Paris", "FR", 48.8566, 2.3522),
        new("olena.kovalenko", "Olena Kovalenko", "Kyiv · hand-drawn frames", "Kyiv", "UA", 50.4501, 30.5234),
        new("mike.johnson", "Mike Johnson", "NYC nights on 35mm", "New York", "US", 40.7128, -74.006),
        new("rashad.novruzov", "Rəşad Novruzov", "Bakı küçələri · gecə çəkilişləri", "Baku", "AZ", 40.4093, 49.8671),
    ];

    private static readonly (string Title, string Synopsis, ShortFilmOrigin Origin)[] Films =
    [
        ("Bulvarda Səhər", "Xəzər sahilində bir səhər, kəsilmədən çəkilmiş.", ShortFilmOrigin.HandCrafted),
        ("Neon Bakı", "AI ilə yaradılmış gələcəyin Bakısı.", ShortFilmOrigin.AiGenerated),
        ("Nənəmin Əlləri", "Xalça toxuyan əllər haqqında qısa sənədli.", ShortFilmOrigin.HandCrafted),
        ("Synthetic Dreams", "A model dreams of a city it has never seen.", ShortFilmOrigin.AiGenerated),
        ("Son Qatar", "Gecə yarısı son qatarı gözləyən iki yad.", ShortFilmOrigin.HandCrafted),
        ("Kağız Şəhər", "Kağızdan kəsilmiş fiqurlarla stop-motion.", ShortFilmOrigin.HandCrafted),
        ("Echoes of Steppe", "Generated landscapes scored with real dombra.", ShortFilmOrigin.AiGenerated),
        ("Pəncərə", "Bir pəncərədən görünən bir il.", ShortFilmOrigin.HandCrafted),
    ];

    private static readonly string[][] Talk =
    [
        ["Salam! Filminə baxdım, çox xoşuma gəldi 👏", "Çox sağ ol! Hansı səhnə daha çox təsir etdi?", "Qatar səhnəsi. İşıq çox gözəl idi.", "O kadrı üç gecə çəkdik 😅"],
        ["Bu həftə kinoteatra gedirik?", "Hə, cümə axşamı 19:00 seansı?", "Olar, yerləri mən tuturam.", "Əla, mənə orta cərgə 🙏"],
        ["Merhaba! AI ile yaptığın kısa film harikaydı.", "Teşekkürler! Hangi araçları merak ediyorsun?", "Özellikle ışık geçişlerini nasıl yaptın?", "Önce elle storyboard, sonra üretim. Yarın detaylı yazarım."],
        ["Привет! Можно взять твой монтаж как пример для курса?", "Конечно, только укажи автора 🙂", "Спасибо огромное!"],
        ["Hey, are you submitting to the festival this year?", "Thinking about it. Deadline is next month, right?", "Yes, the 15th. Let's review each other's cuts.", "Deal. Sending mine tonight."],
        ["Stop-motion üçün hansı kameranı istifadə edirsən?", "Köhnə Canon, amma əsas iş işıqdadır.", "Mənə bir-iki məsləhət verərsən?", "Əlbəttə, şənbə görüşək."],
        ["Profilini gizli etmisən? 🙂", "Hə, hələ bitməmiş işləri paylaşmaq istəmirəm.", "Anladım, sorğu göndərdim.", "Qəbul etdim 👍"],
        ["Gamarjoba! Your Tbilisi doc was beautiful.", "Madloba! It took two winters to film.", "Worth every day of it."],
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var media = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        if (await identity.Users.AnyAsync(u => u.Email.EndsWith(Marker)))
        {
            Console.WriteLine("Demo people already exist; run --remove-demo-people first to start over.");
            return;
        }

        var random = new Random(2026);
        var hash = hasher.Hash(Password);   // one slow hash, shared: they all use the same password
        var now = DateTime.UtcNow;

        var users = People.Select((p, i) => new AppUser
        {
            Email = p.Username + Marker,
            FullName = p.FullName,
            Username = p.Username,
            Bio = p.Bio,
            IsPrivate = p.IsPrivate,
            PasswordHash = hash,
            IsEmailConfirmed = true,
            ShareOnGlobe = true,
            City = p.City,
            CountryCode = p.Country,
            Latitude = p.Lat,
            Longitude = p.Lon,
            CreatedAtUtc = now.AddDays(-60 + i),
        }).ToList();
        identity.Users.AddRange(users);

        // Followers: everyone follows a handful of others. Following a private account leaves
        // about one in three as a request, so each private profile has some waiting.
        foreach (var follower in users)
        {
            foreach (var followee in users.Where(u => u != follower).OrderBy(_ => random.Next()).Take(random.Next(4, 12)))
            {
                identity.Follows.Add(new Follow
                {
                    FollowerId = follower.Id,
                    FolloweeId = followee.Id,
                    IsAccepted = !followee.IsPrivate || random.Next(3) > 0,
                    CreatedAtUtc = now.AddDays(-random.Next(1, 40)),
                });
            }
        }

        // The site's own test accounts get followers and a couple of messages, so signing in as
        // them shows something.
        var realOnes = await identity.Users.Where(u => !u.Email.EndsWith(Marker) && !u.IsSuspended)
            .Select(u => new { u.Id, u.FullName }).ToListAsync();
        foreach (var real in realOnes)
            foreach (var fan in users.OrderBy(_ => random.Next()).Take(random.Next(3, 8)))
                identity.Follows.Add(new Follow { FollowerId = fan.Id, FolloweeId = real.Id, CreatedAtUtc = now.AddDays(-random.Next(1, 20)) });

        var pairs = new List<(AppUser A, Guid B)>();
        for (var i = 0; i < users.Count; i++) pairs.Add((users[i], users[(i + 3) % users.Count].Id));
        foreach (var real in realOnes) pairs.Add((users[random.Next(users.Count)], real.Id));

        foreach (var (a, b) in pairs)
        {
            var lines = Talk[random.Next(Talk.Length)];
            var (low, high) = DirectThread.Pair(a.Id, b);
            var at = now.AddHours(-random.Next(2, 200));
            var thread = new DirectThread { LowUserId = low, HighUserId = high };
            // A real account gets just the opening line (unread), not a conversation it never had.
            var other = users.FirstOrDefault(u => u.Id == b);
            if (other is null) lines = lines[..1];
            for (var n = 0; n < lines.Length; n++)
            {
                var fromA = n % 2 == 0;   // the demo member opens; replies alternate
                at = at.AddMinutes(random.Next(1, 30));
                thread.Messages.Add(new DirectMessage
                {
                    SenderId = fromA ? a.Id : b,
                    SenderName = fromA ? a.FullName : other!.FullName,
                    Body = lines[n],
                    CreatedAtUtc = at,
                    SeenAtUtc = n < lines.Length - 1 ? at : null,
                });
            }
            thread.LastMessageAtUtc = at;
            identity.DirectThreads.Add(thread);
        }

        await identity.SaveChangesAsync();

        // Films: two short trailers copied once into the uploads folder and shared by every demo
        // film, so the grids have something that plays.
        var folder = Path.Combine(environment.ContentRootPath, "uploads", "shorts");
        Directory.CreateDirectory(folder);
        string[] clips = ["vertigo-1958.webm", "rear-window-1954.webm"];
        foreach (var clip in clips)
        {
            var target = Path.Combine(folder, "wydemo-" + clip);
            if (!File.Exists(target)) File.Copy(Path.Combine(environment.ContentRootPath, "trailers", clip), target);
        }

        foreach (var user in users)
        {
            for (var n = random.Next(1, 7); n > 0; n--)
            {
                var film = Films[random.Next(Films.Length)];
                var shared = random.Next(4) > 0;
                var clip = clips[random.Next(clips.Length)];
                var submitted = now.AddDays(-random.Next(1, 50));
                media.ShortFilms.Add(new ShortFilm
                {
                    UserId = user.Id,
                    AuthorName = user.FullName,
                    Title = film.Title,
                    Synopsis = film.Synopsis,
                    StoredFileName = "wydemo-" + clip,
                    OriginalFileName = clip,
                    SizeBytes = new FileInfo(Path.Combine(folder, "wydemo-" + clip)).Length,
                    ContentType = "video/webm",
                    Origin = film.Origin,
                    Visibility = shared ? ShortFilmVisibility.Public : ShortFilmVisibility.Private,
                    Status = SubmissionStatus.Approved,
                    ViewCount = random.Next(0, 900),
                    SubmittedAtUtc = submitted,
                    ApprovedAtUtc = submitted.AddDays(1),
                    ReviewedAtUtc = submitted.AddDays(1),
                });
            }
        }
        await media.SaveChangesAsync();

        Console.WriteLine($"Added {users.Count} demo people. Sign in as e.g. {users[0].Email} with the password in DemoPeople.cs.");
    }

    public static async Task RemoveAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var media = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var ids = await identity.Users.IgnoreQueryFilters().Where(u => u.Email.EndsWith(Marker)).Select(u => u.Id).ToListAsync();
        await media.ShortFilms.IgnoreQueryFilters().Where(f => ids.Contains(f.UserId)).ExecuteDeleteAsync();
        await identity.DirectMessages.Where(m => identity.DirectThreads.IgnoreQueryFilters()
            .Any(t => t.Id == m.ThreadId && (ids.Contains(t.LowUserId) || ids.Contains(t.HighUserId)))).ExecuteDeleteAsync();
        await identity.DirectThreads.IgnoreQueryFilters().Where(t => ids.Contains(t.LowUserId) || ids.Contains(t.HighUserId)).ExecuteDeleteAsync();
        await identity.Follows.Where(f => ids.Contains(f.FollowerId) || ids.Contains(f.FolloweeId)).ExecuteDeleteAsync();
        await identity.UserBlocks.Where(b => ids.Contains(b.BlockerId) || ids.Contains(b.BlockedId)).ExecuteDeleteAsync();
        await identity.RefreshTokens.IgnoreQueryFilters().Where(r => ids.Contains(r.UserId)).ExecuteDeleteAsync();
        await identity.VerificationCodes.IgnoreQueryFilters().Where(v => ids.Contains(v.UserId)).ExecuteDeleteAsync();
        await identity.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
        Console.WriteLine($"Removed {ids.Count} demo people.");
    }
}
