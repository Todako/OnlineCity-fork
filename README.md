# Fork of the OnlineCity mod for RimWorld
Author: Petro Bodnar, known as Todako.
Author of the original OnlineCity: Vasyl Ivanov, known as Aant

**OnlineCity** is a mod for RimWorld that allows multiple players to play on the same planet.
Build your own colony, develop it, and interact with other players. Keep an eye on their colonies and caravans, and help each other by sharing resources, equipment, and even colonists.

# Fork моду OnlineCity для гри RimWorld
Автор: Петро Боднар, відомий як Todako.
Автор оригінального OnlineCity: Василь Іванов, відомий як Aant

**OnlineCity** — модифікація для RimWorld, яка дозволяє кільком гравцям грати на одній планеті.
Створюйте власне поселення, розвивайте його та взаємодійте з іншими гравцями. Спостерігайте за їхніми поселеннями й караванами та допомагайте одне одному, передаючи ресурси, спорядження і навіть поселенців.


# Information for Players
**OnlineCity works with RimWorld versions 1.1–1.4.**

To play, install **Harmony → Core → HugsLib → OnlineCity**.
OnlineCity is compatible with most mods, but the specific set of mods supported may vary by server. Definitely not compatible with the following mods: Save Our Ship 2 (SoS2) and RimWorld Multiplayer (Zetrith).

**Synchronization** with the server occurs every 5 seconds, and a full save takes place every 10 minutes or when you exit via the menu.
Do not close the game by clicking the X button to avoid losing your progress since the last save.

Good to know:
* OnlineCity is currently designed primarily for mutual assistance among players.
* Other players’ settlements and caravans are marked in blue. You can interact with them by right-clicking on your own caravan.
* To have items from other players automatically stored in the correct warehouse, add “trade” to the warehouse’s name.
* All online players can chat in the general chat. You can also create a private chat.
* Click on a player’s nickname to view their profile or open a private chat.

# Інформація для гравців
**OnlineCity працює з RimWorld 1.1–1.4.**

Для гри встановіть **Harmony → Core → HugsLib → OnlineCity**.
OnlineCity сумісний із більшістю модів, але їхній набір може залежати від сервера. Точно не сумісний з модами: Save Our Ship 2 (SoS2) та RimWorld Multiplayer (Zetrith).

**Синхронізація** з сервером відбувається кожні 5 секунд, а повне збереження — кожні 10 хвилин або під час виходу через меню.
Не закривайте гру хрестиком щоб не втратити прогрес після останнього збереження.

Корисно знати:
* OnlineCity наразі призначений переважно для взаємодопомоги між гравцями.
* Поселення та каравани інших гравців позначені синім. Взаємодіяти з ними можна правою кнопкою миші, вибравши свій караван.
* Щоб товари від інших гравців автоматично зберігалися на потрібному складі, додайте до його назви «торг» або «trade».
* У загальному чаті можуть спілкуватися всі гравці онлайн. Також можна створити приватний чат.
* Натисніть на нікнейм гравця, щоб переглянути його інформацію або відкрити приватний чат.


# Information for Developers
Projects:
* **Converter** — Conversion of world saves for newer versions.
* **RimWorldOnlineCity** — The main mod library for the game.
* **DiscordChatBotServer** — A background service for integrating chat and server commands with Discord.
* **Server** — the server component that accepts player connections.
* **UnionDll (OCUnion)** — shared code and models for the client and server.

Build
The project is built into the **Build** folder, which is located next to **Source**. The `Source` folder must remain in the root of the repository, since the files for the build are taken from there.

After the build:
* **Build/Client** — the finished OnlineCity for players. Move it to the game’s `Mods` folder.
* **Build/Server** — the server component.

Where to start:
* **StartPoint.cs** — the mod’s entry point and its initialization. The OnlineCity button in the game’s bottom bar is also created here.
* **SessionClientController.cs** — main client initialization, server connection, and general functions.
* **Dialog_MainOnlineCity.cs** — the main OnlineCity window and user interaction.

# Інформація для розробників
Проєкти:
* **Converter** — конвертація збережень світу для новіших версій.
* **RimWorldOnlineCity** — основна бібліотека мода для гри.
* **DiscordChatBotServer** — фоновий сервіс для зв'язку чату та команд сервера з Discord.
* **Server** — серверна частина, що приймає підключення гравців.
* **UnionDll (OCUnion)** — спільний код і моделі для клієнта та сервера.

Збірка
Проєкт збирається у папку **Build**, яка знаходиться поруч із **Source**. Папка `Source` має залишатися в корені репозиторію, оскільки файли для збірки беруться саме звідти.

Після збірки:
* **Build/Client** — готовий OnlineCity для гравців. Переміщуємо в папку гри  `Mods`.
* **Build/Server** — серверна частина.

З чого почати:
* **StartPoint.cs** — точка входу мода та його первинна ініціалізація. Тут також створюється кнопка OnlineCity у нижній панелі гри.
* **SessionClientController.cs** — основна ініціалізація клієнта, підключення до сервера та загальні функції.
* **Dialog_MainOnlineCity.cs** — головне вікно OnlineCity та взаємодія з користувачем.


## Взаємодія «клієнт-сервер» // Client-Server Communication
### Start of the Exchange
**Client**
The exchange begins in `Dialog_LoginForm` or `Dialog_Registration`, which call `SessionClientController.Login` / `Registration`.
Next, `SessionClientController.Connect` establishes a connection via `SessionClient.Connect`.
During connection:
1. The client sends a `0x00` packet to the server.
2. The server returns a symmetric session key for encrypting traffic.
3. The connection is maintained by `ConnectClient` on the client and `ConnectServer` on the server.

Further data exchange occurs via `TransObject<>`:
* Data is packaged into `ModelContainer`;
* `Trans` serializes, compresses (`GZip.ZipObjByte`), encrypts (`CryptoProvider.SymmetricEncrypt`), and sends it to the server;
* the received data is decrypted, unpacked, and deserialized back into a `ModelContainer`.
A `ModelContainer` may also contain error messages.

**Server**
After the connection is established, the `ServerManager.ConnectionAccepted` event is triggered. A separate thread is created for the client, and `DoClient` is launched, which creates a `SessionServer` game session and calls its `Do` method.
`Do` continuously receives and processes packets, then passes them on for processing via `Service`.
More details on how the server works are provided below.

### Початок обміну
**Клієнт**
Обмін починається у `Dialog_LoginForm` або `Dialog_Registration`, які викликають `SessionClientController.Login` / `Registration`.
Далі `SessionClientController.Connect` встановлює з’єднання через `SessionClient.Connect`.
Під час підключення:
1. Клієнт надсилає серверу пакет `0x00`.
2. Сервер повертає симетричний ключ сесії для шифрування трафіку.
3. З’єднання забезпечують `ConnectClient` на клієнті та `ConnectServer` на сервері.

Подальший обмін відбувається через `TransObject<>`:
* дані упаковуються в `ModelContainer`;
* `Trans` серіалізує, стискає (`GZip.ZipObjByte`), шифрує (`CryptoProvider.SymmetricEncrypt`) і надсилає їх серверу;
* отримані дані розшифровуються, розпаковуються та десеріалізуються назад у `ModelContainer`.
`ModelContainer` також може містити повідомлення про помилки.

**Сервер**
Після підключення виникає подія `ServerManager.ConnectionAccepted`. Для клієнта створюється окремий потік і запускається `DoClient`, який створює ігрову сесію `SessionServer` та запускає її метод `Do`.
`Do` безперервно приймає й обробляє пакети, після чого передає їх на обробку через `Service`.
Детальніше про роботу сервера — нижче.

### Request—Response
**Client**
There is a separate function in `SessionClient` for each type of request. It:
* wraps the parameters into a model;
* calls `TransObject<>`;
* passes the expected response model and the request/response codes.
Typically, **an odd-numbered code indicates a request, and the next even-numbered code indicates a response**.

**Server**
The received `ModelContainer` is processed via `SessionServer.Service`. Based on the request code, `Service` determines the appropriate handler in the `Service` class.
Thus, `Service` on the server is a kind of counterpart to `SessionClient` on the client: the client calls a function, and the server processes the corresponding request.
To add a new request, you need to:
1. Add a function to `SessionClient`.
2. Add the corresponding handler to `Service`.
3. Register the new request codes in `SessionServer.Service`.

### Запит — відповідь
**Клієнт**
Для кожного типу запиту в `SessionClient` є окрема функція. Вона:
* упаковує параметри в модель;
* викликає `TransObject<>`;
* передає модель очікуваної відповіді та коди запиту/відповіді.
Зазвичай **непарний код — запит, наступний парний — відповідь**.

**Сервер**
Отриманий `ModelContainer` обробляється через `SessionServer.Service`. За кодом запиту `Service` визначає потрібний обробник у класі `Service`.
Таким чином, `Service` на сервері є своєрідним аналогом `SessionClient` на клієнті: клієнт викликає функцію, а сервер обробляє відповідний запит.
Для додавання нового запиту потрібно:
1. Додати функцію в `SessionClient`.
2. Додати відповідний обробник у `Service`.
3. Зареєструвати нові коди в `SessionServer.Service`.


## Взаємодія з грою // Interaction with the Game
### Connection
The main game data is stored in `SessionClientController.ClientData`.
The mod starts by calling `Dialog_LoginForm` or `Dialog_Registration`, which in turn call `Login` / `Registration` and connect to the server.

Once connected, `InitConnected` is triggered:
* **The world has not yet been created** — the administrator opens `Dialog_CreateWorld`, after which `GameStarter.GameGeneration` creates the world, and `CreatingWorld` saves it to the server via `connect.CreateWorld()`.
* **No player save** — a new planet is created with other players’ settlements. After selecting a landing site and creating a colony, `CreatePlayerMap` is triggered, which saves the game and proceeds to `InitGame`.
* **A save exists** — it is loaded, after which `InitGame` is triggered via `GameLoades.AfterLoad`.

### Game Initialization
`InitGame`:
* clears the world via `UpdateWorldController.ClearWorld()`;
* initiates synchronization with the server via `UpdateWorldController.InitGame()`;
* executes the first `UpdateWorld(true)` to link existing settlements to their `serverId`s;
* starts three periodic timer-based events;
* configures exit handling: performs an out-of-turn save to the server and disconnects.

### Підключення
Основні ігрові дані зберігаються в `SessionClientController.ClientData`.
Робота мода починається з `Dialog_LoginForm` або `Dialog_Registration`, які викликають `Login` / `Registration` і підключаються до сервера.

Після підключення запускається `InitConnected`:
* **Світ ще не створено** — адміністратор відкриває `Dialog_CreateWorld`, після чого `GameStarter.GameGeneration` створює світ, а `CreatingWorld` зберігає його на сервері через `connect.CreateWorld()`.
* **Збереження гравця немає** — створюється нова планета з поселеннями інших гравців. Після вибору місця висадки та створення колонії запускається `CreatePlayerMap`, яке зберігає гру та переходить до `InitGame`.
* **Збереження є** — воно завантажується, після чого через `GameLoades.AfterLoad` запускається `InitGame`.

### Ініціалізація гри
`InitGame`:
* очищає світ через `UpdateWorldController.ClearWorld()`;
* запускає синхронізацію з сервером через `UpdateWorldController.InitGame()`;
* виконує перше `UpdateWorld(true)` для прив’язки вже існуючих поселень до їхніх `serverId`;
* запускає три циклічні події за таймером;
* налаштовує обробку виходу з гри: виконується позачергове збереження на сервері та роз’єднання.


### Recurring Events During Gameplay
* **Every 0.5 seconds — chat update `UpdateChats()`**
  * We request new messages from the server via `connect.UpdateChat()`, passing the time of the last request.
  * The received messages are added to `Data.Chats` via `Data.ApplyChats()`.
  * If the server does not respond for more than 8 seconds, `ServerConnected` marks the connection as disconnected.

* **Every few seconds — world synchronization `UpdateWorld(false)`**
  * `SendToServer` sends changes to the planet to the server.
  * `LoadFromServer` retrieves the latest data about the world and other players.

* **Every 10 minutes — save `BackgroundSaveGame()`**
  * `SaveGame` is executed, and the resulting save file is sent to the server during the next world synchronization.

### Циклічні події під час гри
* **Кожні 0,5 секунди — оновлення чату `UpdateChats()`**
  * Запитуємо з сервера нові повідомлення через `connect.UpdateChat()`, передаючи час останнього запиту.
  * Отримані повідомлення додаються до `Data.Chats` через `Data.ApplyChats()`.
  * Якщо сервер не відповідає понад 8 секунд, `ServerConnected` визначає з’єднання як розірване.

* **Кожні кілька секунд — синхронізація світу `UpdateWorld(false)`**
  * `SendToServer` передає на сервер зміни планети.
  * `LoadFromServer` отримує актуальні дані про світ та інших гравців.

* **Кожні 10 хвилин — збереження `BackgroundSaveGame()`**
  * Виконується `SaveGame`, а отримане збереження передається на сервер під час наступної синхронізації світу.
