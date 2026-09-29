using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Text;

namespace Util
{
    public static partial class GZip
    {
        static GZip()
        {
            ZipConstants.DefaultCodePage = Encoding.UTF8.CodePage;
        }

        /// <summary>
        /// Фіксована дата для службових заголовків ZipEntry.
        /// Усуває постійні запити DateTime.Now з конвертацією часового поясу ОС.
        /// </summary>
        private static readonly DateTime StaticPacketDateTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Потокобезпечний буфер копіювання 64 КБ.
        /// ОПТИМІЗАЦІЯ: повністю ліквідує виділення new byte[4096] на кожен пакет.
        /// </summary>
        [ThreadStatic]
        private static byte[] s_ZipCopyBuffer;

        private static byte[] GetCopyBuffer()
        {
            if (s_ZipCopyBuffer == null)
            {
                s_ZipCopyBuffer = new byte[65536];
            }
            return s_ZipCopyBuffer;
        }

        /// <summary>
        /// Стискає вхідний потік у ZIP-потік.
        /// ОПТИМІЗАЦІЯ: точний розрахунок ємності MemoryStream та блоковий запис.
        /// </summary>
        private static MemoryStream CreateToStream(Stream memStreamIn, string zipEntryName)
        {
            int initialCapacity = memStreamIn != null && memStreamIn.CanSeek && memStreamIn.Length > 0
                ? (int)Math.Min(memStreamIn.Length + 128, 16 * 1024 * 1024)
                : 4096;

            var outputMemStream = new MemoryStream(initialCapacity);
            using (var zipStream = new ZipOutputStream(outputMemStream))
            {
                zipStream.IsStreamOwner = false;
                zipStream.SetLevel(3); // Рівень 3 — оптимальний баланс CPU / розмір

                var newEntry = new ZipEntry(zipEntryName)
                {
                    DateTime = StaticPacketDateTime
                };

                zipStream.PutNextEntry(newEntry);

                if (memStreamIn != null && memStreamIn.Length > 0)
                {
                    StreamUtils.Copy(memStreamIn, zipStream, GetCopyBuffer());
                }

                zipStream.CloseEntry();
                zipStream.Close();
            }

            outputMemStream.Position = 0;
            return outputMemStream;
        }

        private static MemoryStream CreateToStream(Func<Stream> getMemStreamIn, Func<string> getZipEntryName)
        {
            var outputMemStream = new MemoryStream(65536);
            using (var zipStream = new ZipOutputStream(outputMemStream))
            {
                zipStream.IsStreamOwner = false;
                zipStream.SetLevel(3);

                var buffer = GetCopyBuffer();

                while (true)
                {
                    var zipEntryName = getZipEntryName();
                    if (zipEntryName == null) break;
                    var memStreamIn = getMemStreamIn();

                    var newEntry = new ZipEntry(zipEntryName)
                    {
                        DateTime = StaticPacketDateTime
                    };

                    zipStream.PutNextEntry(newEntry);

                    if (memStreamIn != null)
                    {
                        StreamUtils.Copy(memStreamIn, zipStream, buffer);
                    }

                    zipStream.CloseEntry();
                }

                zipStream.Close();
            }

            outputMemStream.Position = 0;
            return outputMemStream;
        }

        /// <summary>
        /// Розпаковує ZIP-потік у вихідний MemoryStream.
        /// ОПТИМІЗАЦІЯ: використання using гарантовано очищає внутрішні буфери розпакування.
        /// </summary>
        private static MemoryStream UnpackFromStream(Stream zipStream)
        {
            var outputMemStream = new MemoryStream(GetInitialCapacity());

            if (zipStream != null && zipStream.Length > 0)
            {
                using (var zipInputStream = new ZipInputStream(zipStream))
                {
                    zipInputStream.IsStreamOwner = false;
                    var entry = zipInputStream.GetNextEntry();
                    if (entry != null)
                    {
                        StreamUtils.Copy(zipInputStream, outputMemStream, GetCopyBuffer());
                    }
                }
            }

            outputMemStream.Position = 0;
            return outputMemStream;
        }
    }
}