/**
 * The server writes its error messages in English. This puts them into the page's language
 * before anyone sees them: an exact sentence, or a template where {0}, {1} stand for the parts
 * that change ("Wait {0} more seconds…"). Anything not listed is shown as the server sent it.
 *
 * Each entry: English → [Azerbaijani, Russian, Turkish].
 */
type Row = [az: string, ru: string, tr: string];

const MESSAGES: Record<string, Row> = {
  // Account
  "E-mail or password is incorrect.": ["E-poçt və ya şifrə yanlışdır.", "Неверный e-mail или пароль.", "E-posta veya şifre yanlış."],
  "That e-mail is already registered.": ["Bu e-poçt artıq qeydiyyatdan keçib.", "Этот e-mail уже зарегистрирован.", "Bu e-posta zaten kayıtlı."],
  "That password is not correct.": ["Şifrə düzgün deyil.", "Неверный пароль.", "Şifre doğru değil."],
  "Password needs at least one capital letter.": ["Şifrədə ən azı bir böyük hərf olmalıdır.", "Пароль должен содержать хотя бы одну заглавную букву.", "Şifrede en az bir büyük harf olmalı."],
  "Password needs at least one digit.": ["Şifrədə ən azı bir rəqəm olmalıdır.", "Пароль должен содержать хотя бы одну цифру.", "Şifrede en az bir rakam olmalı."],
  "That code has expired or been used up. Ask for a new one.": ["Kodun vaxtı bitib və ya istifadə olunub. Yeni kod istəyin.", "Код истёк или уже использован. Запросите новый.", "Kodun süresi doldu veya kullanıldı. Yeni kod isteyin."],
  "Too many wrong attempts. Request a new code.": ["Çox sayda yanlış cəhd. Yeni kod istəyin.", "Слишком много неверных попыток. Запросите новый код.", "Çok fazla hatalı deneme. Yeni kod isteyin."],
  "Incorrect code. {0} attempts left.": ["Kod yanlışdır. {0} cəhd qalıb.", "Неверный код. Осталось попыток: {0}.", "Kod yanlış. {0} deneme hakkı kaldı."],
  "Wait {0} more seconds before asking for another code.": ["Yeni kod istəmək üçün {0} saniyə gözləyin.", "Подождите ещё {0} сек., прежде чем запросить новый код.", "Yeni kod istemek için {0} saniye bekleyin."],
  "This e-mail is already confirmed.": ["Bu e-poçt artıq təsdiqlənib.", "Этот e-mail уже подтверждён.", "Bu e-posta zaten onaylandı."],
  "This phone number is already confirmed.": ["Bu telefon nömrəsi artıq təsdiqlənib.", "Этот номер уже подтверждён.", "Bu telefon numarası zaten onaylandı."],
  "Add a phone number to your profile first.": ["Əvvəlcə profilinizə telefon nömrəsi əlavə edin.", "Сначала добавьте номер телефона в профиль.", "Önce profilinize telefon numarası ekleyin."],
  "Use digits only, optionally starting with +.": ["Yalnız rəqəm yazın, əvvəlində + ola bilər.", "Только цифры, можно начать с +.", "Yalnızca rakam kullanın, başında + olabilir."],
  "This session was ended. Sign in again.": ["Sessiya bitib. Yenidən daxil olun.", "Сеанс завершён. Войдите снова.", "Oturum sona erdi. Tekrar giriş yapın."],
  "Refresh token has expired.": ["Sessiyanın vaxtı bitib. Yenidən daxil olun.", "Срок сеанса истёк. Войдите снова.", "Oturum süresi doldu. Tekrar giriş yapın."],
  "Your account has no e-mail address.": ["Hesabınızda e-poçt ünvanı yoxdur.", "В аккаунте нет адреса e-mail.", "Hesabınızda e-posta adresi yok."],
  "An administrator account cannot be deleted from here.": ["Admin hesabı buradan silinə bilməz.", "Аккаунт администратора нельзя удалить отсюда.", "Yönetici hesabı buradan silinemez."],
  "You cannot remove your own Admin role.": ["Öz Admin rolunuzu silə bilməzsiniz.", "Нельзя снять с себя роль администратора.", "Kendi Admin rolünüzü kaldıramazsınız."],
  "You cannot suspend yourself.": ["Özünüzü dayandıra bilməzsiniz.", "Нельзя заблокировать самого себя.", "Kendinizi askıya alamazsınız."],
  "This is the last active admin.": ["Bu, sonuncu aktiv admindir.", "Это последний активный администратор.", "Bu, son aktif yöneticidir."],
  "This is the last admin. Promote someone else first.": ["Bu, sonuncu admindir. Əvvəlcə başqasını admin edin.", "Это последний администратор. Сначала назначьте другого.", "Bu son yönetici. Önce başka birini yönetici yapın."],
  "Unknown role(s): {0}.": ["Naməlum rol: {0}.", "Неизвестная роль: {0}.", "Bilinmeyen rol: {0}."],

  // Catalogue, rentals, PRO
  "Every copy is currently out. Try again later.": ["Hazırda bütün nüsxələr icarədədir. Bir az sonra yenidən cəhd edin.", "Все копии сейчас на руках. Попробуйте позже.", "Şu anda tüm kopyalar kirada. Daha sonra tekrar deneyin."],
  "You already have this title out on rental.": ["Bu film artıq sizdə icarədədir.", "Этот фильм уже у вас в аренде.", "Bu film zaten sizde kirada."],
  "This rental is already closed.": ["Bu icarə artıq bağlanıb.", "Эта аренда уже закрыта.", "Bu kiralama zaten kapandı."],
  "This rental was already returned.": ["Bu film artıq qaytarılıb.", "Этот фильм уже возвращён.", "Bu film zaten iade edildi."],
  "You can add three more days once the current three are over.": ["Əlavə 3 günü cari 3 gün bitəndən sonra götürə bilərsiniz.", "Продлить на 3 дня можно, когда текущие 3 дня закончатся.", "Ek 3 günü mevcut 3 gün bittikten sonra alabilirsiniz."],
  "Watching PRO already includes every film — just press Watch.": ["Watching PRO bütün filmləri əhatə edir — sadəcə «İzlə» düyməsinə basın.", "Watching PRO уже включает все фильмы — просто нажмите «Смотреть».", "Watching PRO tüm filmleri kapsıyor — sadece «İzle»ye basın."],
  "Rent this film before reviewing it.": ["Rəy yazmaq üçün əvvəlcə filmi icarəyə götürün.", "Чтобы оставить отзыв, сначала возьмите фильм в аренду.", "Yorum yazmak için önce filmi kiralayın."],
  "That title and year are already in the catalogue.": ["Bu ad və il ilə film artıq kataloqdadır.", "Фильм с таким названием и годом уже есть в каталоге.", "Bu ad ve yılla film zaten katalogda."],
  "{0} copies are out on rental. Stock cannot go below that.": ["{0} nüsxə icarədədir. Stok bundan az ola bilməz.", "{0} копий в аренде. Запас не может быть меньше.", "{0} kopya kirada. Stok bundan az olamaz."],
  "Your list is full ({0} films). Remove one first.": ["Siyahınız doludur ({0} film). Əvvəlcə birini silin.", "Список заполнен ({0} фильмов). Сначала удалите один.", "Listeniz dolu ({0} film). Önce birini çıkarın."],
  "Only Visa and Mastercard are accepted.": ["Yalnız Visa və Mastercard qəbul olunur.", "Принимаются только Visa и Mastercard.", "Yalnızca Visa ve Mastercard kabul edilir."],
  "The card number is not valid.": ["Kart nömrəsi yanlışdır.", "Неверный номер карты.", "Kart numarası geçersiz."],
  "That card number is not valid.": ["Kart nömrəsi yanlışdır.", "Неверный номер карты.", "Kart numarası geçersiz."],
  "The card has expired.": ["Kartın müddəti bitib.", "Срок действия карты истёк.", "Kartın süresi dolmuş."],
  "That expiry date has passed.": ["Kartın müddəti bitib.", "Срок действия карты истёк.", "Kartın süresi dolmuş."],
  "The CVC is three digits.": ["CVC üç rəqəmdən ibarətdir.", "CVC — три цифры.", "CVC üç haneli olmalı."],
  "The security code should be three or four digits.": ["Təhlükəsizlik kodu üç və ya dörd rəqəm olmalıdır.", "Код безопасности — три или четыре цифры.", "Güvenlik kodu üç veya dört haneli olmalı."],
  "Enter the name on the card.": ["Kartın üzərindəki adı yazın.", "Введите имя владельца карты.", "Kart üzerindeki adı girin."],

  // Cinema
  "Pick at least one seat.": ["Ən azı bir yer seçin.", "Выберите хотя бы одно место.", "En az bir koltuk seçin."],
  "Eight seats is the limit per booking.": ["Bir sifarişdə ən çox 8 yer ola bilər.", "Не более 8 мест в одном бронировании.", "Bir rezervasyonda en fazla 8 koltuk olabilir."],
  "Twenty seats is the most in one sale.": ["Bir satışda ən çox 20 yer ola bilər.", "Не более 20 мест за одну продажу.", "Bir satışta en fazla 20 koltuk olabilir."],
  "The same seat is in the basket twice.": ["Eyni yer səbətdə iki dəfədir.", "Одно и то же место в корзине дважды.", "Aynı koltuk sepette iki kez var."],
  "One of those seats was just sold. Refresh the map and pick again.": ["Seçdiyiniz yerlərdən biri indicə satıldı. Xəritəni yeniləyib yenidən seçin.", "Одно из мест только что продано. Обновите схему и выберите снова.", "Seçtiğiniz koltuklardan biri az önce satıldı. Haritayı yenileyip tekrar seçin."],
  "One of those seats was just taken. Reload the map and try again.": ["Seçdiyiniz yerlərdən biri indicə tutuldu. Xəritəni yeniləyib yenidən cəhd edin.", "Одно из мест только что заняли. Обновите схему и попробуйте снова.", "Seçtiğiniz koltuklardan biri az önce alındı. Haritayı yenileyip tekrar deneyin."],
  "One of those seats was taken while you were paying. Reload the map and try again.": ["Ödəniş zamanı yerlərdən biri tutuldu. Xəritəni yeniləyib yenidən cəhd edin.", "Пока вы оплачивали, одно из мест заняли. Обновите схему и попробуйте снова.", "Ödeme sırasında koltuklardan biri alındı. Haritayı yenileyip tekrar deneyin."],
  "Seat {0}-{1} is not in this hall.": ["{0}-{1} yeri bu zalda yoxdur.", "Места {0}-{1} нет в этом зале.", "{0}-{1} koltuğu bu salonda yok."],
  "This screening has already started.": ["Bu seans artıq başlayıb.", "Этот сеанс уже начался.", "Bu seans zaten başladı."],
  "This screening has been cancelled.": ["Bu seans ləğv edilib.", "Этот сеанс отменён.", "Bu seans iptal edildi."],
  "This screening is already cancelled.": ["Bu seans artıq ləğv edilib.", "Этот сеанс уже отменён.", "Bu seans zaten iptal edildi."],
  "This screening is no longer on sale.": ["Bu seansa bilet satışı dayandırılıb.", "Продажа на этот сеанс закрыта.", "Bu seansın satışı kapandı."],
  "Sales for this screening have closed.": ["Bu seansa bilet satışı bağlanıb.", "Продажа на этот сеанс закрыта.", "Bu seansın satışı kapandı."],
  "This booking expired. The seats are back on sale.": ["Sifarişin vaxtı bitdi. Yerlər yenidən satışdadır.", "Бронирование истекло. Места снова в продаже.", "Rezervasyonun süresi doldu. Koltuklar tekrar satışta."],
  "This booking is already confirmed.": ["Bu sifariş artıq təsdiqlənib.", "Это бронирование уже подтверждено.", "Bu rezervasyon zaten onaylandı."],
  "This booking belongs to someone else.": ["Bu sifariş başqa hesaba aiddir.", "Это бронирование принадлежит другому пользователю.", "Bu rezervasyon başka bir hesaba ait."],
  "Too many wrong codes. The seats have been released.": ["Çox sayda yanlış kod. Yerlər azad edildi.", "Слишком много неверных кодов. Места освобождены.", "Çok fazla hatalı kod. Koltuklar serbest bırakıldı."],
  "One of the ticket types is not on sale.": ["Bilet növlərindən biri satışda deyil.", "Один из типов билетов не продаётся.", "Bilet türlerinden biri satışta değil."],
  "Stripe is not set up on this server.": ["Bu serverdə Stripe ödənişi qurulmayıb.", "Stripe на этом сервере не настроен.", "Bu sunucuda Stripe kurulu değil."],
  "Stripe has not received the payment yet.": ["Stripe ödənişi hələ almayıb.", "Stripe ещё не получил оплату.", "Stripe ödemeyi henüz almadı."],

  // Box office and bar
  "Open a cash shift before selling.": ["Satışdan əvvəl kassa növbəsini açın.", "Откройте кассовую смену перед продажей.", "Satıştan önce kasa vardiyasını açın."],
  "You already have an open shift.": ["Sizin artıq açıq növbəniz var.", "У вас уже есть открытая смена.", "Zaten açık bir vardiyanız var."],
  "You already have an open shift. Close it first.": ["Sizin artıq açıq növbəniz var. Əvvəlcə onu bağlayın.", "У вас уже есть открытая смена. Сначала закройте её.", "Zaten açık bir vardiyanız var. Önce onu kapatın."],
  "The basket is empty.": ["Səbət boşdur.", "Корзина пуста.", "Sepet boş."],
  "One of those items is not on the menu.": ["Məhsullardan biri menyuda yoxdur.", "Одного из товаров нет в меню.", "Ürünlerden biri menüde yok."],
  "Not enough {0} in stock.": ["Anbarda kifayət qədər «{0}» yoxdur.", "Недостаточно «{0}» на складе.", "Stokta yeterli «{0}» yok."],
  "Quantities run from 1 to 50.": ["Miqdar 1 ilə 50 arasında olmalıdır.", "Количество — от 1 до 50.", "Miktar 1 ile 50 arasında olmalı."],
  "Cash received is short of {0}.": ["Alınan nağd pul {0}-dan azdır.", "Полученных наличных меньше {0}.", "Alınan nakit {0} tutarından az."],
  "Restock by a positive quantity.": ["Stoku müsbət miqdarla artırın.", "Пополняйте запас положительным количеством.", "Stoğu pozitif miktarla artırın."],
  "The float is between 0 and 10 000.": ["Kassa qalığı 0 ilə 10 000 arasında olmalıdır.", "Размен — от 0 до 10 000.", "Kasa bozuk parası 0 ile 10 000 arasında olmalı."],

  // Messages, globe, help
  "This person is not accepting messages.": ["Bu istifadəçi mesaj qəbul etmir.", "Этот пользователь не принимает сообщения.", "Bu kişi mesaj kabul etmiyor."],
  "You cannot message yourself.": ["Özünüzə mesaj yaza bilməzsiniz.", "Нельзя написать самому себе.", "Kendinize mesaj gönderemezsiniz."],
  "You cannot block yourself.": ["Özünüzü bloklaya bilməzsiniz.", "Нельзя заблокировать самого себя.", "Kendinizi engelleyemezsiniz."],
  "Write something first.": ["Əvvəlcə nəsə yazın.", "Сначала напишите что-нибудь.", "Önce bir şey yazın."],
  "Nothing to report.": ["Şikayət ediləcək mətn yoxdur.", "Не на что жаловаться.", "Bildirilecek bir şey yok."],
  "Choose a city before appearing on the globe.": ["Xəritədə görünmək üçün əvvəlcə şəhər seçin.", "Выберите город, чтобы появиться на карте.", "Haritada görünmek için önce şehir seçin."],
  "Country code must be two letters.": ["Ölkə kodu iki hərfdən ibarət olmalıdır.", "Код страны — две буквы.", "Ülke kodu iki harf olmalı."],

  // Studio (short films)
  "Give your film a title.": ["Filminizə ad verin.", "Дайте фильму название.", "Filminize bir ad verin."],
  "No file was attached.": ["Fayl əlavə edilməyib.", "Файл не прикреплён.", "Dosya eklenmedi."],
  "Upload a file between 1 byte and 512 MB.": ["1 bayt ilə 512 MB arasında fayl yükləyin.", "Загрузите файл от 1 байта до 512 МБ.", "1 bayt ile 512 MB arasında bir dosya yükleyin."],
  "Supported formats: {0}.": ["Dəstəklənən formatlar: {0}.", "Поддерживаемые форматы: {0}.", "Desteklenen biçimler: {0}."],
  "Say whether the film is AI-generated or hand-crafted.": ["Filmin süni intellektlə, yoxsa əl ilə hazırlandığını qeyd edin.", "Укажите, создан фильм ИИ или вручную.", "Filmin yapay zekâyla mı, elle mi yapıldığını belirtin."],
  "This is not your submission.": ["Bu film sizə aid deyil.", "Это не ваша работа.", "Bu gönderi size ait değil."],
  "You cannot post on this submission.": ["Bu filmə şərh yaza bilməzsiniz.", "Вы не можете комментировать эту работу.", "Bu gönderiye yorum yazamazsınız."],
  "This submission has already been decided.": ["Bu film barədə artıq qərar verilib.", "По этой работе уже принято решение.", "Bu gönderi hakkında zaten karar verildi."],
  "Security has not filed a report for this submission yet.": ["Təhlükəsizlik bu film üçün hələ hesabat təqdim etməyib.", "Служба безопасности ещё не подала отчёт по этой работе.", "Güvenlik bu gönderi için henüz rapor vermedi."],
  "A security report already exists for this submission.": ["Bu film üçün təhlükəsizlik hesabatı artıq var.", "Отчёт безопасности по этой работе уже есть.", "Bu gönderi için güvenlik raporu zaten var."],
  "Confirm you watched the film end to end before filing.": ["Hesabatdan əvvəl filmi sonuna qədər izlədiyinizi təsdiqləyin.", "Подтвердите, что посмотрели фильм целиком.", "Raporlamadan önce filmi sonuna kadar izlediğinizi onaylayın."],
  "Security flagged this film. Record why you are approving it anyway.": ["Təhlükəsizlik bu filmi işarələyib. Buna baxmayaraq niyə təsdiq etdiyinizi yazın.", "Служба безопасности отметила фильм. Укажите, почему вы всё же одобряете его.", "Güvenlik bu filmi işaretledi. Yine de neden onayladığınızı yazın."],

  // Schedule and pricing (manager)
  "Add at least one start time.": ["Ən azı bir başlama vaxtı əlavə edin.", "Добавьте хотя бы одно время начала.", "En az bir başlama saati ekleyin."],
  "Times are written HH:mm, e.g. 19:30.": ["Vaxt SS:dd formatında yazılır, məs. 19:30.", "Время в формате ЧЧ:мм, например 19:30.", "Saat SS:dd biçiminde yazılır, örn. 19:30."],
  "The end date must be after the start date.": ["Bitmə tarixi başlama tarixindən sonra olmalıdır.", "Дата окончания должна быть позже даты начала.", "Bitiş tarihi başlangıçtan sonra olmalı."],
  "Say why it was cancelled — the customers will be told.": ["Ləğvin səbəbini yazın — müştərilərə bildiriləcək.", "Укажите причину отмены — клиентам сообщат.", "İptal nedenini yazın — müşterilere bildirilecek."],
  "Seats are already sold for this screening, so the hall is fixed.": ["Bu seansa artıq bilet satılıb, zalı dəyişmək olmaz.", "На этот сеанс уже проданы билеты, зал изменить нельзя.", "Bu seansa bilet satıldı, salon değiştirilemez."],
  "Set either a percentage or an amount, not both.": ["Ya faiz, ya da məbləğ yazın — ikisini birlikdə yox.", "Укажите либо процент, либо сумму, но не оба.", "Ya yüzde ya tutar girin, ikisini birden değil."],
  "Use letters, digits, - and _ only.": ["Yalnız hərf, rəqəm, - və _ istifadə edin.", "Только буквы, цифры, - и _.", "Yalnızca harf, rakam, - ve _ kullanın."],
  "{0} already exists.": ["{0} artıq mövcuddur.", "{0} уже существует.", "{0} zaten var."],
  "A ticket type called \"{0}\" already exists.": ["«{0}» adlı bilet növü artıq var.", "Тип билета «{0}» уже существует.", "«{0}» adlı bilet türü zaten var."],

  // Generic
  "The body is not valid JSON.": ["Sorğunun məzmunu düzgün deyil.", "Некорректное содержимое запроса.", "İstek içeriği geçersiz."],
  "The request could not be read. Check the body and the parameters.": ["Sorğu oxunmadı. Məlumatları yoxlayın.", "Не удалось прочитать запрос. Проверьте данные.", "İstek okunamadı. Verileri kontrol edin."],
};

// "X was not found." — from Error.NotFound("X").
const THINGS: Record<string, Row> = {
  Account: ["Hesab", "Аккаунт", "Hesap"], Booking: ["Sifariş", "Бронирование", "Rezervasyon"],
  Conversation: ["Söhbət", "Переписка", "Sohbet"], Film: ["Film", "Фильм", "Film"], Movie: ["Film", "Фильм", "Film"],
  Hall: ["Zal", "Зал", "Salon"], Item: ["Məhsul", "Товар", "Ürün"], "Open shift": ["Açıq növbə", "Открытая смена", "Açık vardiya"],
  "Promo code": ["Promo kod", "Промокод", "Promosyon kodu"], Rental: ["İcarə", "Аренда", "Kiralama"], Report: ["Hesabat", "Отчёт", "Rapor"],
  Screening: ["Seans", "Сеанс", "Seans"], Submission: ["Film", "Работа", "Gönderi"], "Ticket type": ["Bilet növü", "Тип билета", "Bilet türü"],
  User: ["İstifadəçi", "Пользователь", "Kullanıcı"],
};
const NOT_FOUND: Row = ["{0} tapılmadı.", "{0}: не найдено.", "{0} bulunamadı."];

const INDEX = { az: 0, ru: 1, tr: 2 } as const;

// Templates become patterns once: "{0}" matches any run of characters.
const TEMPLATES = Object.entries(MESSAGES)
  .filter(([en]) => en.includes("{0}"))
  .map(([en, row]) => ({
    pattern: new RegExp("^" + en.replace(/[.*+?^$()|[\]\\]/g, "\\$&").replace(/\{(\d)\}/g, "(.+?)") + "$"),
    row,
  }));

export function translateServerMessage(message: string, lang: string): string {
  const i = INDEX[lang as keyof typeof INDEX];
  if (i === undefined || !message) return message;

  const exact = MESSAGES[message];
  if (exact) return exact[i];

  const missing = /^(.+) was not found\.$/.exec(message);
  if (missing) {
    const thing = THINGS[missing[1]];
    return NOT_FOUND[i].replace("{0}", thing ? thing[i] : missing[1]);
  }

  for (const { pattern, row } of TEMPLATES) {
    const match = pattern.exec(message);
    if (match) return row[i].replace(/\{(\d)\}/g, (_, n) => match[Number(n) + 1] ?? "");
  }
  return message;
}
