using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Util
{
    /// <summary>
    /// Утиліта для швидкої бінарної серіалізації, десеріалізації
    /// та стиснення/розпакування даних через алгоритми Deflate/GZip.
    /// </summary>
    public static partial class GZip
    {
        [ThreadStatic]
        private static BinaryFormatter formatter = null;

        [ThreadStatic]
        public static long LastSizeObj;

        /// <summary>
        /// Евристичний розрахунок початкового розміру буфера на основі попереднього пакета.
        /// Запобігає лавиноподібному розширенню MemoryStream (подвоєнню буфера та виділенню зайвої пам'яті).
        /// </summary>
        private static int GetInitialCapacity()
        {
            if (LastSizeObj <= 0) return 4096;
            if (LastSizeObj > 1024 * 1024 * 16) return 1024 * 1024 * 16; // Обмежуємо початкову планку 16 МБ
            return (int)LastSizeObj;
        }

        /// <summary>
        /// Швидке копіювання одного потоку в інший зі збільшеним буфером 64 КБ.
        /// ОПТИМІЗАЦІЯ: усунено створення масиву new byte[4096] на кожен виклик.
        /// </summary>
        public static void CopyTo(Stream src, Stream dest)
        {
            src.CopyTo(dest, 65536);
        }

        public static string Zip(string str)
        {
            return Convert.ToBase64String(ZipByte(str));
        }

        public static byte[] ZipByteByte(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
                return ZipStreamByte(msi);
        }

        public static byte[] ZipByte(string str)
        {
            var bytes = Encoding.UTF8.GetBytes(str);
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
                return ZipStreamByte(msi);
        }

        /// <summary>
        /// Серіалізація об'єкта в бінарний масив через кешований BinaryFormatter.
        /// ОПТИМІЗАЦІЯ: початкова ємність задається заздалегідь, запобігаючи повторним виділенням буферів.
        /// </summary>
        public static byte[] Serialize(object obj)
        {
            using (var msi = new MemoryStream(GetInitialCapacity()))
            {
                if (formatter == null) formatter = new BinaryFormatter();
                formatter.Serialize(msi, obj);
                LastSizeObj = msi.Length;
                return msi.ToArray();
            }
        }

        /// <summary>
        /// Повна бінарна серіалізація об'єкта з наступним GZip-стисненням.
        /// </summary>
        public static byte[] ZipObjByte(object obj)
        {
            using (var msi = new MemoryStream(GetInitialCapacity()))
            {
                if (formatter == null) formatter = new BinaryFormatter();
                formatter.Serialize(msi, obj);
                LastSizeObj = msi.Length;
                msi.Position = 0;
                return ZipStreamByte(msi);
            }
        }

        public static byte[] ZipStreamByte(Stream msi)
        {
            using (var mso = CreateToStream(msi, "data"))
            {
                return mso.ToArray();
            }
        }

        public static byte[] ZipMoreByteByte(string[] list, Func<string, byte[]> getContent)
        {
            var index = -1;
            Stream lastStream = null;
            Func<string> getZipEntryName = () =>
            {
                if (++index >= list.Length) return null;
                return Path.GetFileName(list[index]);
            };
            Func<Stream> getMemStreamIn = () =>
            {
                if (lastStream != null) lastStream.Dispose();
                lastStream = new MemoryStream(getContent(list[index]));
                return lastStream;
            };

            try
            {
                using (var mso = CreateToStream(getMemStreamIn, getZipEntryName))
                {
                    return mso.ToArray();
                }
            }
            finally
            {
                if (lastStream != null) lastStream.Dispose();
            }
        }

        public static string Unzip(string str)
        {
            return UnzipByte(Convert.FromBase64String(str));
        }

        public static byte[] UnzipByteByte(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                return UnzipStreamByte(msi);
            }
        }

        public static string UnzipByte(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                byte[] bs = UnzipStreamByte(msi);
                return Encoding.UTF8.GetString(bs);
            }
        }

        public static object Deserialize(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                if (formatter == null) formatter = new BinaryFormatter();
                return formatter.Deserialize(msi);
            }
        }

        /// <summary>
        /// Розпакування GZip-потоку та десеріалізація в об'єкт.
        /// </summary>
        public static object UnzipObjByte(byte[] bytes)
        {
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            using (var mso = UnpackFromStream(msi))
            {
                LastSizeObj = mso.Length;
                mso.Position = 0;
                if (formatter == null) formatter = new BinaryFormatter();
                return formatter.Deserialize(mso);
            }
        }

        public static byte[] UnzipStreamByte(Stream msi)
        {
            using (var mso = UnpackFromStream(msi))
            {
                return mso.ToArray();
            }
        }
    }
}