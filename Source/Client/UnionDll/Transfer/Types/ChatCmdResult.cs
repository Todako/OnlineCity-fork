using System;

namespace OCUnion.Transfer.Types
{
    /// <summary>
    /// Результат виконання команди в чаті
    /// </summary>
    [Serializable]
    public enum ChatCmdResult : byte
    {
        /// <summary>
        /// Успішне виконання
        /// </summary>
        Ok = 0,

        /// <summary>
        /// Доступ заборонено
        /// </summary>
        AccessDeny = 1,

        /// <summary>
        /// Немає доступу до вказаного гравця
        /// </summary>
        CantAccess = 2,

        /// <summary>
        /// Команду не знайдено
        /// </summary>
        CommandNotFound = 3,

        /// <summary>
        /// Некоректна підкоманда або аргументи
        /// </summary>
        IncorrectSubCmd = 4,

        /// <summary>
        /// Операція доступна лише для загального/публічного каналу
        /// </summary>
        OnlyForPublicChannel = 5,

        /// <summary>
        /// Власний логін не знайдено (виникає під час спроби виконати команду чату через Discord)
        /// </summary>
        OwnLoginNotFound = 6,

        /// <summary>
        /// Гравець уже присутній у цьому чаті/каналі
        /// </summary>
        PlayerHere = 7,

        /// <summary>
        /// Ім'я гравця не вказано або воно порожнє
        /// </summary>
        PlayerNameEmpty = 8,

        /// <summary>
        /// Вказану роль не знайдено
        /// </summary>
        RoleNotFound = 9,

        /// <summary>
        /// Встановлення або зміна назви каналу
        /// </summary>
        SetNameChannel = 10,

        /// <summary>
        /// Користувач не має відповідних прав для цієї дії
        /// </summary>
        UserDoesNotHavePermission = 11,

        /// <summary>
        /// Користувача не знайдено
        /// </summary>
        UserNotFound = 12,
    }
}
