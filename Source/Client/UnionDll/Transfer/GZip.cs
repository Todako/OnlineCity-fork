using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;

namespace Util
{
    public static partial class GZip
    {
        [ThreadStatic]
        private static BinaryFormatter formatter = null;

        [ThreadStatic]
        public static long LastSizeObj;

        // Потокобезпечний буфер для копіювання даних без виділення пам'яті в кучі
        [ThreadStatic]
        private static byte[] copyBuffer;

        // Перевикористовуваний потік для серіалізації об'єктів без каскадних перевиділень пам'яті
        [ThreadStatic]
        private static MemoryStream serializeStream;

        [ThreadStatic]
        private static bool isSerializing;

        public static void CopyTo(Stream src, Stream dest)
        {
            if (src == null || dest == null) return;

            // 16 КБ буфер замість 4 КБ: вчетверо менше ітерацій циклу та нуль алокацій у GC
            if (copyBuffer == null)
            {
                copyBuffer = new byte[16384];
            }

            int cnt;
            while ((cnt = src.Read(copyBuffer, 0, copyBuffer.Length)) != 0)
            {
                dest.Write(copyBuffer, 0, cnt);
            }
        }

        public static string Zip(string str)
        {
            if (string.IsNullOrEmpty(str)) return string.Empty;
            return Convert.ToBase64String(ZipByte(str));
        }

        public static byte[] ZipByteByte(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return new byte[0];
            using (var msi = new MemoryStream(bytes, false))
            {
                return ZipStreamByte(msi);
            }
        }

        public static byte[] ZipByte(string str)
        {
            if (string.IsNullOrEmpty(str)) return new byte[0];
            var bytes = Encoding.UTF8.GetBytes(str);

            using (var msi = new MemoryStream(bytes, false))
            {
                return ZipStreamByte(msi);
            }
        }

        public static byte[] Serialize(object obj)
        {
            if (obj == null) return new byte[0];

            MemoryStream msi;
            bool useShared = !isSerializing;

            if (useShared)
            {
                isSerializing = true;
                if (serializeStream == null)
                {
                    serializeStream = new MemoryStream(64 * 1024);
                }
                else if (serializeStream.Capacity > 4 * 1024 * 1024)
                {
                    serializeStream = new MemoryStream(64 * 1024);
                }
                else
                {
                    serializeStream.SetLength(0);
                    serializeStream.Position = 0;
                }
                msi = serializeStream;
            }
            else
            {
                msi = new MemoryStream(64 * 1024);
            }

            try
            {
                if (formatter == null) formatter = new BinaryFormatter();
                formatter.Serialize(msi, obj);
                LastSizeObj = msi.Length;
                return msi.ToArray();
            }
            finally
            {
                if (useShared)
                {
                    isSerializing = false;
                }
                else
                {
                    msi.Dispose();
                }
            }
        }

        public static byte[] ZipObjByte(object obj)
        {
            if (obj == null) return new byte[0];

            MemoryStream msi;
            bool useShared = !isSerializing;

            if (useShared)
            {
                isSerializing = true;
                if (serializeStream == null)
                {
                    // Початкова місткість 64 КБ запобігає множинним подвоєнням масиву
                    serializeStream = new MemoryStream(64 * 1024);
                }
                else if (serializeStream.Capacity > 4 * 1024 * 1024)
                {
                    // Якщо пакет був гігантським (>4 МБ), скидаємо буфер, щоб не утримувати RAM
                    serializeStream = new MemoryStream(64 * 1024);
                }
                else
                {
                    serializeStream.SetLength(0);
                    serializeStream.Position = 0;
                }
                msi = serializeStream;
            }
            else
            {
                msi = new MemoryStream(64 * 1024);
            }

            try
            {
                if (formatter == null) formatter = new BinaryFormatter();
                formatter.Serialize(msi, obj);
                LastSizeObj = msi.Length;
                msi.Position = 0;
                return ZipStreamByte(msi);
            }
            finally
            {
                if (useShared)
                {
                    isSerializing = false;
                }
                else
                {
                    msi.Dispose();
                }
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
                lastStream = new MemoryStream(getContent(list[index]), false);
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
            using (var msi = new MemoryStream(bytes, false))
            {
                return UnzipStreamByte(msi);
            }
        }

        public static string UnzipByte(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            using (var msi = new MemoryStream(bytes, false))
            {
                byte[] bs = UnzipStreamByte(msi);
                return Encoding.UTF8.GetString(bs);
            }
        }

        public static object Deserialize(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            using (var msi = new MemoryStream(bytes, false))
            {
                if (formatter == null) formatter = new BinaryFormatter();
                return formatter.Deserialize(msi);
            }
        }

        public static object UnzipObjByte(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;

            using (var msi = new MemoryStream(bytes, false))
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
