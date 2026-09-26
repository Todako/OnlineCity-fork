using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OCUnion.Transfer.Model
{
    [Serializable]
    public class ModelModsFilesRequest
    {
        /// <summary>
        /// Дерево каталогів, яке потрібно відновити.
        /// </summary>
        public FolderType FolderType { get; set; }

        /// <summary>
        /// Після основного запиту на файли з директорії з 0, ідуть запити на синхронізацію XML-файлів.
        /// </summary>
        public int NumberFileRequest { get; set; }

        public int CodeRequest => (int)FolderType * 1000 + NumberFileRequest;

        /// <summary>
        /// Файли, які знаходяться в цих директоріях.
        /// </summary>
        public List<ModelFileInfo> Files { get; set; }
    }

    [Serializable]
    public class ModelModsFilesResponse
    {
        public FolderCheck Folder { get; set; }

        /// <summary>
        /// Дерево каталогів, яке потрібно відновити.
        /// </summary>
        public FoldersTree FoldersTree { get; set; }

        /// <summary>
        /// Файли, які знаходяться в цих директоріях.
        /// </summary>
        public List<ModelFileInfo> Files { get; set; }

        /// <summary>
        /// Який обсяг даних залишився до відправки без урахування поточного пакета.
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// Якщо вказано, то в Path шлях до XML-файлу, вміст зазначених тегів якого не порівнюється.
        /// </summary>
        public List<string> IgnoreTag { get; set; }
    }

    [Serializable]
    public class FolderCheck
    {
        public FolderType FolderType { get; set; }

        /// <summary>
        /// Повний шлях на сервері.
        /// </summary>
        public string ServerPath { get; set; }

        /// <summary>
        /// Чи можна замінити файл за вмістом із сервера.
        /// </summary>
        public bool NeedReplace { get; set; }

        /// <summary>
        /// Якщо вказано, то в Path шлях до XML-файлу, вміст зазначених тегів якого не порівнюється.
        /// </summary>
        public List<string> IgnoreTag { get; set; }

        public string XMLFileName { get; set; }

        /// <summary>
        /// Ігнорувати файли з указаним ім'ям.
        /// </summary>
        public List<string> IgnoreFile { get; set; }

        /// <summary>
        /// Ігнорувати підпапки з указаним ім'ям.
        /// </summary>
        public List<string> IgnoreFolder { get; set; }
    }

    [Serializable]
    public enum FolderType
    {
        [Description("Configs folder")]
        ModsConfigPath,
        [Description("Game folder")]
        GamePath,
        [Description("Mods folder")]
        ModsFolder,
    }
}