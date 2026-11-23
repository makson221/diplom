# Архів курсових і дипломних робіт

Веб-застосунок на **C# (ASP.NET Core 10, Razor Pages)** для зберігання курсових і дипломних робіт у хмарі.

- **Студенти** здають роботу через сторінку «Здати роботу» (без реєстрації, можна захистити кодом доступу). Список робіт вони не бачать.
- **Викладачі** входять за логіном і паролем, фільтрують роботи **за групою і роком здачі** (а також за типом і ПІБ), скачують окремі файли або **все знайдене одним ZIP-архівом**, можуть самі додавати і видаляти роботи.
- **Адміністратор** у панелі адміністратора бачить статистику і заповненість сховища, створює викладачів, блокує їх і скидає паролі, веде список груп, підключає Google Drive, вмикає чи вимикає прийом робіт від студентів і задає тему оформлення за замовчуванням.
- **Теми оформлення**: кнопка 🎨 у верхньому меню дає вибрати режим (світлий, темний або автоматично, як у системі) і один із 7 кольорів. Вибір запам'ятовується в браузері кожного користувача.

## Де зберігаються дані (безкоштовно)

Застосунок зберігає окремо **метадані** (групи, роки, ПІБ, користувачі; це кілька мегабайт навіть для десятків тисяч робіт) і **самі файли**.

### Файли: оберіть сховище (`Storage:Provider`)

| Сховище | Безкоштовно | Картка | `Provider` | Примітка |
|---|---|---|---|---|
| **Google Drive** ⭐ | **15 ГБ** | не потрібна | `GoogleDrive` | Рекомендовано. Файли видно і в самому Drive, по папках «Рік/Група/Тип» |
| Oracle Cloud Object Storage | 20 ГБ | потрібна | `S3` | Найбільший безкоштовний обсяг |
| Backblaze B2 | 10 ГБ | за більшістю джерел не потрібна | `S3` | Понад 10 ГБ коштує ≈ $7 за 1 ТБ на місяць |
| Cloudflare R2 | 10 ГБ | потрібна | `S3` | |
| Supabase Storage | 1 ГБ | не потрібна | `Supabase` | Ліміт 50 МБ на файл |
| Локальна папка | — | — | `Local` | Лише для розробки |

**Скільки потрібно місця:** курсова зазвичай займає 1–5 МБ, диплом 3–20 МБ. 1000+ робіт ≈ **5–10 ГБ**, тож Google Drive (15 ГБ) вистачить приблизно на 1500–3000 робіт. Скільки вже зайнято і на скільки робіт ще вистачить, показує сторінка **Адмін-панель → Сховище**. Якщо місце закінчиться, можна докупити Google One (100 ГБ коштують кілька доларів на місяць) або перейти на Backblaze B2 чи Oracle.

### База даних: PostgreSQL

| Сервіс | Безкоштовно | Примітка |
|---|---|---|
| **Neon** ⭐ (neon.com) | 1 ГБ на проєкт | Рекомендовано. «Засинає» без запитів, але **сам прокидається** за ~1 с |
| Supabase (supabase.com) | 500 МБ | Безкоштовний проєкт ставиться на паузу після 7 днів без активності, і його треба вручну відновлювати в панелі |

## Структура проєкту

```
WorksArchive/
├── Program.cs                    — налаштування: БД, сховище, вхід, ролі
├── Data/                         — моделі (AppUser, StudyGroup, Work, AppSetting) і EF Core
├── Services/
│   ├── IFileStorage.cs           — інтерфейс сховища + налаштування
│   ├── GoogleDriveStorage.cs     — Google Drive (OAuth 2.0, Drive API v3)
│   ├── S3Storage.cs              — S3-сумісні: Backblaze B2, Oracle, R2, IDrive e2, MinIO
│   ├── SupabaseStorage.cs        — Supabase Storage
│   ├── LocalFileStorage.cs       — локальна папка (для розробки)
│   ├── WorkService.cs            — збереження, фільтр, ZIP, заповненість
│   ├── SettingsService.cs        — налаштування з адмін-панелі
│   └── Themes.cs                 — теми оформлення
└── Pages/
    ├── Index, Upload             — головна, здача роботи студентом
    ├── Account/                  — вхід, вихід, зміна пароля
    ├── Works/                    — панель викладача (список, фільтр, завантаження)
    └── Admin/                    — огляд, користувачі, групи, сховище, налаштування
```

## 1. Швидкий запуск локально (без хмари)

Потрібен [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd WorksArchive
dotnet run --launch-profile "Local (SQLite)"
```

Відкрийте http://localhost:5080. Перший вхід: логін **`admin`**, пароль **`Admin123!`** (одразу змініть його в меню користувача).
У цьому режимі база — файл SQLite, а роботи лежать у папці `App_Data/`.

## 2. База даних Neon

1. Зареєструйтеся на https://neon.com і створіть проєкт (регіон Frankfurt, `eu-central-1`, найближчий до України).
2. **Connect** → скопіюйте параметри підключення і запишіть їх у форматі Npgsql:
   ```
   Host=ep-xxxx.eu-central-1.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=<пароль>;SSL Mode=Require
   ```
Таблиці застосунок створить сам під час першого запуску.

## 3а. Файли в Google Drive (рекомендовано)

1. Створіть **окремий Gmail-акаунт** для архіву, наприклад `archive.kafedra@gmail.com`. Так усі 15 ГБ дістануться роботам, а не пошті й фото.
2. Відкрийте https://console.cloud.google.com, увійдіть під цим акаунтом і створіть проєкт (наприклад, «Архів робіт»).
3. **APIs & Services → Library** → знайдіть **Google Drive API** → **Enable**.
4. **Google Auth Platform** (або **OAuth consent screen**) → **Get started**: назва застосунку, email підтримки, Audience: **External**.
5. **Audience** → **Publish app** (статус **In production**). ⚠️ Це важливо: у режимі *Testing* Google відкликає доступ кожні 7 днів. Застосунок просить лише право `drive.file` (доступ тільки до своїх файлів), тому перевірка від Google не потрібна.
6. **Clients → Create client** → тип **Web application** → **Authorized redirect URIs**, додайте:
   - `http://localhost:5080/Admin/GoogleDriveCallback` — для локального запуску;
   - `https://ВАШ-САЙТ/Admin/GoogleDriveCallback` — для сайту в інтернеті (точну адресу показує сторінка «Сховище»).
7. Скопіюйте **Client ID** і **Client secret** у `appsettings.Local.json` (зразок: `appsettings.Local.example.json`):
   ```json
   "Storage": { "Provider": "GoogleDrive",
     "GoogleDrive": { "ClientId": "...apps.googleusercontent.com", "ClientSecret": "..." } }
   ```
8. Запустіть сайт, увійдіть як адміністратор → **Адмін-панель → Сховище → «Підключити Google Drive»** → увійдіть у Gmail архіву → **Дозволити**. Якщо Google покаже «застосунок не перевірено», натисніть «Додатково → Перейти».

Готово. Роботи з'являтимуться на Drive у папках `Архів робіт / 2026 / КН-41 / Курсові / Іваненко Іван - курсова.pdf`. Refresh-токен Google зберігається в базі в зашифрованому вигляді.

## 3б. Файли в Backblaze B2 / Oracle / R2 (S3-сумісні)

**Backblaze B2** (10 ГБ):
1. Зареєструйтеся на https://www.backblaze.com/cloud-storage → **Buckets → Create a Bucket**, тип **Private**.
2. Запишіть **Endpoint** бакета, наприклад `s3.eu-central-003.backblazeb2.com`.
3. **Application Keys → Add a New Application Key** з доступом до цього бакета. `keyID` — це AccessKey, `applicationKey` — SecretKey.

```json
"Storage": {
  "Provider": "S3",
  "QuotaGb": 10,
  "S3": {
    "ServiceUrl": "https://s3.eu-central-003.backblazeb2.com",
    "Region": "eu-central-003",
    "AccessKey": "keyID", "SecretKey": "applicationKey", "Bucket": "назва-бакета"
  }
}
```

**Oracle Cloud** (20 ГБ): створіть бакет в Object Storage, у профілі користувача згенеруйте **Customer Secret Key**. Endpoint має вигляд `https://<namespace>.compat.objectstorage.<регіон>.oraclecloud.com`, Region — наприклад `eu-frankfurt-1`, `QuotaGb: 20`.

**Cloudflare R2**: Endpoint `https://<account-id>.r2.cloudflarestorage.com`, Region `auto`.

`QuotaGb` — розмір безкоштовного ліміту для індикатора заповненості (S3-сервіси самі його не повідомляють).

## 4. Розміщення в інтернеті (безкоштовно, Render.com)

Сайт працюватиме за адресою на кшталт `https://works-archive.onrender.com`, і викладачі зможуть заходити з будь-якого комп'ютера чи телефону.
У репозиторії вже є все потрібне: `WorksArchive/Dockerfile` і `render.yaml`.

**Перед початком** підготуйте:
- рядок підключення до бази **Neon** (розділ 2);
- **Client ID** і **Client Secret** Google (розділ 3а, кроки 1–6);
- код у гілці **`main`** на GitHub. Render бере код звідти.

**Кроки:**
1. Зареєструйтеся на https://render.com через **GitHub** (картка не потрібна).
2. **New → Blueprint** → дозвольте Render доступ до репозиторію `diplom` → оберіть його.
3. Render прочитає `render.yaml` і попросить ввести секрети:

   | Змінна | Що вписати |
   |---|---|
   | `ConnectionStrings__Default` | рядок підключення Neon |
   | `Storage__GoogleDrive__ClientId` | Client ID від Google |
   | `Storage__GoogleDrive__ClientSecret` | Client Secret від Google |
   | `Admin__Password` | пароль адміністратора (придумайте надійний) |

4. **Apply**. Перша збірка триває 3–5 хвилин. Після неї з'явиться адреса сайту, наприклад `https://works-archive.onrender.com`.
5. У **Google Cloud Console → Clients** → ваш клієнт → **Authorized redirect URIs** додайте
   `https://works-archive.onrender.com/Admin/GoogleDriveCallback` (з вашою адресою).
6. Відкрийте сайт, увійдіть як `admin` → **Адмін-панель → Сховище → Підключити Google Drive**.
7. **Адмін-панель → Групи**: додайте групи. **Користувачі**: створіть викладачів і передайте їм адресу сайту, логін і пароль.

Після цього кожна зміна коду в гілці `main` автоматично оновлює сайт.

**Про «засинання».** Безкоштовний сервер Render засинає після 15 хвилин без відвідувачів, і перше відкриття після цього триває близько хвилини. Дані не губляться: вони в Neon і Google Drive. Щоб сайт не засинав:
1. Зареєструйтеся на https://uptimerobot.com (безкоштовно).
2. **Add New Monitor** → тип **HTTP(s)** → URL `https://works-archive.onrender.com/health`, інтервал **5 хвилин**.

Одного сервісу, що працює цілодобово, вистачає на місяць: це ~720 годин із 750 безкоштовних.

**Власна адреса (необов'язково).** У Render → Settings → **Custom Domains** можна підключити домен, наприклад `archive.college.edu.ua`, якщо його надасть адміністратор сайту закладу.

**Інші хостинги.** Застосунок запуститься на будь-якому хостингу з Docker: Koyeb, Railway, Azure App Service тощо. Порт береться зі змінної `PORT`, налаштування — зі змінних середовища (див. таблицю вище). Є й безкоштовні хостинги саме для ASP.NET, наприклад MonsterASP.NET: туди публікують з Visual Studio (Publish → Web Deploy) і задають ті самі налаштування в `appsettings.Local.json`.

## Налаштування (`appsettings.json`)

| Ключ | Опис |
|---|---|
| `Database:Provider` | `Postgres` або `Sqlite` (локально) |
| `Storage:Provider` | `GoogleDrive`, `S3`, `Supabase` або `Local` |
| `Storage:QuotaGb` | ліміт для індикатора заповненості (для S3/Supabase/Local) |
| `Storage:GoogleDrive:RootFolderName` | назва кореневої папки на Drive |
| `Upload:MaxFileSizeMb` | максимальний розмір одного файлу (за замовчуванням 100; для Supabase — до 50) |
| `Admin:Login` / `Admin:Password` | перший адміністратор (створюється, лише якщо користувачів ще немає) |

## Безпека

- Паролі зберігаються як хеш (PBKDF2, `PasswordHasher` з ASP.NET Core Identity).
- Ролі: сторінки `/Works/*` відкриті лише викладачам і адміністраторам, `/Admin/*` — лише адміністраторам.
- Після блокування користувача або зміни його пароля всі його активні сесії одразу завершуються.
- Вхід обмежено до 10 спроб на хвилину з однієї IP-адреси (захист від підбору пароля).
- Файли не мають публічних посилань: бакети приватні, на Google Drive у застосунку є доступ лише до власних файлів (`drive.file`), а скачати роботу можна тільки після входу.
- Refresh-токен Google зашифровано ключами ASP.NET Data Protection, які теж зберігаються в БД.
- Дозволені лише типи файлів документів і архівів: pdf, doc(x), odt, rtf, txt, ppt(x), xls(x), zip, rar, 7z.
- Усі форми захищені від CSRF (antiforgery-токени).
