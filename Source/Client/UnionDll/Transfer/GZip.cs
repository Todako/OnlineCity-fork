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

        // Потокобезпечний реюзабельний потік для серіалізації без виділення нових масивів у купі GC
        [ThreadStatic]
        private static MemoryStream t_SerializeStream;

        /// <summary>
        /// Евристичний розрахунок початкового розміру буфера на основі попереднього пакета.
        /// Запобігає подвоєнню ємності MemoryStream під час запису.
        /// </summary>
        private static int GetInitialCapacity()
        {
            if (LastSizeObj <= 0) return 4096;
            if (LastSizeObj > 1024 * 1024 * 16) return 1024 * 1024 * 16; // Обмежуємо початкову планку 16 МБ
            return (int)LastSizeObj;
        }

        /// <summary>
        /// Отримує готовий очищений потік для поточного потоку виконання.
        /// </summary>
        private static MemoryStream GetSerializeStream()
        {
            if (t_SerializeStream == null)
            {
                t_SerializeStream = new MemoryStream(GetInitialCapacity());
            }
            else
            {
                // Якщо потік розрісся понад 16 МБ після важкого збереження карти, перестворюємо його,
                // щоб не тримати зайву пам'ять у фоні
                if (t_SerializeStream.Capacity > 16 * 1024 * 1024)
                {
                    t_SerializeStream.Dispose();
                    t_SerializeStream = new MemoryStream(GetInitialCapacity());
                }
                else
                {
                    t_SerializeStream.SetLength(0);
                    t_SerializeStream.Position = 0;
                }
            }
            return t_SerializeStream;
        }

        /// <summary>
        /// Швидке копіювання одного потоку в інший зі збільшеним буфером 64 КБ.
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
            if (bytes == null || bytes.Length == 0) return new byte[0];
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                return ZipStreamByte(msi);
            }
        }

        public static byte[] ZipByte(string str)
        {
            if (string.IsNullOrEmpty(str)) return new byte[0];
            var bytes = Encoding.UTF8.GetBytes(str);
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                return ZipStreamByte(msi);
            }
        }

        /// <summary>
        /// Серіалізація об'єкта в бінарний масив через кешований BinaryFormatter.
        /// </summary>
        public static byte[] Serialize(object obj)
        {
            var msi = GetSerializeStream();
            if (formatter == null) formatter = new BinaryFormatter();
            formatter.Serialize(msi, obj);
            LastSizeObj = msi.Length;
            return msi.ToArray();
        }

        /// <summary>
        /// Повна бінарна серіалізація об'єкта з наступним GZip-стисненням.
        /// </summary>
        public static byte[] ZipObjByte(object obj)
        {
            var msi = GetSerializeStream();
            if (formatter == null) formatter = new BinaryFormatter();
            formatter.Serialize(msi, obj);
            LastSizeObj = msi.Length;
            msi.Position = 0;
            return ZipStreamByte(msi);
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
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return UnzipByte(Convert.FromBase64String(str));
        }

        public static byte[] UnzipByteByte(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new byte[0];
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                return UnzipStreamByte(msi);
            }
        }

        public static string UnzipByte(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                byte[] bs = UnzipStreamByte(msi);
                return Encoding.UTF8.GetString(bs);
            }
        }

        public static object Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
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
            if (bytes == null || bytes.Length == 0) return null;
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