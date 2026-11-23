# Архів курсових і дипломних робіт

Веб-застосунок на **C# (ASP.NET Core 10, Razor Pages)** для зберігання курсових і дипломних робіт у хмарі.

- **Студенти** здають роботу через сторінку «Здати роботу» (без реєстрації, можна захистити кодом доступу). Список робіт вони не бачать.
- **Викладачі** входять за логіном і паролем, фільтрують роботи **за групою і роком здачі** (а також за типом і ПІБ), скачують окремі файли або **все знайдене одним ZIP-архівом**, можуть самі додавати і видаляти роботи.
- **Адміністратор** у панелі адміністратора бачить статистику, створює викладачів, блокує їх і скидає паролі, веде список груп, вмикає чи вимикає прийом робіт від студентів.

## Безкоштовна хмара: Supabase

| Що | Де зберігається | Безкоштовний ліміт |
|---|---|---|
| Метадані (групи, роки, ПІБ, користувачі) | Supabase **PostgreSQL** | 500 МБ |
| Самі файли робіт | Supabase **Storage**, приватний бакет `works` | 1 ГБ, до 50 МБ на файл |

Бакет **приватний**: файли не мають публічних посилань і віддаються лише через сервер після входу викладача.

## Структура проєкту

```
WorksArchive/
├── Program.cs                  — налаштування: БД, сховище, вхід, ролі
├── Data/
│   ├── Entities.cs             — моделі: AppUser, StudyGroup, Work, AppSetting
│   ├── AppDbContext.cs         — Entity Framework Core
│   └── DbInitializer.cs        — створення таблиць і першого адміністратора
├── Services/
│   ├── IFileStorage.cs         — інтерфейс сховища
│   ├── SupabaseStorage.cs      — Supabase Storage (REST API)
│   ├── LocalFileStorage.cs     — локальна папка (для розробки)
│   ├── WorkService.cs          — збереження, фільтр, ZIP
│   └── SettingsService.cs      — налаштування з адмін-панелі
└── Pages/
    ├── Index, Upload           — головна, здача роботи студентом
    ├── Account/                — вхід, вихід, зміна пароля
    ├── Works/                  — панель викладача (список, фільтр, завантаження)
    └── Admin/                  — адмін-панель (огляд, користувачі, групи, налаштування)
```

## 1. Швидкий запуск локально (без хмари)

Потрібен [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd WorksArchive
dotnet run --launch-profile "Local (SQLite)"
```

Відкрийте http://localhost:5080. У цьому режимі база — файл SQLite, а роботи лежать у папці `App_Data/`.
Перший вхід: логін **`admin`**, пароль **`Admin123!`** (одразу змініть його в меню користувача).

У Visual Studio або Rider виберіть профіль запуску **Local (SQLite)**.

## 2. Підключення Supabase

1. Зареєструйтеся на https://supabase.com і створіть новий проєкт (план **Free**). Запишіть пароль бази даних.
2. **Рядок підключення до БД**: кнопка **Connect** вгорі панелі → вкладка **Session pooler** (саме pooler, бо пряме підключення працює лише через IPv6). Скопіюйте параметри у форматі:
   ```
   Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<пароль>;SSL Mode=Require
   ```
3. **Ключ для Storage**: **Project Settings → API Keys** → скопіюйте `service_role` (або новий `secret`-ключ `sb_secret_...`) і **Project URL** (`https://<project-ref>.supabase.co`).
   ⚠️ Цей ключ секретний: зберігайте його лише на сервері й ніколи не комітьте в git.
4. Скопіюйте `WorksArchive/appsettings.Local.example.json` у `WorksArchive/appsettings.Local.json` (git цей файл ігнорує) і впишіть свої значення.
5. Запустіть:
   ```bash
   dotnet run --launch-profile "Cloud (Supabase)"
   ```
   Під час першого запуску застосунок сам створить таблиці в PostgreSQL і приватний бакет `works` у Storage.

## 3. Розміщення в інтернеті (безкоштовно)

Підійде будь-який хостинг, що вміє запускати Docker, наприклад **Render.com** (план Free):

1. New → **Web Service** → підключіть цей GitHub-репозиторій.
2. Root Directory: `WorksArchive`, Runtime: **Docker**.
3. Environment Variables (подвійне підкреслення `__` означає вкладеність):

| Змінна | Значення |
|---|---|
| `ConnectionStrings__Default` | рядок підключення з кроку 2.2 |
| `Storage__Supabase__Url` | `https://<project-ref>.supabase.co` |
| `Storage__Supabase__ServiceKey` | секретний ключ |
| `Admin__Password` | пароль першого адміністратора |

Безкоштовний сервер Render «засинає», якщо ним не користуються, тож перше відкриття після паузи триває близько 30–60 секунд. Дані при цьому не губляться, бо лежать у Supabase.

## Налаштування (`appsettings.json`)

| Ключ | Опис |
|---|---|
| `Database:Provider` | `Postgres` (Supabase) або `Sqlite` (локально) |
| `Storage:Provider` | `Supabase` або `Local` |
| `Storage:Supabase:Bucket` | назва бакета (за замовчуванням `works`) |
| `Upload:MaxFileSizeMb` | максимальний розмір файлу (у Supabase Free — до 50) |
| `Admin:Login` / `Admin:Password` | перший адміністратор (створюється, лише якщо користувачів ще немає) |

## Безпека

- Паролі зберігаються як хеш (PBKDF2, `PasswordHasher` з ASP.NET Core Identity).
- Ролі: сторінки `/Works/*` відкриті лише викладачам і адміністраторам, `/Admin/*` — лише адміністраторам.
- Після блокування користувача або зміни його пароля всі його активні сесії одразу завершуються.
- Вхід обмежено до 10 спроб на хвилину з однієї IP-адреси (захист від підбору пароля).
- Дозволені лише типи файлів документів і архівів: pdf, doc(x), odt, rtf, txt, ppt(x), xls(x), zip, rar, 7z.
- Усі форми захищені від CSRF (antiforgery-токени).
