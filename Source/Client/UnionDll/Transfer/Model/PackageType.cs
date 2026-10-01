using System;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public enum PackageType : byte
    {
        /// <summary>
        /// 1 — реєстрація (логін, пароль) EN: register (login, password)
        /// </summary>
        Request1Register = 1,
        /// <summary>
        /// 2 — відповідь на реєстрацію (успішно, повідомлення) EN: answer (sucess, message)
        /// </summary>
        Response2Register = 2,
        /// <summary>
        /// 3 — вхід (логін, пароль)
        /// </summary>
        Request3Login = 3,
        /// <summary>
        /// 4 — відповідь на вхід (успішно, повідомлення)
        /// </summary>
        Response4Login = 4,
        /// <summary>
        /// 5 — запит інформації
        /// </summary>
        Request5UserInfo = 5,
        /// <summary>
        /// 6 — інформація про самого користувача
        /// </summary>
        Response6UserInfo = 6,
        /// <summary>
        /// 7 — створити світ (усе необхідне для запуску сервера)
        /// </summary>
        Request7CreateWorld = 7,
        /// <summary>
        /// 8 — відповідь на 7 (успішно, повідомлення)
        /// </summary>
        Response8WorldCreated = 8,
        /// <summary>
        /// 9 — створити поселення (запит із даними про поселення нового гравця; усе, що передається після створення ним мапи поселення)
        /// </summary>
        Request9CreateSettlement = 9,
        /// <summary>
        /// 10 — відповідь на 9 (успішно, повідомлення)
        /// </summary>
        Response10SettlementCreated = 10,
        /// <summary>
        /// 11 — синхронізація світу (тип синхронізації, час останньої синхронізації, усі дані для сервера)
        /// </summary>
        Request11 = 11,
        /// <summary>
        /// 12 — відповідь на 11 (час сервера, усі дані світу, що змінилися від зазначеного часу)
        /// </summary>
        Response12 = 12,
        /// <summary>
        /// 13 — створити гру (ідентифікатор лобі)
        /// </summary>
        Request13 = 13,
        /// <summary>
        /// 14 — відповідь (seed для створення світу, ?)
        /// </summary>
        Response14 = 14,
        /// <summary>
        /// 15 — надсилання ігрової дії (дані для оновлення на сервері)
        /// </summary>
        Request15 = 15,
        /// <summary>
        /// 16 — відповідь (успішно, повідомлення)
        /// </summary>
        Response16 = 16,
        /// <summary>
        /// 17 — оновити чат (час, після якого потрібні дані)
        /// </summary>
        Request17 = 17,
        /// <summary>
        /// 18 — дані чату
        /// </summary>
        Response18 = 18,
        /// <summary>
        /// 19 — написати в чат (ідентифікатор каналу, повідомлення); тут же командами можна створити канал, додати учасника тощо
        /// </summary>
        Request19PostingChat = 19,
        /// <summary>
        /// 20 — відповідь (повідомлення)
        /// </summary>
        Response20PostingChat = 20,
        /// <summary>
        /// 21 — команди для роботи з біржею.
        /// </summary>
        Request21 = 21,
        /// <summary>
        /// 22 — відповідь.
        /// </summary>
        Response22 = 22,
        /// <summary>
        /// 23 — команди для роботи з біржею
        /// </summary>
        Request23 = 23,
        /// <summary>
        /// 24 — відповідь
        /// </summary>
        Response24 = 24,
        /// <summary>
        /// 25 — команди для роботи з біржею
        /// </summary>
        Request25 = 25,
        /// <summary>
        /// 26 — відповідь
        /// </summary>
        Response26 = 26,
        /// <summary>
        /// 27 — онлайн-атака
        /// </summary>
        Request27 = 27,
        /// <summary>
        /// 28 — відповідь
        /// </summary>
        Response28 = 28,
        /// <summary>
        /// 29 — гравець, на якого здійснюється онлайн-атака
        /// </summary>
        Request29 = 29,
        /// <summary>
        /// 30 — відповідь
        /// </summary>
        Response30 = 30,

        RequestPlayerByToken,
        ResponsePlayerByToken,
        RequestServerInfo,
        ResponseServerInfo,
        /// <summary>
        /// Надсилання запиту з хешем файлів.
        /// </summary>
        Request35ListFiles,
        /// <summary>
        /// Отримання відповіді зі списком файлів.
        /// </summary>
        Response36ListFiles,

        Request37Reserv,
        Response38Reserv,


        /// <summary>
        /// Сповіщення про від'єднання.
        /// </summary>
        Request39Disconnect,
        /// <summary>
        /// Відповідь на від'єднання.
        /// </summary>
        Response40Disconnect,

        Request41SetPlayerInfo,
        Response42SetPlayerInfo,
        /// <summary>
        /// World Object Online GetWorldObjectUpdate()
        /// </summary>
        Request43WObjectUpdate,
        /// <summary>
        /// World Object Online GetWorldObjectUpdate()
        /// </summary>
        Response44WObjectUpdate,
        /// <summary>
        /// Download any byte[] by hash. AnyUpload / AnyLoad
        /// </summary>
        Request45AnyLoad,
        /// <summary>
        /// Download any byte[] by hash. AnyUpload / AnyLoad
        /// </summary>
        Response46AnyLoad,
        /// <summary>
        /// Керування складом товарів біржі.
        /// </summary>
        Request47Storage,
        /// <summary>
        /// Керування складом товарів біржі.
        /// </summary>
        Response48Storage,
        /// <summary>
        /// Download and upload file in category
        /// </summary>
        Request49FileSharing,
        /// <summary>
        /// Download and upload file in category
        /// </summary>
        Response50FileSharing,
        /// <summary>
        /// Не використовується.
        /// </summary>
        Request51Free,
        /// <summary>
        /// Не використовується.
        /// </summary>
        Response52Free,
        /// <summary>
        /// Отримати інформацію про біржу.
        /// </summary>
        Request53ExchengeInfo,
        /// <summary>
        /// Отримати інформацію про біржу.
        /// </summary>
        Response54ExchengeInfo,
        /// <summary>
        /// Отримати розширену інформацію про гравця.
        /// </summary>
        Request55PlayerInfoExtended,
        /// <summary>
        /// Отримати розширену інформацію про гравця.
        /// </summary>
        Response56PlayerInfoExtended,
    }
}