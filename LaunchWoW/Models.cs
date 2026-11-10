using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AzerothCoreLauncher
{
    [DataContract]
    internal sealed class ClientManifest
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "files")]
        public List<ManifestFile> Files { get; set; }
    }

    [DataContract]
    internal sealed class ManifestFile
    {
        [DataMember(Name = "path")]
        public string Path { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "sha256")]
        public string Sha256 { get; set; }
    }

    [DataContract]
    internal sealed class AccountRequest
    {
        [DataMember(Name = "username")]
        public string Username { get; set; }

        [DataMember(Name = "password")]
        public string Password { get; set; }
    }

    [DataContract]
    internal sealed class ApiResponse
    {
        [DataMember(Name = "ok")]
        public bool Ok { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; }
    }

    internal static class ManifestTools
    {
        private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>(
            new[] { "wtf", "cache", "logs", "screenshots", "errors", "error", "savedvariables", "interface" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExcludedFiles = new HashSet<string>(
            new[] { "realmlist.wtf", "config-cache.wtf", "bindings-cache.wtf", "macros-cache.txt", "desktop.ini", "thumbs.db", "frostbound launcher.exe", "set_language_enus.bat" },
            StringComparer.OrdinalIgnoreCase);

        internal static bool IsExcluded(string relativePath)
        {
            string normalized = (relativePath ?? String.Empty).Replace('\\', '/');
            foreach (string part in normalized.Split('/'))
            {
                if (ExcludedDirectories.Contains(part))
                    return true;
            }

            string name = normalized.Substring(normalized.LastIndexOf('/') + 1);
            string extension = System.IO.Path.GetExtension(name);
            return ExcludedFiles.Contains(name) ||
                   String.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(extension, ".dmp", StringComparison.OrdinalIgnoreCase) ||
                   String.Equals(extension, ".tmp", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsSafeManifestPath(string name)
        {
            if (String.IsNullOrEmpty(name) || name.IndexOf('\\') >= 0 ||
                name.StartsWith("/", StringComparison.Ordinal) ||
                Regex.IsMatch(name, @"^[A-Za-z]:") ||
                name.IndexOf('\0') >= 0)
                return false;

            string[] parts = name.Split('/');
            if (parts.Any(part => part.Length == 0 || part == "." || part == ".."))
                return false;
            return !IsExcluded(name);
        }

        internal static ClientManifest Build(string root, Action<int, int, string> progress)
        {
            string fullRoot = System.IO.Path.GetFullPath(root);
            if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("La carpeta cliente es un enlace/junction y no es segura.");
            if (!File.Exists(System.IO.Path.Combine(fullRoot, "Wow.exe")))
                throw new InvalidDataException("La carpeta debe contener Wow.exe.");
            if (!Directory.Exists(System.IO.Path.Combine(fullRoot, "Data")))
                throw new InvalidDataException("La carpeta debe contener Data.");

            List<string> files = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(fullRoot);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    FileAttributes attributes = File.GetAttributes(entry);
                    string relative = entry.Substring(fullRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar).Length)
                        .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!IsExcluded(relative))
                            pending.Push(entry);
                    }
                    else if (!IsExcluded(relative))
                    {
                        files.Add(relative);
                    }

                }
            }

            files.Sort(StringComparer.Ordinal);
            ClientManifest result = new ClientManifest();
            result.Version = 1;
            result.Files = new List<ManifestFile>(files.Count);
            for (int i = 0; i < files.Count; i++)
            {
                string relative = files[i];
                string filePath = System.IO.Path.Combine(fullRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                FileInfo info = new FileInfo(filePath);
                ManifestFile item = new ManifestFile();
                item.Path = relative;
                item.Size = info.Length;
                item.Sha256 = HashFile(filePath);
                result.Files.Add(item);
                if (progress != null)
                    progress(i + 1, files.Count, relative);
            }

            Validate(result);
            return result;
        }

        internal static void Validate(ClientManifest manifest)
        {
            if (manifest == null || manifest.Version != 1 || manifest.Files == null || manifest.Files.Count == 0)
                throw new InvalidDataException("Formato de manifiesto no compatible.");

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasWow = false;
            foreach (ManifestFile item in manifest.Files)
            {
                if (item == null || !IsSafeManifestPath(item.Path) || item.Size < 0 ||
                    !Regex.IsMatch(item.Sha256 ?? String.Empty, "^[a-f0-9]{64}$"))
                    throw new InvalidDataException("El manifiesto contiene una entrada no válida.");
                if (!seen.Add(item.Path))
                    throw new InvalidDataException("El manifiesto contiene una ruta duplicada.");
                if (String.Equals(item.Path, "Wow.exe", StringComparison.OrdinalIgnoreCase))
                    hasWow = true;
            }

            if (!hasWow)
                throw new InvalidDataException("El manifiesto no contiene Wow.exe.");
        }

        internal static string HashFile(string filePath)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(stream));
        }

        internal static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", String.Empty).ToLowerInvariant();
        }

        internal static string AppDataDirectory
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AzerothCoreLauncher");
            }
        }
    }

    internal static class ClientValidation
    {
        internal static bool IsRequiredBuild(int major, int minor, int build, int revision)
        {
            return major == 3 && minor == 3 && build == 5 && revision == 12340;
        }

        internal static string GetInvalidClient(string clientDirectory)
        {
            string executable = System.IO.Path.Combine(clientDirectory, "Wow.exe");
            if (!File.Exists(executable))
                return "Wow.exe";
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
            if (!IsRequiredBuild(version.FileMajorPart, version.FileMinorPart,
                version.FileBuildPart, version.FilePrivatePart))
                return "Wow.exe (se requiere la versión 3.3.5.12340)";
            string missingArchive = GetMissingRequiredArchive(clientDirectory);
            if (missingArchive != null)
                return missingArchive;
            if (!Directory.Exists(System.IO.Path.Combine(clientDirectory, "Data", "enUS")) &&
                !Directory.Exists(System.IO.Path.Combine(clientDirectory, "Data", "enGB")) &&
                !Directory.Exists(System.IO.Path.Combine(clientDirectory, "Data", "esES")))
                return "Data\\enUS, Data\\enGB o Data\\esES";
            return null;
        }

        internal static string GetMissingRequiredArchive(string clientDirectory)
        {
            string dataDirectory = System.IO.Path.Combine(clientDirectory, "Data");
            foreach (string archive in new[] { "common.MPQ" })
            {
                string requiredArchive = System.IO.Path.Combine(dataDirectory, archive);
                if (!Directory.Exists(dataDirectory) || !File.Exists(requiredArchive))
                    return "Data\\" + archive;
                if ((File.GetAttributes(requiredArchive) & FileAttributes.ReparsePoint) != 0 ||
                    new FileInfo(requiredArchive).Length == 0)
                    return "Data\\" + archive + " (archivo vacío o enlace no válido)";
            }
            return null;
        }
    }

    internal static class ClientPackage
    {
        internal static void InstallZip(string packagePath, string destination, Action<long, long, string> progress)
        {
            string target = System.IO.Path.GetFullPath(destination);
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("La carpeta de destino ya existe. Elige una carpeta nueva para no sobrescribir archivos.");

            string parent = System.IO.Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            string temporary = System.IO.Path.Combine(parent, ".azerothcore-client-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            bool installed = false;
            try
            {
                using (FileStream file = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    long total = archive.Entries.Where(entry => !String.IsNullOrEmpty(entry.Name) &&
                        !ManifestTools.IsExcluded(entry.FullName)).Sum(entry => entry.Length);
                    long extracted = 0;
                    byte[] buffer = new byte[1024 * 1024];
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (String.IsNullOrEmpty(entry.Name))
                            continue;
                        string relative = entry.FullName.Replace('\\', '/');
                        if (ManifestTools.IsExcluded(relative))
                            continue;
                        if (!ManifestTools.IsSafeManifestPath(relative))
                            throw new InvalidDataException("El paquete contiene una ruta no válida: " + entry.FullName);

                        string output = System.IO.Path.GetFullPath(System.IO.Path.Combine(temporary,
                            relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                        if (!output.StartsWith(temporary.TrimEnd(System.IO.Path.DirectorySeparatorChar) +
                            System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("El paquete contiene una ruta fuera de la carpeta de destino.");
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
                        using (Stream input = entry.Open())
                        using (FileStream destinationFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                destinationFile.Write(buffer, 0, read);
                                extracted += read;
                                if (progress != null)
                                    progress(extracted, total, relative);
                            }
                        }
                    }
                }

                string invalidClient = ClientValidation.GetInvalidClient(temporary);
                if (invalidClient != null)
                    throw new InvalidDataException("El ZIP no contiene un cliente válido: falta o no coincide " + invalidClient + ".");

                Directory.Move(temporary, target);
                installed = true;
            }
            finally
            {
                if (!installed && Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
            }
        }
    }

    internal static class ClientDownload
    {
        internal static readonly Uri Server = new Uri("https://192.168.201.209:8788/client/");
        internal const string CertificatePin = "2801924a71f3ade247e5d57d9e133e49582dc01199599826c7e115ade4cc025d";

        internal static Uri ManifestUri(Uri server)
        {
            return new Uri(server, "manifest.json");
        }

        internal static Uri FileUri(Uri server, string relativePath)
        {
            string escaped = String.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString).ToArray());
            return new Uri(server, "files/" + escaped);
        }

        internal static void Install(Uri server, string pin, string destination, string realm,
            Action<long, long, string> progress)
        {
            Uri baseUri = new Uri(server.AbsoluteUri.TrimEnd('/') + "/");
            HttpWebRequest manifestRequest = CreatePinnedRequest(ManifestUri(baseUri), pin);
            manifestRequest.Timeout = 600000;
            manifestRequest.ReadWriteTimeout = 600000;
            ClientManifest manifest;
            using (HttpWebResponse response = (HttpWebResponse)manifestRequest.GetResponse())
            using (Stream input = response.GetResponseStream())
                manifest = JsonTools.Deserialize<ClientManifest>(input);
            ManifestTools.Validate(manifest);

            string target = Path.GetFullPath(destination);
            if (Directory.Exists(target) || File.Exists(target))
                throw new IOException("La carpeta de destino ya existe. Elige una carpeta nueva para no sobrescribir archivos.");
            string parent = Path.GetDirectoryName(target);
            Directory.CreateDirectory(parent);
            string temporary = Path.Combine(parent, ".azerotbatzoki-client-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            bool installed = false;
            try
            {
                long total = manifest.Files.Sum(item => item.Size);
                long completed = 0;
                byte[] buffer = new byte[1024 * 1024];
                foreach (ManifestFile item in manifest.Files)
                {
                    Uri fileUri = FileUri(baseUri, item.Path);
                    string output = Path.GetFullPath(Path.Combine(temporary,
                        item.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (!output.StartsWith(temporary.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El manifiesto contiene una ruta fuera de la carpeta de destino.");
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    string partialFile = output + ".part";
                    if (item.Size == 0)
                        File.WriteAllBytes(partialFile, new byte[0]);
                    int retries = 0;
                    while (new FileInfo(partialFile).Length < item.Size)
                    {
                        long offset = new FileInfo(partialFile).Length;
                        HttpWebRequest request = CreatePinnedRequest(fileUri, pin);
                        request.Timeout = 300000;
                        request.ReadWriteTimeout = 300000;
                        if (offset > 0)
                            request.AddRange(offset);
                        try
                        {
                            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                            {
                                if ((offset > 0 && response.StatusCode != HttpStatusCode.PartialContent) ||
                                    (offset == 0 && response.StatusCode != HttpStatusCode.OK))
                                    throw new InvalidDataException("El host respondió con un rango inesperado para " + item.Path + ".");
                                using (Stream input = response.GetResponseStream())
                                using (FileStream file = new FileStream(partialFile,
                                    offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
                                {
                                    int read;
                                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        file.Write(buffer, 0, read);
                                        completed += read;
                                        if (progress != null)
                                            progress(completed, total, item.Path);
                                    }
                                }
                            }
                            if (new FileInfo(partialFile).Length < item.Size && ++retries > 3)
                                throw new EndOfStreamException("La conexión se interrumpió varias veces al descargar " + item.Path + ".");
                        }
                        catch (WebException)
                        {
                            if (++retries > 3)
                                throw;
                            Thread.Sleep(1000 * retries);
                        }
                        catch (IOException)
                        {
                            if (++retries > 3)
                                throw;
                            Thread.Sleep(1000 * retries);
                        }
                    }
                    File.Move(partialFile, output);
                    FileInfo info = new FileInfo(output);
                    if (info.Length != item.Size ||
                        !String.Equals(ManifestTools.HashFile(output), item.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La verificación SHA-256 falló para " + item.Path + ".");
                }

                string invalid = ClientValidation.GetInvalidClient(temporary);
                if (invalid != null)
                    throw new InvalidDataException("El paquete descargado no es un cliente válido: " + invalid + ".");
                foreach (string locale in new[] { "enUS", "enGB", "esES" })
                {
                    string localeDirectory = Path.Combine(temporary, "Data", locale);
                    if (Directory.Exists(localeDirectory))
                        File.WriteAllText(Path.Combine(localeDirectory, "realmlist.wtf"),
                            "set realmlist " + realm + "\r\n", Encoding.ASCII);
                }
                Directory.Move(temporary, target);
                installed = true;
            }
            finally
            {
                if (!installed && Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
            }
        }

        private static HttpWebRequest CreatePinnedRequest(Uri uri, string pin)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Proxy = null;
            request.Timeout = 300000;
            request.ReadWriteTimeout = 300000;
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
            return request;
        }
    }

    internal static class JsonTools
    {
        internal static string Serialize<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer serializer =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static T Deserialize<T>(Stream stream)
        {
            System.Runtime.Serialization.Json.DataContractJsonSerializer serializer =
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
            return (T)serializer.ReadObject(stream);
        }
    }
}
