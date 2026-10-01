using OCUnion.Transfer.Model;
using RimWorldOnlineCity.UI;
using System.IO;
using Verse;

namespace RimWorldOnlineCity.ClientHashCheck
{
    /// <summary>
    /// Фабрика створення екземплярів ClientFileChecker для різних каталогів гри (конфіги, моди, файли гри).
    /// </summary>
    internal class ClientFileCheckerFactory
    {
        /// <summary>
        /// Створює сконфігурований екземпляр перевіряльника файлів для заданого типу теки.
        /// </summary>
        public ClientFileChecker GetFileChecker(FolderType folderType)
        {
            var fc = new FolderCheck { FolderType = folderType };

            switch (folderType)
            {
                case FolderType.ModsConfigPath:
                    return new ClientFileChecker(fc, GenFilePaths.ConfigFolderPath)
                    {
                        OnChangeFolderAction = UpdateStatusWindow,
                    };

                case FolderType.ModsFolder:
                    return new ClientFileChecker(fc, GenFilePaths.ModsFolderPath)
                    {
                        OnChangeFolderAction = UpdateStatusWindow,
                    };

                case FolderType.GamePath:
                    return new ClientFileChecker(fc, Path.GetDirectoryName(GenFilePaths.ModsFolderPath))
                    {
                        OnChangeFolderAction = UpdateStatusWindow,
                    };
            }

            return null;
        }

        /// <summary>
        /// Оновлення тексту поточної директорії у вікні перевірки файлів.
        /// </summary>
        private static void UpdateStatusWindow(string folderName, int folderIndex)
        {
            UpdateModsWindow.HashStatus = folderName;
        }
    }
}