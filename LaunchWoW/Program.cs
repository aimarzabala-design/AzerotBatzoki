using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Net;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("AzerothCore Launcher")]
[assembly: AssemblyProduct("AzerothCore Launcher")]
[assembly: AssemblyDescription("Lanzador de escritorio para cliente AzerothCore")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8")]

namespace AzerothCoreLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length > 0 && String.Equals(args[0], "--serve", StringComparison.Ordinal))
                return LauncherServer.Run();
            if (args.Length > 0 && String.Equals(args[0], "--self-test", StringComparison.Ordinal))
                return LauncherTests.Run();
            if (args.Length > 0 && String.Equals(args[0], "--fingerprint", StringComparison.Ordinal))
            {
                try
                {
                    Console.WriteLine(LauncherServer.GetFingerprint());
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
            return 0;
        }
    }

    internal static class LauncherTests
    {
        internal static int Run()
        {
            if (!ManifestTools.IsExcluded("WTF\\Config.wtf") ||
                !ManifestTools.IsExcluded("Data\\enUS\\realmlist.wtf") ||
                ManifestTools.IsExcluded("Data\\common.MPQ") ||
                ManifestTools.IsSafeManifestPath("../outside") ||
                ManifestTools.IsSafeManifestPath("C:/outside") ||
                !LauncherServer.IsPrivateAddress("192.168.1.5") ||
                LauncherServer.IsPrivateAddress("8.8.8.8") ||
                !LauncherServer.IsValidAccount("player_1", "secret123") ||
                LauncherServer.IsValidAccount("bad user", "secret123") ||
                !ClientValidation.IsRequiredBuild(3, 3, 5, 12340) ||
                ClientValidation.IsRequiredBuild(3, 3, 5, 12341) ||
                LauncherForm.BuildRegistrationUri(new Uri("https://192.168.201.213:8787/")).AbsolutePath != "/api/register" ||
                LauncherForm.DefaultServerAddress != "https://192.168.201.213:8787" ||
                LauncherForm.DefaultRealmAddress != "192.168.201.213" ||
                !TestManifestSerialization() ||
                !TestManifestBuild() ||
                !TestClientValidation() ||
                !TestClientPackage())
            {
                Console.Error.WriteLine("Falló una comprobación interna.");
                return 1;
            }

            Console.WriteLine("SELF_TEST_OK");
            return 0;
        }

        private static bool TestManifestSerialization()
        {
            ClientManifest expected = new ClientManifest
            {
                Version = 1,
                Files = new System.Collections.Generic.List<ManifestFile>
                {
                    new ManifestFile { Path = "Wow.exe", Size = 3, Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" }
                }
            };
            string json = JsonTools.Serialize(expected);
            if (!json.Contains("\"version\":1") || !json.Contains("\"sha256\""))
                return false;
            using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                ClientManifest actual = JsonTools.Deserialize<ClientManifest>(input);
                return actual.Version == expected.Version && actual.Files.Count == 1 &&
                       actual.Files[0].Path == expected.Files[0].Path &&
                       actual.Files[0].Size == expected.Files[0].Size;
            }
        }

        private static bool TestManifestBuild()
        {
            string root = Path.Combine(Path.GetTempPath(), "AzerothCoreLauncherTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Data", "enUS"));
                Directory.CreateDirectory(Path.Combine(root, "WTF"));
                File.WriteAllText(Path.Combine(root, "Wow.exe"), "wow");
                File.WriteAllText(Path.Combine(root, "Data", "common.MPQ"), "data");
                File.WriteAllText(Path.Combine(root, "Data", "enUS", "realmlist.wtf"), "set realmlist bad");
                File.WriteAllText(Path.Combine(root, "WTF", "Config.wtf"), "user settings");
                ClientManifest manifest = ManifestTools.Build(root, null);
                return manifest.Files.Count == 2 &&
                       manifest.Files.Any(item => item.Path == "Wow.exe") &&
                       manifest.Files.Any(item => item.Path == "Data/common.MPQ") &&
                       manifest.Files.All(item => Regex.IsMatch(item.Sha256, "^[a-f0-9]{64}$"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static bool TestClientValidation()
        {
            string root = Path.Combine(Path.GetTempPath(), "AzerothCoreClientValidation-" + Guid.NewGuid().ToString("N"));
            string data = Path.Combine(root, "Data");
            try
            {
                Directory.CreateDirectory(data);
                if (ClientValidation.GetMissingRequiredArchive(root) != "Data\\interface.MPQ")
                    return false;
                string archive = Path.Combine(data, "interface.MPQ");
                File.WriteAllBytes(archive, new byte[] { 1 });
                if (ClientValidation.GetMissingRequiredArchive(root) != "Data\\common.MPQ")
                    return false;
                File.WriteAllBytes(Path.Combine(data, "common.MPQ"), new byte[] { 1 });
                if (ClientValidation.GetMissingRequiredArchive(root) != null)
                    return false;
                File.WriteAllBytes(archive, new byte[0]);
                return ClientValidation.GetMissingRequiredArchive(root) != null;
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static bool TestClientPackage()
        {
            string root = Path.Combine(Path.GetTempPath(), "AzerothCoreClientPackage-" + Guid.NewGuid().ToString("N"));
            string zip = root + ".zip";
            string target = Path.Combine(root, "installed");
            string gameExecutable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Wow.exe");
            try
            {
                if (!File.Exists(gameExecutable))
                    return false;
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(gameExecutable);
                if (!ClientValidation.IsRequiredBuild(version.FileMajorPart, version.FileMinorPart,
                    version.FileBuildPart, version.FilePrivatePart))
                    return false;

                using (FileStream file = new FileStream(zip, FileMode.CreateNew))
                using (System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(file,
                    System.IO.Compression.ZipArchiveMode.Create))
                {
                    AddPackageFile(archive, "Wow.exe", gameExecutable);
                    AddPackageEntry(archive, "Data/interface.MPQ", "interface");
                    AddPackageEntry(archive, "Data/common.MPQ", "common");
                    AddPackageEntry(archive, "Data/enUS/locale.MPQ", "locale");
                    AddPackageEntry(archive, "WTF/Account/private.txt", "private");
                    AddPackageEntry(archive, "Screenshots/private.jpg", "private");
                }
                ClientPackage.InstallZip(zip, target, null);
                return ClientValidation.GetInvalidClient(target) == null &&
                       ManifestTools.HashFile(Path.Combine(target, "Wow.exe")) == ManifestTools.HashFile(gameExecutable) &&
                       !Directory.Exists(Path.Combine(target, "WTF")) &&
                       !Directory.Exists(Path.Combine(target, "Screenshots"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
                if (File.Exists(zip))
                    File.Delete(zip);
            }
        }

        private static void AddPackageEntry(System.IO.Compression.ZipArchive archive, string path, string content)
        {
            using (Stream output = archive.CreateEntry(path).Open())
            {
                byte[] data = Encoding.UTF8.GetBytes(content);
                output.Write(data, 0, data.Length);
            }
        }

        private static void AddPackageFile(System.IO.Compression.ZipArchive archive, string path, string source)
        {
            using (Stream input = File.OpenRead(source))
            using (Stream output = archive.CreateEntry(path).Open())
                input.CopyTo(output);
        }
    }
}
