using OCUnion;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RimWorldOnlineCity
{
    /// <summary>
    /// Безпечна кросплатформна служба читання зображень із системного буфера обміну.
    /// Автоматично конвертує системні DIB/скриншоти у валідний формат PNG, сумісний з Unity.
    /// </summary>
    public class AncillaryUtil
    {
        public AncillaryUtil() { }

        /// <summary>
        /// Отримує байти зображення (PNG/JPG) з буфера обміну для аватарки.
        /// </summary>
        public byte[] GetClipboardImageData()
        {
            try
            {
                // 1. Перевірка: чи скопійовано прямий шлях до файлу картинки
                var pathData = TryGetImagePathFromTextClipboard();
                if (pathData != null && pathData.Length > 0)
                {
                    return pathData;
                }

                // 2. Специфічне для платформи зчитування
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        return GetClipboardImageWindows();

                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                        return GetClipboardImageLinux();

                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.OSXEditor:
                        return GetClipboardImageMacOS();

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Loger.Log("AncillaryUtil GetClipboardImageData exception: " + ex.Message, Loger.LogLevel.DEBUG);
                return null;
            }
        }

        #region Перевірка текстового буфера на шлях до файлу

        private static byte[] TryGetImagePathFromTextClipboard()
        {
            try
            {
                var text = GUIUtility.systemCopyBuffer?.Trim('"', ' ', '\t', '\r', '\n');
                if (!string.IsNullOrEmpty(text) && text.Length < 300 && IsImageFile(text) && File.Exists(text))
                {
                    var fi = new FileInfo(text);
                    if (fi.Length > 0 && fi.Length < 15 * 1024 * 1024)
                    {
                        return File.ReadAllBytes(text);
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;

            return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Реалізація для Windows (Win32 API + Unity PNG Encoder)

        private const uint CF_DIB = 8;
        private const uint CF_HDROP = 15;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterClipboardFormat(string lpszFormat);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr hMem);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        private static uint _cfPng = 0;
        private static uint CF_PNG => _cfPng != 0 ? _cfPng : (_cfPng = RegisterClipboardFormat("PNG"));

        private static byte[] GetClipboardImageWindows()
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;

            try
            {
                // Сценарій 1: Скопійовано файл із Провідника Windows (CF_HDROP)
                if (IsClipboardFormatAvailable(CF_HDROP))
                {
                    var hDrop = GetClipboardData(CF_HDROP);
                    if (hDrop != IntPtr.Zero)
                    {
                        var sb = new StringBuilder(1024);
                        if (DragQueryFile(hDrop, 0, sb, (uint)sb.Capacity) > 0)
                        {
                            var filePath = sb.ToString();
                            if (File.Exists(filePath) && IsImageFile(filePath))
                            {
                                return File.ReadAllBytes(filePath);
                            }
                        }
                    }
                }

                // Сценарій 2: Браузер (Chrome, Firefox, Edge), Discord, Telegram або «Ножиці» Windows 10/11
                // Вони кладуть у буфер нативний потік PNG
                uint cfPng = CF_PNG;
                if (cfPng != 0 && IsClipboardFormatAvailable(cfPng))
                {
                    var hPng = GetClipboardData(cfPng);
                    if (hPng != IntPtr.Zero)
                    {
                        var pPng = GlobalLock(hPng);
                        if (pPng != IntPtr.Zero)
                        {
                            try
                            {
                                int size = (int)GlobalSize(hPng).ToUInt32();
                                if (size > 0)
                                {
                                    byte[] pngBytes = new byte[size];
                                    Marshal.Copy(pPng, pngBytes, 0, size);
                                    return pngBytes;
                                }
                            }
                            finally
                            {
                                GlobalUnlock(hPng);
                            }
                        }
                    }
                }

                // Сценарій 3: Скриншот PrintScreen, Paint або старі версії графічних редакторів (CF_DIB)
                if (IsClipboardFormatAvailable(CF_DIB))
                {
                    var hDib = GetClipboardData(CF_DIB);
                    if (hDib == IntPtr.Zero) return null;

                    var pDib = GlobalLock(hDib);
                    if (pDib == IntPtr.Zero) return null;

                    try
                    {
                        int dibSize = (int)GlobalSize(hDib).ToUInt32();
                        if (dibSize <= 40) return null;

                        byte[] dibData = new byte[dibSize];
                        Marshal.Copy(pDib, dibData, 0, dibSize);

                        return ConvertDibToPng(dibData);
                    }
                    finally
                    {
                        GlobalUnlock(hDib);
                    }
                }
            }
            catch (Exception ex)
            {
                Loger.Log("AncillaryUtil Windows read error: " + ex.Message, Loger.LogLevel.DEBUG);
            }
            finally
            {
                CloseClipboard();
            }

            return null;
        }

        /// <summary>
        /// Парсить сирий буфер DIB, відновлює кольори та кодує у валідний PNG засобами рушія Unity.
        /// </summary>
        private static byte[] ConvertDibToPng(byte[] dibData)
        {
            if (dibData == null || dibData.Length < 40) return null;

            int headerSize = BitConverter.ToInt32(dibData, 0);
            if (headerSize < 40) return null;

            int width = BitConverter.ToInt32(dibData, 4);
            int rawHeight = BitConverter.ToInt32(dibData, 8);
            int height = Math.Abs(rawHeight);
            bool isBottomUp = rawHeight > 0;

            short bitCount = BitConverter.ToInt16(dibData, 14);
            int compression = BitConverter.ToInt32(dibData, 16);
            int clrUsed = BitConverter.ToInt32(dibData, 32);

            if (width <= 0 || height <= 0) return null;
            if (bitCount != 24 && bitCount != 32) return null; // Підтримка 24 та 32-бітних скриншотів

            int pixelOffset = headerSize;
            if (compression == 3 && headerSize == 40) // BI_BITFIELDS
            {
                pixelOffset += 12;
            }
            if (clrUsed > 0)
            {
                pixelOffset += clrUsed * 4;
            }

            if (pixelOffset >= dibData.Length) return null;

            int rowStride = ((width * bitCount + 31) / 32) * 4;
            Color32[] pixels = new Color32[width * height];
            bool hasNonZeroAlpha = false;

            for (int y = 0; y < height; y++)
            {
                int dibRow = isBottomUp ? y : (height - 1 - y);
                int rowStart = pixelOffset + dibRow * rowStride;

                if (rowStart + (width * (bitCount / 8)) > dibData.Length) break;

                int targetRowStart = y * width;

                if (bitCount == 32)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = rowStart + (x << 2);
                        byte b = dibData[p];
                        byte g = dibData[p + 1];
                        byte r = dibData[p + 2];
                        byte a = dibData[p + 3];

                        if (a != 0) hasNonZeroAlpha = true;
                        pixels[targetRowStart + x] = new Color32(r, g, b, a);
                    }
                }
                else // 24 біти
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = rowStart + x * 3;
                        byte b = dibData[p];
                        byte g = dibData[p + 1];
                        byte r = dibData[p + 2];
                        pixels[targetRowStart + x] = new Color32(r, g, b, 255);
                    }
                }
            }

            // Виправлення багу Windows GDI: якщо у скриншоті альфа-канал повністю нульовий, робимо його непрозорим
            if (bitCount == 32 && !hasNonZeroAlpha)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i].a = 255;
                }
            }

            Texture2D tempTex = null;
            try
            {
                tempTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tempTex.SetPixels32(pixels);
                tempTex.Apply();
                return tempTex.EncodeToPNG();
            }
            finally
            {
                if (tempTex != null)
                {
                    UnityEngine.Object.Destroy(tempTex);
                }
            }
        }

        #endregion

        #region Реалізація для Linux (X11 / Wayland / SteamOS)

        private static byte[] GetClipboardImageLinux()
        {
            var bytes = RunProcessForBinaryOutput("wl-paste", "--type image/png");
            if (bytes != null && bytes.Length > 0) return bytes;

            bytes = RunProcessForBinaryOutput("xclip", "-selection clipboard -t image/png -out");
            if (bytes != null && bytes.Length > 0) return bytes;

            return null;
        }

        #endregion

        #region Реалізація для macOS (AppleScript / NSPasteboard)

        private static byte[] GetClipboardImageMacOS()
        {
            const string script = "try\n" +
                                  "set pngData to the clipboard as «class PNGf»\n" +
                                  "return pngData\n" +
                                  "end try";

            var hexOutput = RunProcessForTextOutput("osascript", $"-e \"{script}\"");
            if (string.IsNullOrEmpty(hexOutput) || !hexOutput.Contains("«data PNGf"))
            {
                return null;
            }

            int start = hexOutput.IndexOf("«data PNGf", StringComparison.Ordinal);
            if (start < 0) return null;
            start += 10;
            int end = hexOutput.IndexOf('»', start);
            if (end < 0) return null;

            string hex = hexOutput.Substring(start, end - start).Trim();
            if (hex.Length == 0 || hex.Length % 2 != 0) return null;

            byte[] rawBytes = new byte[hex.Length / 2];
            for (int i = 0; i < rawBytes.Length; i++)
            {
                rawBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return rawBytes;
        }

        #endregion

        #region Допоміжний запуск процесів для Unix

        private static byte[] RunProcessForBinaryOutput(string command, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(command, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return null;

                    using (var ms = new MemoryStream())
                    {
                        process.StandardOutput.BaseStream.CopyTo(ms);
                        if (!process.WaitForExit(1000))
                        {
                            try { process.Kill(); } catch { }
                            return null;
                        }

                        if (process.ExitCode == 0 && ms.Length > 0)
                        {
                            return ms.ToArray();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string RunProcessForTextOutput(string command, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(command, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null) return null;

                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(1500))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    return process.ExitCode == 0 ? output : null;
                }
            }
            catch { }
            return null;
        }

        #endregion
    }
}