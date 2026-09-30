using OCUnion;
using System;
using System.Collections.Generic;
using System.IO;
using Util;

namespace ServerOnlineCity
{
    public class RepositorySaveData
    {
        /// <summary>
        /// Довжина історії збережень користувача (максимальна кількість файлів із колонією користувача, починаючи з 1).
        /// </summary>
        public int CountSaveDataPlayer { get; } = 3;

        private readonly Repository MainRepository;

        public RepositorySaveData(Repository repository)
        {
            MainRepository = repository;
        }

        private string GetFileNameBase(string login)
        {
            return Path.Combine(MainRepository.SaveFolderDataPlayers, Repository.NormalizeLogin(login) + ".dat");
        }

        /// <summary>
        /// Отримує дані збереження гри користувача.
        /// </summary>
        /// <param name="login">Логін користувача, на основі якого формується ім'я файлу.</param>
        /// <param name="numberSave">Номер збереження: від 1 (найновіше) до CountSaveDataPlayer (найстаріше). Якщо такого файлу немає, повертається найстаріший наявний.</param>
        /// <returns>Вміст збереження гри або null, якщо немає жодного файлу з даними.</returns>
        public byte[] LoadPlayerData(string login, int numberSave)
        {
            if (numberSave < 1 || numberSave > CountSaveDataPlayer) return null;

            var fileName = GetFileNameBase(login) + numberSave.ToString().NormalizePath();
            if (!File.Exists(fileName)) return null;

            // Зчитуємо дані збереження з диска за один прохід
            var saveFileData = File.ReadAllBytes(fileName);
            if (saveFileData.Length < 10) return null;

            // Перевіряємо формат: відкритий XML чи стиснений GZip-архів
            if (IsXmlData(saveFileData))
            {
                return saveFileData;
            }
            else
            {
                return GZip.UnzipByteByte(saveFileData);
            }
        }

        /// <summary>
        /// Швидка перевірка сигнатури XML без виділення рядків і без конвертації кодування.
        /// </summary>
        private static bool IsXmlData(byte[] data)
        {
            if (data == null || data.Length < 5) return false;

            int offset = 0;
            // Облік можливого UTF-8 BOM (0xEF, 0xBB, 0xBF)
            if (data.Length >= 8 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                offset = 3;
            }

            return data[offset] == '<'
                && data[offset + 1] == '?'
                && data[offset + 2] == 'x'
                && data[offset + 3] == 'm'
                && data[offset + 4] == 'l';
        }

        /// <summary>
        /// Зберігає ігрові дані гравця. Уся наявна історія файлів перейменовується на номери +1. Файл із номером більше ніж CountSaveDataPlayer видаляється.
        /// </summary>
        /// <param name="login">Логін гравця, на основі якого формується ім'я файлу.</param>
        /// <param name="data">Вміст збереження гри.</param>
        /// <param name="single">Якщо встановлено, видаляється вся історія, залишаючи лише поточне збереження та останній файл з розширенням .bak (для можливості ручного відновлення адміністратором).</param>
        public void SavePlayerData(string login, byte[] data, bool single)
        {
            if (data == null || data.Length < 10) return;

            var fileNameBase = GetFileNameBase(login);
            var pFiles = GetListPlayerFiles(login);

            if (single)
            {
                if (pFiles.Count > 0)
                {
                    var bakupFileName = Path.Combine(Path.GetDirectoryName(pFiles[0]), Path.GetFileNameWithoutExtension(pFiles[0])) + ".bak";
                    if (File.Exists(bakupFileName)) DeleteFileAndBackup(bakupFileName);
                    File.Move(pFiles[0], bakupFileName);
                }
                for (int i = 1; i < pFiles.Count; i++) DeleteFileAndBackup(pFiles[i]);
            }
            else
            {
                // Забезпечуємо, щоб у pFiles[pFiles.Count - 1] було ім'я файлу, якого ще немає
                if (pFiles.Count == CountSaveDataPlayer)
                {
                    DeleteFileAndBackup(pFiles[pFiles.Count - 1]);
                }
                else
                {
                    pFiles.Add(fileNameBase + (pFiles.Count + 1).ToString());
                }

                for (int i = pFiles.Count - 2; i >= 0; i--)
                {
                    File.Move(pFiles[i], pFiles[i + 1]);
                }
            }

            var fileName = fileNameBase + "1";
            byte[] dataToSave = GZip.ZipByteByte(data);

            File.WriteAllBytes(fileName, dataToSave);
            Loger.Log("Server User " + Path.GetFileNameWithoutExtension(fileName) + " saved.");
        }

        /// <summary>
        /// Видаляє файл, але перед цим зберігає його копію так, щоб залишалася копія старша за 12 годин.
        /// </summary>
        private void DeleteFileAndBackup(string fileName)
        {
            var fi = new FileInfo(fileName);
            if (!fi.Exists) return;

            if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalHours < 12)
            {
                fi.Delete();
                return;
            }

            var bakupFileName = Path.Combine(Path.GetDirectoryName(fileName), Path.GetFileNameWithoutExtension(fileName));
            var d1 = new FileInfo(bakupFileName + ".day1");
            var d2 = new FileInfo(bakupFileName + ".day2");

            if (!d1.Exists || (fi.LastWriteTimeUtc - d1.LastWriteTimeUtc).TotalHours > 12)
            {
                // Потрібно записати файл fileName у fileName.day1
                if (d1.Exists)
                {
                    // Обробляємо наявний fileName.day1
                    if (!d2.Exists || (d1.LastWriteTimeUtc - d2.LastWriteTimeUtc).TotalHours > 12)
                    {
                        // Потрібно перемістити fileName.day1 у fileName.day2
                        if (d2.Exists)
                        {
                            d2.Delete();
                        }
                        File.Move(d1.FullName, d2.FullName);
                    }
                    else
                    {
                        d1.Delete();
                    }
                }
                File.Move(fi.FullName, d1.FullName);
            }
            else
            {
                fi.Delete();
            }
        }

        public void DeletePlayerData(string login)
        {
            var pFiles = GetListPlayerFiles(login);

            if (pFiles.Count > 0)
            {
                var bakupFileName = Path.Combine(Path.GetDirectoryName(pFiles[0]), Path.GetFileNameWithoutExtension(pFiles[0])) + ".bak";
                if (File.Exists(bakupFileName)) File.Delete(bakupFileName);
                File.Move(pFiles[0], bakupFileName);
            }

            for (int i = 1; i < pFiles.Count; i++)
            {
                File.Delete(pFiles[i]);
            }
        }

        /// <summary>
        /// Повертає список доступної історії збережень гравця. У кожному рядку — дата збереження за часом сервера.
        /// </summary>
        /// <param name="login">Логін гравця. Індекс 0 відповідає номеру 1, індекс 1 — номеру 2 тощо.</param>
        public List<string> GetListPlayerDatas(string login)
        {
            var files = GetListPlayerFiles(login);
            var result = new List<string>(files.Count);

            for (int i = 0; i < files.Count; i++)
            {
                result.Add(File.GetLastWriteTime(files[i]).ToString("yyyy-MM-dd"));
            }

            return result;
        }

        private List<string> GetListPlayerFiles(string login)
        {
            var result = new List<string>(CountSaveDataPlayer);
            var fileNameBase = GetFileNameBase(login);

            for (int num = 1; num <= CountSaveDataPlayer; num++)
            {
                var currentFile = fileNameBase + num.ToString();
                if (!File.Exists(currentFile)) break;
                result.Add(currentFile);
            }

            if (result.Count == 0 && File.Exists(fileNameBase))
            {
                File.Move(fileNameBase, fileNameBase + "1");
                result.Add(fileNameBase + "1");
            }

            return result;
        }
    }
}