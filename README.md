# Kiriha

[English](#english) | [Русский](#русский)

## English

Kiriha is a Windows desktop anime tracker with MAL/Shikimori sync, local media scrobbling, a built-in mpv player, torrent discovery, notifications, and Discord Rich Presence.

The project is built with C#, .NET 10, Avalonia UI, SQLite, and libmpv. It is inspired by Taiga and MAL Updater, but is focused on a modern Russian/English desktop workflow: tracking, watching, matching filenames, and keeping anime metadata close at hand.

> Kiriha is an unofficial client. It is not affiliated with MyAnimeList, Shikimori, AniList, Nyaa.si, Jikan, or mpv.

### Features

- MyAnimeList account login, list sync, status updates, scores, progress, and history.
- Shikimori metadata support for Russian titles, descriptions, mirrors, and anime links.
- AniList airing schedule data for upcoming episodes.
- Local media scrobbling through player detection and filename parsing.
- Built-in mpv/libmpv player with custom overlay, hotkeys, subtitles, audio tracks, screenshots, fullscreen mode, and video-processing settings.
- Torrent search and filtering through Nyaa.si feeds.
- Manual title mapping for files that cannot be matched automatically.
- Discord Rich Presence integration.
- Windows toast notifications for new episodes, skipped episode detection, and app events.
- SQLite local cache for metadata, mappings, history, and sync state.
- Auto-update packaging through Velopack.
- English and Russian localization assets.

### Tech Stack

- C# / .NET 10
- Avalonia UI 12
- CommunityToolkit.Mvvm
- Entity Framework Core + SQLite
- libmpv / mpv render API
- Serilog
- Velopack
- xUnit

### Requirements

- Windows 10 19041 or newer
- .NET 10 SDK
- PowerShell
- Internet access during the first build, because `download-mpv.ps1` downloads `libmpv-2.dll` when `mpv/` is missing

### Build And Run

Clone the repository:

```powershell
git clone https://github.com/Donate684/kiriha.git
cd kiriha
```

Restore and build:

```powershell
dotnet restore .\Kiriha.sln
dotnet build .\Kiriha.sln
```

Run the app:

```powershell
dotnet run --project .\Kiriha.csproj
```

Run tests:

```powershell
dotnet test .\Kiriha.sln
```

The build target automatically runs `download-mpv.ps1` if `mpv\libmpv-2.dll` is not present. The `mpv/` directory is intentionally ignored by Git and copied into the output folder during build.

### Release Build

Create a local Release publish:

```powershell
dotnet publish .\Kiriha.csproj -c Release --runtime win-x64 --self-contained true -p:PublishReadyToRun=true -o .\publish
```

The repository also includes a release helper:

```powershell
.\release.bat 1.2.3
```

It updates the project version, runs tests, publishes a self-contained Windows build, creates Velopack packages, commits, tags, and pushes the release.

### Project Layout

```text
Assets/        Icons, localization files, Anisthesia player definitions
Composition/   Dependency injection registrations
Core/          App primitives, mpv interop, navigation, platform helpers
Models/        Domain models, settings, entities, API DTOs
Services/      API clients, auth, tracking, data, lifecycle, notifications
ViewModels/    MVVM state and commands
Views/         Avalonia windows, controls, styles
Tests/         xUnit test suite
```

### Data Sources

Kiriha uses public APIs and feeds from:

- MyAnimeList
- Shikimori / Shikimori mirror
- AniList
- Jikan
- Nyaa.si

API responses and derived metadata are cached locally in SQLite to reduce network calls and keep the UI responsive.

### OAuth Notes

`Core/ApiKeys.cs` contains public desktop OAuth client identifiers/secrets used by the app. This is intentional for this project: Kiriha is a local desktop client, and the supported providers require these values for token exchange while not offering a fully public-client flow for every case.

The tokens granted to a user are stored locally and are separate from the committed client registration values.

### Development Notes

- Nullable warnings are treated as errors.
- Package versions are managed centrally in `Directory.Packages.props`.
- `bin/`, `obj/`, `publish/`, `Releases/`, `artifacts/`, test outputs, logs, and `mpv/` are ignored.
- The internal player can be launched via the app's `--player` mode and also supports a resident player process.

### Credits

Kiriha depends on excellent open-source projects, including Avalonia UI, mpv/libmpv, CommunityToolkit.Mvvm, Entity Framework Core, Serilog, Velopack, DiscordRichPresence, AnitomySharp, and Anisthesia.

The app is inspired by Taiga and MAL Updater.

### License

No license file is included yet. Add a `LICENSE` file before distributing or accepting external contributions.

## Русский

Kiriha - десктопный аниме-трекер для Windows с синхронизацией MAL/Shikimori, локальным скробблингом, встроенным mpv-плеером, поиском торрентов, уведомлениями и Discord Rich Presence.

Проект написан на C# / .NET 10 с Avalonia UI, SQLite и libmpv. По духу он вдохновлен Taiga и MAL Updater, но заточен под удобный русско-английский сценарий: смотреть, трекать прогресс, распознавать файлы и держать метаданные рядом.

> Kiriha - неофициальный клиент. Проект не связан с MyAnimeList, Shikimori, AniList, Nyaa.si, Jikan или mpv.

### Возможности

- Авторизация MyAnimeList, синхронизация списка, статусов, оценок, прогресса и истории.
- Метаданные Shikimori: русские названия, описания, зеркала и ссылки на аниме.
- Расписание выходящих эпизодов через AniList.
- Локальный скробблинг через определение запущенного плеера и парсинг имени файла.
- Встроенный mpv/libmpv-плеер с кастомным оверлеем, хоткеями, субтитрами, аудиодорожками, скриншотами, fullscreen-режимом и настройками обработки видео.
- Поиск и фильтрация торрентов через Nyaa.si.
- Ручные сопоставления названий для файлов, которые не удалось распознать автоматически.
- Discord Rich Presence.
- Windows toast-уведомления о новых эпизодах, пропущенных эпизодах и событиях приложения.
- Локальный SQLite-кеш для метаданных, маппингов, истории и состояния синхронизации.
- Упаковка автообновлений через Velopack.
- Английская и русская локализация.

### Стек

- C# / .NET 10
- Avalonia UI 12
- CommunityToolkit.Mvvm
- Entity Framework Core + SQLite
- libmpv / mpv render API
- Serilog
- Velopack
- xUnit

### Требования

- Windows 10 19041 или новее
- .NET 10 SDK
- PowerShell
- Интернет при первой сборке: `download-mpv.ps1` скачивает `libmpv-2.dll`, если папки `mpv/` еще нет

### Сборка И Запуск

Клонировать репозиторий:

```powershell
git clone https://github.com/Donate684/kiriha.git
cd kiriha
```

Восстановить зависимости и собрать:

```powershell
dotnet restore .\Kiriha.sln
dotnet build .\Kiriha.sln
```

Запустить приложение:

```powershell
dotnet run --project .\Kiriha.csproj
```

Запустить тесты:

```powershell
dotnet test .\Kiriha.sln
```

Во время сборки автоматически запускается `download-mpv.ps1`, если `mpv\libmpv-2.dll` отсутствует. Папка `mpv/` специально игнорируется Git и копируется в output при сборке.

### Релизная Сборка

Собрать локальный Release:

```powershell
dotnet publish .\Kiriha.csproj -c Release --runtime win-x64 --self-contained true -p:PublishReadyToRun=true -o .\publish
```

Также есть helper-скрипт:

```powershell
.\release.bat 1.2.3
```

Он обновляет версию проекта, запускает тесты, публикует self-contained Windows-сборку, создает Velopack-пакеты, делает commit, tag и push.

### Структура Проекта

```text
Assets/        Иконки, локализация, определения плееров Anisthesia
Composition/   Регистрация зависимостей
Core/          Базовые примитивы, mpv interop, навигация, platform helpers
Models/        Доменные модели, настройки, сущности, API DTO
Services/      API-клиенты, auth, tracking, data, lifecycle, notifications
ViewModels/    MVVM-состояние и команды
Views/         Avalonia-окна, контролы, стили
Tests/         xUnit-тесты
```

### Источники Данных

Kiriha использует публичные API и фиды:

- MyAnimeList
- Shikimori / зеркало Shikimori
- AniList
- Jikan
- Nyaa.si

Ответы API и производные метаданные кешируются локально в SQLite, чтобы снизить количество сетевых запросов и ускорить интерфейс.

### OAuth

`Core/ApiKeys.cs` содержит публичные desktop OAuth client id/secret, которые использует приложение. Для этого проекта это намеренное решение: Kiriha - локальный десктопный клиент, а используемые провайдеры требуют эти значения для token exchange и не везде дают полноценный public-client flow.

Пользовательские токены хранятся локально и не являются тем же самым, что committed client registration values.

### Для Разработки

- Nullable warnings считаются ошибками.
- Версии пакетов централизованы в `Directory.Packages.props`.
- `bin/`, `obj/`, `publish/`, `Releases/`, `artifacts/`, test outputs, logs и `mpv/` игнорируются.
- Внутренний плеер можно запустить через `--player`; также есть режим resident player process.

### Благодарности

Kiriha использует отличные open-source проекты: Avalonia UI, mpv/libmpv, CommunityToolkit.Mvvm, Entity Framework Core, Serilog, Velopack, DiscordRichPresence, AnitomySharp и Anisthesia.

Приложение вдохновлено Taiga и MAL Updater.

### Лицензия

Файл лицензии пока не добавлен. Перед публичным распространением или приемом внешних контрибьютов стоит добавить `LICENSE`.
