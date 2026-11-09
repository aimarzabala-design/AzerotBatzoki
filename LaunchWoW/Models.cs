using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
            new[] { "wtf", "cache", "logs", "screenshots", "errors", "error", "savedvariables" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> ExcludedFiles = new HashSet<string>(
            new[] { "realmlist.wtf", "config-cache.wtf", "bindings-cache.wtf", "macros-cache.txt", "desktop.ini", "thumbs.db" },
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
