using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Security.Authentication;
using System.Net.Security;

namespace AzerothCoreLauncher
{
    internal static class LauncherServer
    {
        internal const int Port = 8787;
        private static readonly ConcurrentDictionary<string, List<DateTime>> RegistrationAttempts =
            new ConcurrentDictionary<string, List<DateTime>>(StringComparer.Ordinal);
        private static readonly object LogLock = new object();
        private static readonly object RateLock = new object();
        private static readonly SemaphoreSlim Connections = new SemaphoreSlim(32);

        internal static int Run()
        {
            try
            {
                X509Certificate2 certificate = LoadOrCreateCertificate();
                TcpListener listener = new TcpListener(IPAddress.Any, Port);
                try
                {
                    listener.Start(128);
                    Log("Servicio de cuentas HTTPS iniciado; puerto=" + Port +
                        " huella=" + GetFingerprint(certificate));
                    while (true)
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        if (!Connections.Wait(0))
                        {
                            client.Close();
                            continue;
                        }
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try { HandleClient(client, certificate); }
                            finally { Connections.Release(); }
                        });
                    }
                }
                finally
                {
                    listener.Stop();
                }
            }
            catch (Exception ex)
            {
                Log("Servidor detenido: " + ex);
                return 1;
            }
        }

        internal static string GetFingerprint()
        {
            using (X509Certificate2 certificate = LoadOrCreateCertificate())
                return GetFingerprint(certificate);
        }

        private static string GetFingerprint(X509Certificate2 certificate)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(certificate.RawData)).Replace("-", String.Empty);
        }

        internal static bool EnsureServerRunning()
        {
            GetFingerprint();
            if (IsServerAvailable())
                return true;

            string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = executable;
            start.Arguments = "--serve";
            start.WorkingDirectory = Path.GetDirectoryName(executable);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WindowStyle = ProcessWindowStyle.Hidden;
            Process process = Process.Start(start);
            if (process == null)
                throw new InvalidOperationException("No se pudo iniciar el servicio LAN.");
            process.Dispose();

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Thread.Sleep(250);
                if (IsServerAvailable())
                    return true;
            }
            throw new InvalidOperationException("El servicio LAN no abrió el puerto 8787. Revisa el registro de servidor.");
        }

        private static bool IsServerAvailable()
        {
            try
            {
                string pin = GetFingerprint();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://127.0.0.1:" + Port + "/api/health");
                request.Proxy = null;
                request.Timeout = 1000;
                request.ReadWriteTimeout = 1000;
                request.ServerCertificateValidationCallback = delegate(object sender,
                    System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                    System.Security.Cryptography.X509Certificates.X509Chain chain,
                    System.Net.Security.SslPolicyErrors errors)
                {
                    if (certificate == null)
                        return false;
                    using (SHA256 sha = SHA256.Create())
                        return String.Equals(ManifestTools.ToHex(sha.ComputeHash(certificate.GetRawCertData())),
                            pin, StringComparison.OrdinalIgnoreCase);
                };
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                {
                    ApiResponse result = JsonTools.Deserialize<ApiResponse>(stream);
                    return response.StatusCode == HttpStatusCode.OK && result != null && result.Ok;
                }
            }
            catch (WebException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void HandleClient(TcpClient client, X509Certificate2 certificate)
        {
            using (client)
            using (NetworkStream network = client.GetStream())
            using (SslStream ssl = new SslStream(network, false))
            {
                try
                {
                    client.ReceiveTimeout = 120000;
                    client.SendTimeout = 120000;
                    ssl.AuthenticateAsServer(certificate, false, SslProtocols.Tls12, false);
                    string headersText = ReadHeaders(ssl);
                    string[] lines = headersText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] requestLine = lines[0].Split(' ');
                    if (requestLine.Length < 2)
                    {
                        SendText(ssl, 400, "Bad Request", "Solicitud no válida.");
                        return;
                    }

                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0)
                            headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                    }

                    string method = requestLine[0];
                    string target = requestLine[1];
                    int query = target.IndexOf('?');
                    string path = query < 0 ? target : target.Substring(0, query);
                    if (String.Equals(method, "GET", StringComparison.Ordinal))
                    {
                        HandleGet(ssl, path);
                        return;
                    }
                    if (String.Equals(method, "POST", StringComparison.Ordinal) &&
                        String.Equals(path, "/api/register", StringComparison.Ordinal))
                    {
                        HandleRegister(ssl, headers, client.Client.RemoteEndPoint, ReadBody(ssl, headers));
                        return;
                    }
                    SendText(ssl, 405, "Method Not Allowed", "Método no permitido.");
                }
                catch (Exception ex)
                {
                    Log("Solicitud HTTPS fallida: " + ex.Message);
                    try { SendText(ssl, 400, "Bad Request", "Solicitud no válida."); }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private static void HandleGet(SslStream stream, string path)
        {
            if (path == "/api/health")
            {
                SendJson(stream, 200, new ApiResponse { Ok = true, Message = "launcher-ready" });
                return;
            }
            SendText(stream, 404, "Not Found", "No encontrado.");
        }

        private static void HandleRegister(SslStream stream, Dictionary<string, string> headers, EndPoint remote, byte[] body)
        {
            IPEndPoint endpoint = remote as IPEndPoint;
            string address = endpoint == null ? String.Empty : endpoint.Address.ToString();
            if (endpoint == null || !IsPrivateAddress(address))
            {
                SendJson(stream, 403, new ApiResponse { Message = "El registro solo está disponible desde la red local." });
                return;
            }
            if (!AllowRegistration(address))
            {
                SendJson(stream, 429, new ApiResponse { Message = "Demasiados intentos. Espera antes de volver a intentarlo." });
                return;
            }

            AccountRequest request;
            try
            {
                using (MemoryStream input = new MemoryStream(body))
                    request = JsonTools.Deserialize<AccountRequest>(input);
            }
            catch (ArgumentException)
            {
                SendJson(stream, 400, new ApiResponse { Message = "Solicitud de registro no válida." });
                return;
            }
            if (request == null || !IsValidAccount(request.Username, request.Password))
            {
                SendJson(stream, 400, new ApiResponse { Message = "Usuario o contraseña no válidos." });
                return;
            }

            try
            {
                CreateAccount(request);
                Log("Cuenta creada desde " + address);
                SendJson(stream, 201, new ApiResponse { Ok = true, Message = "Cuenta creada correctamente." });
            }
            catch (Exception ex)
            {
                Log("Registro fallido desde " + address + ": " + ex.Message);
                SendJson(stream, 502, new ApiResponse { Message = "No se pudo crear la cuenta. Comprueba que el servidor esté iniciado." });
            }
        }

        private static string ReadHeaders(Stream stream)
        {
            MemoryStream buffer = new MemoryStream();
            int matched = 0;
            byte[] ending = new byte[] { 13, 10, 13, 10 };
            while (buffer.Length < 8192)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new EndOfStreamException();
                buffer.WriteByte((byte)value);
                if (value == ending[matched])
                {
                    matched++;
                    if (matched == ending.Length)
                        return Encoding.ASCII.GetString(buffer.ToArray(), 0, (int)buffer.Length - ending.Length);
                }
                else
                {
                    matched = value == ending[0] ? 1 : 0;
                }
            }
            throw new InvalidDataException("Encabezados HTTP demasiado grandes.");
        }

        private static byte[] ReadBody(Stream stream, Dictionary<string, string> headers)
        {
            string transferEncoding;
            if (headers.TryGetValue("Transfer-Encoding", out transferEncoding))
                throw new InvalidDataException("Transfer-Encoding no admitido.");
            string value;
            int length = 0;
            if (headers.TryGetValue("Content-Length", out value) &&
                (!Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0 || length > 2048))
                throw new InvalidDataException("Tamaño de solicitud no válido.");
            if (length == 0)
                throw new InvalidDataException("Solicitud vacía.");
            byte[] body = new byte[length];
            int offset = 0;
            while (offset < body.Length)
            {
                int read = stream.Read(body, offset, body.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException();
                offset += read;
            }
            return body;
        }

        private static void SendJson(Stream stream, int status, ApiResponse response)
        {
            string reason = status == 201 ? "Created" :
                status == 200 ? "OK" :
                status == 400 ? "Bad Request" :
                status == 403 ? "Forbidden" :
                status == 429 ? "Too Many Requests" : "Bad Gateway";
            SendText(stream, status, reason, JsonTools.Serialize(response), "application/json; charset=utf-8");
        }

        private static void SendText(Stream stream, int status, string reason, string content, string contentType)
        {
            byte[] body = Encoding.UTF8.GetBytes(content);
            string header = "HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + reason +
                            "\r\nContent-Type: " + contentType + "\r\nX-Content-Type-Options: nosniff\r\nContent-Length: " +
                            body.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static void SendText(Stream stream, int status, string reason, string content)
        {
            SendText(stream, status, reason, content, "text/plain; charset=utf-8");
        }

        private static bool AllowRegistration(string key)
        {
            lock (RateLock)
            {
                List<DateTime> attempts;
                if (!RegistrationAttempts.TryGetValue(key, out attempts))
                {
                    attempts = new List<DateTime>();
                    RegistrationAttempts[key] = attempts;
                }
                DateTime cutoff = DateTime.UtcNow.AddHours(-1);
                attempts.RemoveAll(stamp => stamp < cutoff);
                if (attempts.Count >= 5)
                    return false;
                attempts.Add(DateTime.UtcNow);
                return true;
            }
        }

        internal static bool IsPrivateAddress(string text)
        {
            IPAddress address;
            if (!IPAddress.TryParse(text, out address))
                return false;
            if (IPAddress.IsLoopback(address))
                return true;
            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            }
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
                   (bytes[0] & 0xFE) == 0xFC;
        }

        internal static bool IsValidAccount(string username, string password)
        {
            return Regex.IsMatch(username ?? String.Empty, "^[A-Za-z0-9_]{3,16}$") &&
                   (password ?? String.Empty).Length >= 8 &&
                   (password ?? String.Empty).Length <= 16 &&
                   Regex.IsMatch(password ?? String.Empty, "^[A-Za-z0-9!#$%&()*+,\\-./:;=?@^_~]+$");
        }

        private static void CreateAccount(AccountRequest request)
        {
            string root = Environment.GetEnvironmentVariable("SystemRoot");
            if (String.IsNullOrEmpty(root))
                root = Environment.GetEnvironmentVariable("WINDIR");
            if (String.IsNullOrEmpty(root) || !Path.IsPathRooted(root))
                throw new InvalidOperationException("No se encontró Windows.");

            string wsl = Path.Combine(root, "System32", "wsl.exe");
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = wsl;
            start.Arguments = "-d WoWServer -u root --exec /usr/local/bin/wowctl register";
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            {
                StringBuilder captured = new StringBuilder();
                object outputLock = new object();
                DataReceivedEventHandler captureOutput = delegate(object sender, DataReceivedEventArgs eventArgs)
                {
                    if (eventArgs.Data == null)
                        return;
                    lock (outputLock)
                    {
                        if (captured.Length < 4096)
                        {
                            int remaining = 4096 - captured.Length;
                            captured.Append(eventArgs.Data.Substring(0, Math.Min(eventArgs.Data.Length, remaining)));
                        }
                    }
                };
                process.OutputDataReceived += captureOutput;
                process.ErrorDataReceived += captureOutput;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.Write(JsonTools.Serialize(request));
                process.StandardInput.Close();
                if (!process.WaitForExit(90000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("La operación de registro en WSL excedió el tiempo límite.");
                }
                process.WaitForExit();
                string result;
                lock (outputLock)
                    result = captured.ToString().Trim();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("WSL/AzerothCore: " + result);
            }
        }

        private static X509Certificate2 LoadOrCreateCertificate()
        {
            Directory.CreateDirectory(ManifestTools.AppDataDirectory);
            string path = Path.Combine(ManifestTools.AppDataDirectory, "server-certificate.pfx");
            if (File.Exists(path))
            {
                try
                {
                    return new X509Certificate2(path, String.Empty,
                        X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
                }
                catch (CryptographicException ex)
                {
                    throw new InvalidDataException("El certificado TLS del launcher está dañado.", ex);
                }
            }

            using (RSA rsa = RSA.Create())
            {
                rsa.KeySize = 2048;
                CertificateRequest request = new CertificateRequest(
                    "CN=AzerothCore Launcher LAN", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
                SubjectAlternativeNameBuilder names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                names.AddIpAddress(IPAddress.Loopback);
                foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (UnicastIPAddressInformation address in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address))
                            names.AddIpAddress(address.Address);
                    }
                }
                request.CertificateExtensions.Add(names.Build());
                DateTimeOffset now = DateTimeOffset.UtcNow;
                using (X509Certificate2 generated = request.CreateSelfSigned(now.AddMinutes(-1), now.AddYears(5)))
                {
                    byte[] pfx = generated.Export(X509ContentType.Pfx, String.Empty);
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllBytes(temporary, pfx);
                        if (File.Exists(path))
                            File.Replace(temporary, path, null);
                        else
                            File.Move(temporary, path);
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                            File.Delete(temporary);
                    }
                }
            }
            return new X509Certificate2(path, String.Empty,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(ManifestTools.AppDataDirectory);
                lock (LogLock)
                    File.AppendAllText(Path.Combine(ManifestTools.AppDataDirectory, "server.log"),
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                        Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
