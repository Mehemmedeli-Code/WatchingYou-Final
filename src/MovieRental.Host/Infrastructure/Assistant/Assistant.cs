using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MovieRental.Host.Infrastructure.Localization;

namespace MovieRental.Host.Infrastructure.Assistant;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>"Builtin", "OpenAI" or "Anthropic". The last two need an API key in user
    /// secrets; the first needs nothing.</summary>
    public string Provider { get; set; } = "Builtin";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Left empty, each provider's sensible default is used — gpt-4o-mini for
    /// OpenAI, claude-sonnet-4-6 for Anthropic.</summary>
    public string Model { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 600;
}

public sealed record AssistantTurn(string Role, string Content);

public sealed record AssistantReply(string Content, bool FromModel);

public interface IAssistant
{
    Task<AssistantReply> AskAsync(
        IReadOnlyList<AssistantTurn> history, string language, string? visitorName, CancellationToken ct);
}

/// <summary>
/// Answers from a small FAQ about this site, in the visitor's language.
///
/// It is not a language model and does not pretend to be one: it matches keywords and returns
/// a written answer. That is a real limit, and the interface says so rather than inventing a
/// confident reply to a question it did not understand. It also costs nothing and needs no
/// credentials, which is why it is the default — a support box that is broken until somebody
/// pastes an API key is worse than one that answers the twelve questions people actually ask.
/// </summary>
internal sealed class BuiltinAssistant : IAssistant
{
    private sealed record Topic(string[] Keywords, Dictionary<string, string> Answer);

    private static readonly Topic[] Topics =
    [
        new(["rent", "icarə", "icare", "kirala", "аренд", "прокат"], new()
        {
            ["az"] = "Kataloqda filmi tap və **İcarəyə götür** düyməsinə bas — 7 gün sənin olur. İcarələrin **İcarələrim** səhifəsindədir; oradan 7 gün uzada və ya qaytara bilərsən.",
            ["en"] = "Find the film in the catalogue and press **Rent** — it is yours for seven days. Everything you have out is under **My rentals**, where you can extend by a week or return it.",
            ["ru"] = "Найдите фильм в каталоге и нажмите **Взять** — он ваш на семь дней. Все аренды находятся в разделе **Мои аренды**, там же можно продлить или вернуть.",
            ["tr"] = "Katalogda filmi bulun ve **Kirala**'ya basın — yedi gün sizin olur. Kiraladıklarınız **Kiralamalarım** sayfasında; oradan uzatabilir veya iade edebilirsiniz.",
        }),
        new(["watch", "izlə", "izle", "смотр", "video"], new()
        {
            ["az"] = "Film kartındaki **İzlə** düyməsi videonu açır. Düymə sönülüdürsə, həmin filmə hələ video linki əlavə olunmayıb — **Ətraflı** ilə təsvirə və treylerə baxa bilərsən.",
            ["en"] = "The **Watch** button on a film card opens the player. If it is greyed out, no video has been added for that title yet — **Show details** still gives you the synopsis and trailer.",
            ["ru"] = "Кнопка **Смотреть** на карточке открывает плеер. Если она неактивна, видео для фильма ещё не добавлено — в **Подробнее** есть описание и трейлер.",
            ["tr"] = "Film kartındaki **İzle** düğmesi oynatıcıyı açar. Pasifse, o filme henüz video eklenmemiştir — **Ayrıntılar** özeti ve fragmanı gösterir.",
        }),
        new(["seat", "cinema", "yer", "kinoteatr", "bilet", "ticket", "мест", "билет", "koltuk"], new()
        {
            ["az"] = "**Kinoteatr yerləri** səhifəsində seansı seç, yerləri işarələ, sonra **Yerləri təsdiqlə**. Kart məlumatını yazırsan, e-poçtuna altı rəqəmli kod gəlir, kodu yazdıqdan sonra hər yer üçün ayrıca QR kodlu bilet çıxır.",
            ["en"] = "On **Cinema seats**, pick a screening, choose your seats and press **Confirm seats**. You enter card details, a six-digit code arrives by e-mail, and confirming it produces a ticket with one QR code per seat.",
            ["ru"] = "На странице **Места в зале** выберите сеанс и места, затем **Подтвердить места**. Введите данные карты, на почту придёт шестизначный код, после подтверждения вы получите билет с QR-кодом для каждого места.",
            ["tr"] = "**Sinema koltukları** sayfasında seansı ve koltukları seçip **Koltukları onayla**'ya basın. Kart bilgisini girersiniz, e-postanıza altı haneli kod gelir, onayladıktan sonra her koltuk için QR kodlu bilet oluşur.",
        }),
        new(["qr", "check", "giriş", "girish", "door", "скан", "scan"], new()
        {
            ["az"] = "Hər yerin öz QR kodu var — girişdə müvafiq kodu göstər. Biletlərin **Kinoteatr yerləri** səhifəsinin altında saxlanılır, səhifəni yeniləsən də itmir.",
            ["en"] = "Each seat has its own QR code — show the matching one at the door. Your tickets are kept at the bottom of the **Cinema seats** page and survive a refresh.",
            ["ru"] = "У каждого места свой QR-код — покажите нужный на входе. Билеты хранятся внизу страницы **Места в зале** и не исчезают при обновлении.",
            ["tr"] = "Her koltuğun kendi QR kodu var — girişte ilgilisini gösterin. Biletleriniz **Sinema koltukları** sayfasının altında durur, sayfa yenilense de kaybolmaz.",
        }),
        new(["3d", "görünüş", "gorunus", "view from", "вид", "görünüm"], new()
        {
            ["az"] = "Yer seçib **Bu yerdən bax** düyməsinə bas — zal 3D-də açılır və kamera həmin yerdə olur. Bir neçə yer seçsən, oxlarla aralarında keçə bilərsən.",
            ["en"] = "Pick a seat and press **View from here** — the hall opens in 3D with the camera in that seat. Choose several seats and the arrows step between them.",
            ["ru"] = "Выберите место и нажмите **Вид отсюда** — зал откроется в 3D с камерой на этом месте. Если выбрано несколько мест, переключайтесь стрелками.",
            ["tr"] = "Bir koltuk seçip **Buradan gör**'e basın — salon 3D açılır ve kamera o koltukta olur. Birden fazla koltukta oklarla geçiş yapabilirsiniz.",
        }),
        new(["upload", "short", "studio", "qısametraj", "qisametraj", "загруз", "yükle", "kısa"], new()
        {
            ["az"] = "**Studiya** səhifəsindən qısametrajlı film göndər. Əvvəlcə Təhlükəsizlik yoxlayır, sonra Admin qərar verir — hər şey üç gün ərzində. Nəticə e-poçtuna gəlir, gedişi Studiyada izləyə bilərsən.",
            ["en"] = "Send a short from the **Studio** page. Security inspects it first, then an admin decides — all within three days. The verdict arrives by e-mail and you can follow its progress in Studio.",
            ["ru"] = "Отправьте короткометражку на странице **Студия**. Сначала проверяет служба безопасности, затем решает администратор — всё в течение трёх дней. Результат придёт на почту.",
            ["tr"] = "**Stüdyo** sayfasından kısa film gönderin. Önce Güvenlik inceler, sonra yönetici karar verir — hepsi üç gün içinde. Sonuç e-postayla gelir.",
        }),
        new(["password", "şifrə", "sifre", "пароль", "forgot", "unut", "reset"], new()
        {
            ["az"] = "Giriş səhifəsində **Şifrəni unutmusan?** düyməsinə bas. E-poçtunu yaz, kod göndərək, sonra yeni şifrəni təyin et. Bərpadan sonra bütün açıq sessiyalar bağlanır.",
            ["en"] = "Press **Forgot your password?** on the sign-in form. Enter your e-mail, we send a code, then you set a new password. Completing a reset signs out every existing session.",
            ["ru"] = "Нажмите **Забыли пароль?** на форме входа. Укажите e-mail, мы вышлем код, затем задайте новый пароль. После сброса все сессии завершаются.",
            ["tr"] = "Giriş formunda **Şifrenizi mi unuttunuz?**'a basın. E-postanızı girin, kod gönderelim, sonra yeni şifreyi belirleyin. Sıfırlama tüm oturumları kapatır.",
        }),
        new(["code", "kod", "verification", "təsdiq", "tesdiq", "код", "doğrulama"], new()
        {
            ["az"] = "Kodlar altı rəqəmlidir, 10 dəqiqə etibarlıdır və beş səhv cəhddən sonra yanır. Gəlmirsə, spam qovluğuna bax və bir dəqiqə sonra yenidən istə.",
            ["en"] = "Codes are six digits, valid for ten minutes, and burn after five wrong attempts. If one does not arrive, check your spam folder and request another after a minute.",
            ["ru"] = "Коды шестизначные, действуют десять минут и сгорают после пяти неверных попыток. Если код не пришёл, проверьте спам и запросите новый через минуту.",
            ["tr"] = "Kodlar altı hanelidir, on dakika geçerlidir ve beş yanlış denemeden sonra geçersiz olur. Gelmezse spam klasörüne bakın ve bir dakika sonra tekrar isteyin.",
        }),
        new(["review", "rəy", "rey", "rating", "отзыв", "yorum", "puan"], new()
        {
            ["az"] = "Rəy yazmaq üçün həmin filmi əvvəlcə icarəyə götürməlisən. Keçmiş icarələr də sayılır — qaytarmısansa, rəy yazmaq hüququn qalır.",
            ["en"] = "You need to have rented a film before reviewing it. Past rentals count — returning it does not cost you your say.",
            ["ru"] = "Чтобы оставить отзыв, нужно было взять фильм в аренду. Прошлые аренды тоже считаются.",
            ["tr"] = "Yorum yazmak için filmi kiralamış olmanız gerekir. Geçmiş kiralamalar da sayılır.",
        }),
        new(["language", "dil", "язык", "translate"], new()
        {
            ["az"] = "Sağ yuxarıda **AZ / EN / RU / TR** düymələri var. Seçimin brauzerdə saxlanılır və bütün səhifələrdə qalır.",
            ["en"] = "The **AZ / EN / RU / TR** buttons are at the top right. Your choice is remembered and applies to every page.",
            ["ru"] = "Кнопки **AZ / EN / RU / TR** вверху справа. Выбор запоминается и действует на всех страницах.",
            ["tr"] = "**AZ / EN / RU / TR** düğmeleri sağ üstte. Seçiminiz hatırlanır ve tüm sayfalarda geçerlidir.",
        }),
        new(["globe", "dünya", "dunya", "map", "xəritə", "xerite", "карт", "harita", "küre"], new()
        {
            ["az"] = "**Dünya** səhifəsində xəritədə görünməyi seçən istifadəçilər var. Yalnız şəhər göstərilir, dəqiq ünvan yox, və istədiyin vaxt söndürə bilərsən. Birinin adının yanındaki düymə film zövqünüzün nə qədər uyğun olduğunu göstərir.",
            ["en"] = "The **Globe** page shows members who chose to appear. City level only, never a street, and you can switch it off at any time. The button beside a name compares your film tastes.",
            ["ru"] = "На странице **Глобус** видны участники, выбравшие появиться. Только город, не улица, и отключить можно в любой момент. Кнопка рядом с именем сравнивает вкусы.",
            ["tr"] = "**Küre** sayfasında görünmeyi seçen üyeler var. Yalnızca şehir düzeyinde, adres değil, istediğiniz zaman kapatabilirsiniz. İsmin yanındaki düğme film zevklerinizi karşılaştırır.",
        }),
    ];

    private static readonly Dictionary<string, string> Fallback = new()
    {
        ["az"] = "Bunu dəqiq bilmirəm. Mən saytın özü haqqında suallara cavab verirəm — icarə, kinoteatr biletləri, qısametraj göndərmə, hesab və dil məsələləri. Sualını başqa sözlərlə yazsan, bəlkə tapım.",
        ["en"] = "I do not know that one. I answer questions about the site itself — renting, cinema tickets, submitting a short film, accounts and languages. Try rephrasing and I may find it.",
        ["ru"] = "Этого я не знаю. Я отвечаю на вопросы о самом сайте — аренда, билеты в кино, отправка короткометражки, аккаунт и языки. Попробуйте переформулировать.",
        ["tr"] = "Bunu bilmiyorum. Sitenin kendisiyle ilgili soruları yanıtlıyorum — kiralama, sinema bileti, kısa film gönderme, hesap ve diller. Farklı sözcüklerle sorarsanız bulabilirim.",
    };

    public Task<AssistantReply> AskAsync(
        IReadOnlyList<AssistantTurn> history, string language, string? visitorName, CancellationToken ct)
    {
        var question = history.LastOrDefault(t => t.Role == "user")?.Content?.ToLowerInvariant() ?? "";
        var lang = Translations.Normalise(language);

        var best = Topics
            .Select(topic => (topic, score: topic.Keywords.Count(k => question.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Select(x => x.topic)
            .FirstOrDefault();

        var answer = best is null
            ? Fallback.GetValueOrDefault(lang, Fallback["en"])
            : best.Answer.GetValueOrDefault(lang, best.Answer["en"]);

        return Task.FromResult(new AssistantReply(answer, FromModel: false));
    }
}

internal static class AssistantPrompt
{
    public const string Base = """
        You are WatchingYou AI, the support assistant for a film rental and cinema booking site.
        Answer only questions about this site and about films. Keep replies to a few sentences.
        Reply in the same language the visitor used.

        What the site does: rent films for seven days; watch titles that have a video; book
        cinema seats across four venues with a card, an e-mailed six-digit code and a QR ticket
        per seat; preview the view from any seat in 3D; submit short films that Security
        inspects and an admin approves within three days; browse AI Catalog and Human Craft
        galleries; appear on an opt-in globe and compare film tastes with other members.

        You cannot see the visitor's account, rentals or bookings, and you cannot change
        anything. If asked to do something, explain where on the site they can do it. If you do
        not know, say so plainly rather than guessing.
        """;

    /// <summary>
    /// The visitor's own name, so the assistant is talking to them rather than to nobody.
    /// Only the display name goes in — never the e-mail address, which the model has no use
    /// for and which would end up in a third party's logs for nothing.
    /// </summary>
    public static string For(string? visitorName) =>
        string.IsNullOrWhiteSpace(visitorName)
            ? Base
            : $"{Base}\n\nThe visitor is signed in and their name is {visitorName}. Address them by it when it reads naturally.";
}

/// <summary>
/// Forwards the conversation to OpenAI's chat completions API. Used only when a key is
/// configured, and the key lives in user secrets — never in appsettings.json.
/// </summary>
internal sealed class OpenAiAssistant(
    IHttpClientFactory factory, IOptions<AssistantOptions> options, IAssistant fallback, ILogger<OpenAiAssistant> logger)
    : IAssistant
{
    private readonly AssistantOptions _options = options.Value;

    public async Task<AssistantReply> AskAsync(
        IReadOnlyList<AssistantTurn> history, string language, string? visitorName, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(nameof(OpenAiAssistant));
            client.Timeout = TimeSpan.FromSeconds(30);

            // OpenAI takes the system prompt as the first message rather than a separate
            // field, which is the only real shape difference from Anthropic here.
            var messages = new List<object> { new { role = "system", content = AssistantPrompt.For(visitorName) } };
            messages.AddRange(history.Select(turn => new { role = turn.Role, content = turn.Content }));

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model,
                    max_tokens = _options.MaxTokens,
                    messages
                })
            };
            request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI call failed: {Status} {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync(ct));
                return await fallback.AskAsync(history, language, visitorName, ct);
            }

            var body = await response.Content.ReadFromJsonAsync<OpenAiResponse>(cancellationToken: ct);
            var text = body?.Choices?.FirstOrDefault()?.Message?.Content;

            return string.IsNullOrWhiteSpace(text)
                ? await fallback.AskAsync(history, language, visitorName, ct)
                : new AssistantReply(text.Trim(), FromModel: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI call threw; falling back to the written answers.");
            return await fallback.AskAsync(history, language, visitorName, ct);
        }
    }

    private sealed record OpenAiResponse([property: JsonPropertyName("choices")] Choice[]? Choices);
    private sealed record Choice([property: JsonPropertyName("message")] ChoiceMessage? Message);
    private sealed record ChoiceMessage([property: JsonPropertyName("content")] string? Content);
}

/// <summary>
/// Forwards the conversation to Anthropic's API. Used only when a key is configured, and the
/// key lives in user secrets — never in appsettings.json, which goes into source control.
/// </summary>
internal sealed class AnthropicAssistant(
    IHttpClientFactory factory, IOptions<AssistantOptions> options, IAssistant fallback, ILogger<AnthropicAssistant> logger)
    : IAssistant
{
    private readonly AssistantOptions _options = options.Value;

    public async Task<AssistantReply> AskAsync(
        IReadOnlyList<AssistantTurn> history, string language, string? visitorName, CancellationToken ct)
    {
        try
        {
            var client = factory.CreateClient(nameof(AnthropicAssistant));
            client.Timeout = TimeSpan.FromSeconds(30);

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(_options.Model) ? "claude-sonnet-4-6" : _options.Model,
                max_tokens = _options.MaxTokens,
                system = AssistantPrompt.For(visitorName),
                messages = history.Select(turn => new { role = turn.Role, content = turn.Content }).ToArray()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("x-api-key", _options.ApiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Assistant call failed: {Status}", response.StatusCode);
                return await fallback.AskAsync(history, language, visitorName, ct);
            }

            var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(cancellationToken: ct);
            var text = string.Concat(body?.Content?.Where(b => b.Type == "text").Select(b => b.Text) ?? []);

            // An empty answer is not an answer; better the written one than a blank bubble.
            return string.IsNullOrWhiteSpace(text)
                ? await fallback.AskAsync(history, language, visitorName, ct)
                : new AssistantReply(text.Trim(), FromModel: true);
        }
        catch (Exception ex)
        {
            // The support box must never take the page down with it.
            logger.LogError(ex, "Assistant call threw; falling back to the built-in answers.");
            return await fallback.AskAsync(history, language, visitorName, ct);
        }
    }

    private sealed record AnthropicResponse([property: JsonPropertyName("content")] ContentBlock[]? Content);

    private sealed record ContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text);
}
