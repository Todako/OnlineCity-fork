using OCUnion;
using System;
using System.Net;
using System.Net.Sockets;

namespace Transfer
{
    /// <summary>
    /// Серверний слухач вхідних TCP-з'єднань.
    /// Забезпечує миттєвий подієвий прийом підключень без polling-затримок.
    /// </summary>
    public class ConnectServer
    {
        public Action<ConnectClient> ConnectionAccepted;
        private volatile bool _isRunning;
        private TcpListener _listener;

        public void Start(string hostname, int port)
        {
            IPAddress ipAddress;
            try
            {
                if (string.IsNullOrEmpty(hostname) || hostname == "*")
                {
                    ipAddress = IPAddress.IPv6Any;
                }
                else
                {
                    var addresses = Dns.GetHostAddresses(hostname);
                    if (addresses == null || addresses.Length == 0)
                    {
                        throw new ArgumentException("Unable to obtain the IP address for the host: " + hostname);
                    }
                    ipAddress = addresses[0];
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Unable to retrieve the server's IP address based on its name", nameof(hostname), ex);
            }

            _listener = new TcpListener(ipAddress, port);

            // Dual-stack налаштовується виключно на IPv6-сокетах для уникнення помилок на IPv4
            if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                try
                {
                    _listener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                }
                catch (Exception ex)
                {
                    Loger.Log("ConnectServer: IPv6 dual-stack not supported by the system: " + ex.Message, Loger.LogLevel.DEBUG);
                }
            }

            try
            {
                _listener.Start();
                _isRunning = true;

                Loger.Log($"ConnectServer launched on {ipAddress}:{port}");

                // Подієвий цикл очікування нових з'єднань (без Sleep/Pending затримок)
                while (_isRunning)
                {
                    TcpClient tcpClient;
                    try
                    {
                        tcpClient = _listener.AcceptTcpClient();
                    }
                    catch (SocketException) when (!_isRunning)
                    {
                        // Нормальне завершення при виклику Stop()
                        break;
                    }
                    catch (ObjectDisposedException) when (!_isRunning)
                    {
                        break;
                    }

                    if (tcpClient == null) continue;

                    try
                    {
                        var client = new ConnectClient(tcpClient);
                        ConnectionAccepted?.Invoke(client);
                    }
                    catch (Exception ex)
                    {
                        Loger.Log("ConnectServer: Error processing a connected client: " + ex.Message, Loger.LogLevel.WARNING);
                    }
                }
            }
            finally
            {
                _isRunning = false;
                try
                {
                    _listener?.Stop();
                }
                catch { }
                _listener = null;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            try
            {
                _listener?.Stop();
            }
            catch { }
        }
    }
}