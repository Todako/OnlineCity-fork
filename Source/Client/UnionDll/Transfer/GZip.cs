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
        private static readonly byte[] EmptyByteArray = new byte[0];

        [ThreadStatic]
        private static BinaryFormatter formatter = null;

        [ThreadStatic]
        public static long LastSizeObj;

        // Потокобезпечний реюзабельний потік для серіалізації без виділення нових масивів у купі GC
        [ThreadStatic]
        private static MemoryStream t_SerializeStream;

        /// <summary>
        /// Початковий розмір буфера: для звичайних пакетів встановлюється 8–64 КБ (строго нижче межі LOH 85 КБ).
        /// </summary>
        private static int GetInitialCapacity()
        {
            if (LastSizeObj <= 0) return 8192;
            if (LastSizeObj > 64 * 1024) return 64 * 1024;
            return (int)LastSizeObj;
        }

        /// <summary>
        /// Отримує готовий очищений потік для поточного потоку виконання із захистом від LOH-витоків.
        /// </summary>
        private static MemoryStream GetSerializeStream()
        {
            if (t_SerializeStream == null || !t_SerializeStream.CanWrite)
            {
                t_SerializeStream = new MemoryStream(GetInitialCapacity());
            }
            else
            {
                // Якщо потік розрісся понад 8 МБ після важкого збереження карти, скидаємо його,
                // щоб не тримати зайву пам'ять у фоні
                if (t_SerializeStream.Capacity > 8 * 1024 * 1024)
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
            if (bytes == null || bytes.Length == 0) return EmptyByteArray;
            using (var msi = new MemoryStream(bytes, 0, bytes.Length, false, true))
            {
                return ZipStreamByte(msi);
            }
        }

        public static byte[] ZipByte(string str)
        {
            if (string.IsNullOrEmpty(str)) return EmptyByteArray;
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
            if (obj == null) return EmptyByteArray;
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
            if (obj == null) return EmptyByteArray;
            var msi = GetSerializeStream();
            if (formatter == null) formatter = new BinaryFormatter();
            formatter.Serialize(msi, obj);
            LastSizeObj = msi.Length;
            msi.Position = 0;
            return ZipStreamByte(msi);
        }

        public static byte[] ZipStreamByte(Stream msi)
        {
            if (msi == null) return EmptyByteArray;
            using (var mso = CreateToStream(msi, "data"))
            {
                return mso.ToArray();
            }
        }

        public static byte[] ZipMoreByteByte(string[] list, Func<string, byte[]> getContent)
        {
            if (list == null || list.Length == 0) return EmptyByteArray;
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
                var content = getContent(list[index]);
                if (content == null || content.Length == 0)
                {
                    lastStream = new MemoryStream(EmptyByteArray, false);
                }
                else
                {
                    lastStream = new MemoryStream(content, 0, content.Length, false, true);
                }
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
            if (bytes == null || bytes.Length == 0) return EmptyByteArray;
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
            LastSizeObj = bytes.Length;
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
            if (msi == null) return EmptyByteArray;
            using (var mso = UnpackFromStream(msi))
            {
                return mso.ToArray();
            }
        }
    }
}