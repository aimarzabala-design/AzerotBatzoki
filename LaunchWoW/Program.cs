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

[assembly: AssemblyTitle("AzerotBatzoki")]
[assembly: AssemblyProduct("AzerotBatzoki")]
[assembly: AssemblyDescription("Launcher privado de AzerotBatzoki para World of Warcraft 3.3.5a")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
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
            if (args.Length > 0 && String.Equals(args[0], "--serve-client", StringComparison.Ordinal))
                return LauncherServer.RunClientFiles();
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
                    Console.Error.WriteLine(ex.ToString());
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
                !ManifestTools.IsExcluded("Data\\esES\\realmlist.wtf") ||
                ManifestTools.IsExcluded("Interface\\AddOns\\AzerotBatzokiTools\\AzerotBatzokiTools.toc") ||
                !ManifestTools.IsExcluded("Interface\\FrameXML\\UIParent.lua") ||
                !ManifestTools.IsExcluded("Interface\\AddOns\\Frostbound\\Frostbound.lua") ||
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
                ClientDownload.Server.AbsoluteUri != "https://192.168.201.209:8788/client/" ||
                ClientDownload.ManifestUri(ClientDownload.Server).AbsolutePath != "/client/manifest.json" ||
                ClientDownload.FileUri(ClientDownload.Server, "Data/common.MPQ").AbsolutePath != "/client/files/Data/common.MPQ" ||
                !TestWindowLayout() ||
                !TestPartialFileLength() ||
                !TestUpdateComparison() ||
                !TestRangeParsing() ||
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

        private static bool TestWindowLayout()
        {
            try
            {
                using (LauncherForm form = new LauncherForm())
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(1920, 1080))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    foreach (string name in new[] { "PaintBatzokiHome", "PaintBatzokiScene", "PaintHomeProgress" })
                    using (Panel panel = new Panel())
                    {
                        panel.Size = System.Drawing.Size.Empty;
                        typeof(LauncherForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, new object[] { panel, new PaintEventArgs(graphics, System.Drawing.Rectangle.Empty) });
                    }
                    typeof(LauncherForm).GetMethod("SetClientReady", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { "Cliente seleccionado de prueba" });
                    Label selectedStatus = (Label)typeof(LauncherForm).GetField("homeStatus", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    if (selectedStatus.Text != "Cliente seleccionado de prueba")
                        throw new InvalidOperationException("La portada no refleja el cliente seleccionado.");
                    Control settings = form.Controls["Settings"];
                    Control settingsContent = settings.Controls["SettingsContent"];
                    if (settingsContent.Left != Math.Max(0, (settings.ClientSize.Width - settingsContent.Width) / 2))
                        throw new InvalidOperationException("Ajustes no está centrado al abrir por primera vez.");
                    // A hidden handle runs queued UI callbacks without opening a window.
                    IntPtr hiddenHandle = form.Handle;
                    typeof(LauncherForm).GetMethod("FitToWorkingArea", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
                    if (!Screen.FromHandle(form.Handle).WorkingArea.Contains(form.Bounds))
                        throw new InvalidOperationException("La ventana no cabe en el área de trabajo actual.");
                    bool callbackRan = false;
                    Action<Action<string>, Action<int, string>> operation = delegate(Action<string> log, Action<int, string> report)
                    {
                        report(50, "Progreso de prueba");
                    };
                    Action completed = delegate
                    {
                        typeof(LauncherForm).GetMethod("SetClientReady", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, new object[] { "Final de prueba" });
                        callbackRan = true;
                    };
                    typeof(LauncherForm).GetMethod("RunOperation", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { "Prueba", operation, completed });
                    Stopwatch timeout = Stopwatch.StartNew();
                    FieldInfo running = typeof(LauncherForm).GetField("operationRunning", BindingFlags.Instance | BindingFlags.NonPublic);
                    while ((!callbackRan || (int)running.GetValue(form) != 0) && timeout.ElapsedMilliseconds < 5000)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(10);
                    }
                    if (!callbackRan || (int)running.GetValue(form) != 0 || selectedStatus.Text != "Final de prueba")
                        throw new InvalidOperationException("El estado final se pierde o la operación no termina.");
                    Control home = form.Controls.Cast<Control>().Single(c => c.Name == "Home");
                    foreach (System.Drawing.Size size in new[] { new System.Drawing.Size(1000, 660), new System.Drawing.Size(1100, 680),
                        new System.Drawing.Size(1366, 728), new System.Drawing.Size(1920, 1040), new System.Drawing.Size(1100, 730) })
                    {
                        form.ClientSize = size;
                        form.PerformLayout();
                        home.PerformLayout();
                        CheckControlBounds(home);
                    }
                }
                Console.WriteLine("WINDOW_LAYOUT_OK");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WINDOW_LAYOUT_FAILED: " + ex.GetBaseException().Message);
                return false;
            }
        }

        private static void CheckControlBounds(Control parent)
        {
            Control[] children = parent.Controls.Cast<Control>().ToArray();
            foreach (Control child in children)
            {
                if (!parent.ClientRectangle.Contains(child.Bounds))
                    throw new InvalidOperationException("Contenido fuera de ventana: " + child.GetType().Name + " " + child.Text);
                if (parent is TableLayoutPanel)
                    foreach (Control other in children)
                        if (other != child && child.Bounds.IntersectsWith(other.Bounds))
                            throw new InvalidOperationException("Controles solapados: " + child.Text + " / " + other.Text);
                CheckControlBounds(child);
            }
        }

        private static bool TestPartialFileLength()
        {
            MethodInfo method = typeof(ClientDownload).GetMethod("GetPartialFileLength",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null)
                return false;
            string path = Path.Combine(Path.GetTempPath(), "azerotbatzoki-test-" + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                if (Convert.ToInt64(method.Invoke(null, new object[] { path })) != 0L)
                    return false;
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return Convert.ToInt64(method.Invoke(null, new object[] { path })) == 3L;
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static bool TestUpdateComparison()
        {
            string path = Path.Combine(Path.GetTempPath(), "azerotbatzoki-update-test-" + Guid.NewGuid().ToString("N"));
            byte[] content = Encoding.UTF8.GetBytes("same client file");
            try
            {
                File.WriteAllBytes(path, content);
                ManifestFile expected = new ManifestFile
                {
                    Path = "test.bin",
                    Size = content.Length,
                    Sha256 = ManifestTools.HashFile(path)
                };
                if (!ClientDownload.IsFileCurrent(path, expected))
                    return false;
                expected.Sha256 = new string('0', 64);
                if (ClientDownload.IsFileCurrent(path, expected))
                    return false;
                expected.Sha256 = ManifestTools.HashFile(path);
                expected.Size++;
                if (ClientDownload.IsFileCurrent(path, expected))
                    return false;
                File.Delete(path);
                return !ClientDownload.IsFileCurrent(path, expected);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static bool TestRangeParsing()
        {
            long offset;
            return LauncherServer.TryParseStartRange("bytes=0-", 10, out offset) && offset == 0 &&
                   LauncherServer.TryParseStartRange("bytes=9-", 10, out offset) && offset == 9 &&
                   !LauncherServer.TryParseStartRange("bytes=10-", 10, out offset) &&
                   !LauncherServer.TryParseStartRange("bytes=1-2", 10, out offset) &&
                   !LauncherServer.TryParseStartRange("bytes=-1", 10, out offset);
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
                Directory.CreateDirectory(Path.Combine(root, "Interface", "AddOns"));
                Directory.CreateDirectory(Path.Combine(root, "Interface", "FrameXML"));
                Directory.CreateDirectory(Path.Combine(root, "Interface", "AddOns", "Frostbound"));
                Directory.CreateDirectory(Path.Combine(root, "WTF"));
                File.WriteAllText(Path.Combine(root, "Wow.exe"), "wow");
                File.WriteAllText(Path.Combine(root, "Data", "common.MPQ"), "data");
                File.WriteAllText(Path.Combine(root, "Data", "enUS", "realmlist.wtf"), "set realmlist bad");
                File.WriteAllText(Path.Combine(root, "Interface", "AddOns", "AzerotBatzokiTools.toc"), "## Interface: 30300");
                File.WriteAllText(Path.Combine(root, "Interface", "AddOns", "Frostbound", "Frostbound.lua"), "excluded Frostbound addon");
                File.WriteAllText(Path.Combine(root, "Interface", "FrameXML", "UIParent.lua"), "game UI source");
                File.WriteAllText(Path.Combine(root, "WTF", "Config.wtf"), "user settings");
                ClientManifest manifest = ManifestTools.Build(root, null);
                return manifest.Files.Count == 3 &&
                       manifest.Files.Any(item => item.Path == "Wow.exe") &&
                       manifest.Files.Any(item => item.Path == "Data/common.MPQ") &&
                       manifest.Files.Any(item => item.Path == "Interface/AddOns/AzerotBatzokiTools.toc") &&
                       manifest.Files.All(item => !item.Path.Contains("Frostbound") && !item.Path.Contains("FrameXML")) &&
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
                if (ClientValidation.GetMissingRequiredArchive(root) != "Data\\common.MPQ")
                    return false;
                string archive = Path.Combine(data, "common.MPQ");
                File.WriteAllBytes(archive, new byte[] { 1 });
                if (ClientValidation.GetMissingRequiredArchive(root) != null)
                    return false;
                File.WriteAllBytes(archive, new byte[0]);
                if (ClientValidation.GetMissingRequiredArchive(root) == null)
                    return false;

                File.WriteAllBytes(archive, new byte[] { 1 });
                Directory.CreateDirectory(Path.Combine(data, "esES"));
                File.WriteAllText(Path.Combine(root, "Wow.exe"), "not a game executable");
                return ClientValidation.GetInvalidClient(root) == "Wow.exe (se requiere la versión 3.3.5.12340)";
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static bool TestClientPackage()
        {
            string root = Path.Combine(Path.GetTempPath(), "AB" + Guid.NewGuid().ToString("N"));
            string zip = root + ".zip";
            string target = Path.Combine(root, "i");
            try
            {
                using (FileStream file = new FileStream(zip, FileMode.CreateNew))
                using (System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(file,
                    System.IO.Compression.ZipArchiveMode.Create))
                {
                    AddPackageEntry(archive, "Wow.exe", "not a game executable");
                    AddPackageEntry(archive, "Data/common.MPQ", "common");
                    AddPackageEntry(archive, "Data/esES/locale-esES.MPQ", "locale");
                    AddPackageEntry(archive, "Data/esES/realmlist.wtf", "set realmlist personal");
                    AddPackageEntry(archive, "WTF/Account/private.txt", "private");
                    AddPackageEntry(archive, "Screenshots/private.jpg", "private");
                }
                try
                {
                    ClientPackage.InstallZip(zip, target, null);
                    return false;
                }
                catch (InvalidDataException)
                {
                    return !Directory.Exists(target) && Directory.GetFileSystemEntries(root).Length == 0;
                }
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

    }
}
