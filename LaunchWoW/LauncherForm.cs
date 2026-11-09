using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;

namespace AzerothCoreLauncher
{
    internal sealed class LauncherForm : Form
    {
        internal const string DefaultServerAddress = "https://192.168.201.213:8787";
        internal const string DefaultRealmAddress = "192.168.201.213";

        private readonly Color background = Color.FromArgb(16, 22, 32);
        private readonly Color panel = Color.FromArgb(27, 37, 52);
        private readonly Color muted = Color.FromArgb(158, 174, 193);
        private readonly Color foreground = Color.FromArgb(238, 242, 248);
        private readonly Color accent = Color.FromArgb(69, 135, 209);
        private readonly TextBox serverBox;
        private readonly TextBox pinBox;
        private readonly TextBox realmBox;
        private readonly TextBox targetBox;
        private readonly TextBox usernameBox;
        private readonly TextBox passwordBox;
        private readonly TextBox confirmBox;
        private readonly TextBox outputBox;
        private readonly ProgressBar progress;
        private readonly Label status;
        private readonly List<Button> actionButtons = new List<Button>();
        private int operationRunning;

        internal LauncherForm()
        {
            Text = "AzerothCore Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(880, 735);
            MinimumSize = new Size(880, 735);
            MaximumSize = new Size(880, 735);
            BackColor = background;
            ForeColor = foreground;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(880, 102);
            header.BackColor = Color.FromArgb(21, 31, 45);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "AZEROTHCORE";
            title.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
            title.ForeColor = foreground;
            title.Location = new Point(28, 17);
            title.Size = new Size(420, 42);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "WRATH OF THE LICH KING  •  CLIENTE 3.3.5a";
            subtitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            subtitle.ForeColor = muted;
            subtitle.Location = new Point(31, 63);
            subtitle.Size = new Size(460, 22);
            header.Controls.Add(subtitle);

            Label buildBadge = new Label();
            buildBadge.Text = "LAUNCHER DE ESCRITORIO";
            buildBadge.TextAlign = ContentAlignment.MiddleCenter;
            buildBadge.ForeColor = Color.FromArgb(205, 222, 243);
            buildBadge.BackColor = Color.FromArgb(39, 57, 78);
            buildBadge.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            buildBadge.Location = new Point(676, 36);
            buildBadge.Size = new Size(172, 28);
            header.Controls.Add(buildBadge);

            Panel serverCard = MakeCard(24, 120, 832, 191);
            Controls.Add(serverCard);
            AddSectionTitle(serverCard, "SERVIDOR Y CLIENTE", 18, 12);
            AddFieldLabel(serverCard, "Servidor HTTPS", 18, 48, 170);
            serverBox = AddInput(serverCard, DefaultServerAddress, 190, 45, 621);
            AddFieldLabel(serverCard, "Huella SHA-256", 18, 83, 170);
            pinBox = AddInput(serverCard, "", 190, 80, 621);
            AddFieldLabel(serverCard, "Dirección del reino", 18, 118, 170);
            realmBox = AddInput(serverCard, DefaultRealmAddress, 190, 115, 621);
            AddFieldLabel(serverCard, "Carpeta del cliente", 18, 153, 170);
            targetBox = AddInput(serverCard,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerothCoreLauncher", "Client"),
                190, 150, 524);
            Button browse = MakeButton("EXAMINAR", 722, 149, 91, 29, panel, foreground);
            browse.Click += delegate { BrowseTarget(); };
            serverCard.Controls.Add(browse);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Location = new Point(24, 326);
            actions.Size = new Size(832, 56);
            actions.WrapContents = false;
            actions.BackColor = background;
            Controls.Add(actions);
            Button install = MakeButton("CLIENTE / INSTALAR", 0, 0, 178, 46, accent, Color.White);
            Button repair = MakeButton("ABRIR CARPETA", 0, 0, 152, 46, panel, foreground);
            Button play = MakeButton("JUGAR", 0, 0, 132, 46, Color.FromArgb(50, 128, 83), Color.White);
            Button host = MakeButton("ACTIVAR CUENTAS LAN", 0, 0, 228, 46, panel, foreground);
            install.Click += delegate { SelectClient(); };
            repair.Click += delegate { OpenClientFolder(); };
            play.Click += delegate { Play(); };
            host.Click += delegate { PrepareServer(); };
            actions.Controls.Add(install);
            actions.Controls.Add(repair);
            actions.Controls.Add(play);
            actions.Controls.Add(host);
            actionButtons.Add(install);
            actionButtons.Add(repair);
            actionButtons.Add(play);
            actionButtons.Add(host);

            Panel accountCard = MakeCard(24, 397, 832, 113);
            Controls.Add(accountCard);
            AddSectionTitle(accountCard, "CREAR CUENTA", 18, 10);
            AddFieldLabel(accountCard, "Usuario", 18, 45, 75);
            usernameBox = AddInput(accountCard, "", 92, 42, 170);
            AddFieldLabel(accountCard, "Contraseña", 278, 45, 90);
            passwordBox = AddInput(accountCard, "", 367, 42, 156);
            passwordBox.UseSystemPasswordChar = true;
            AddFieldLabel(accountCard, "Repetir", 539, 45, 62);
            confirmBox = AddInput(accountCard, "", 600, 42, 213);
            confirmBox.UseSystemPasswordChar = true;
            Button register = MakeButton("REGISTRAR CUENTA", 18, 75, 166, 28, accent, Color.White);
            Button copyPin = MakeButton("COPIAR HUELLA", 194, 75, 145, 28, panel, foreground);
            register.Click += delegate { RegisterAccount(); };
            copyPin.Click += delegate { CopyFingerprint(); };
            accountCard.Controls.Add(register);
            accountCard.Controls.Add(copyPin);
            actionButtons.Add(register);

            progress = new ProgressBar();
            progress.Location = new Point(25, 529);
            progress.Size = new Size(830, 14);
            progress.Minimum = 0;
            progress.Maximum = 100;
            Controls.Add(progress);

            status = new Label();
            status.Text = "Listo. Selecciona un cliente instalado y verifica la huella del servidor antes de registrar una cuenta.";
            status.ForeColor = muted;
            status.Location = new Point(26, 550);
            status.Size = new Size(830, 24);
            Controls.Add(status);

            outputBox = new TextBox();
            outputBox.Multiline = true;
            outputBox.ReadOnly = true;
            outputBox.ScrollBars = ScrollBars.Vertical;
            outputBox.BackColor = Color.FromArgb(10, 14, 20);
            outputBox.ForeColor = foreground;
            outputBox.BorderStyle = BorderStyle.FixedSingle;
            outputBox.Font = new Font("Consolas", 8.5F);
            outputBox.Location = new Point(24, 580);
            outputBox.Size = new Size(832, 128);
            Controls.Add(outputBox);

            string detectedClient = FindExistingClient();
            if (detectedClient != null)
            {
                targetBox.Text = detectedClient;
                status.Text = "Cliente WoW 3.3.5a build 12340 detectado.";
            }

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (Volatile.Read(ref operationRunning) != 0)
                {
                    e.Cancel = true;
                    MessageBox.Show(this, "Espera a que termine la operación en curso antes de cerrar el launcher.",
                        "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
        }

        private Panel MakeCard(int x, int y, int width, int height)
        {
            Panel result = new Panel();
            result.Location = new Point(x, y);
            result.Size = new Size(width, height);
            result.BackColor = panel;
            return result;
        }

        private void AddSectionTitle(Control parent, string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            label.ForeColor = Color.FromArgb(188, 208, 230);
            label.Location = new Point(x, y);
            label.Size = new Size(350, 22);
            parent.Controls.Add(label);
        }

        private void AddFieldLabel(Control parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = muted;
            label.Location = new Point(x, y + 3);
            label.Size = new Size(width, 22);
            parent.Controls.Add(label);
        }

        private TextBox AddInput(Control parent, string text, int x, int y, int width)
        {
            TextBox box = new TextBox();
            box.Text = text;
            box.Location = new Point(x, y);
            box.Size = new Size(width, 25);
            box.BackColor = Color.FromArgb(239, 243, 248);
            box.ForeColor = Color.FromArgb(25, 34, 45);
            box.BorderStyle = BorderStyle.FixedSingle;
            parent.Controls.Add(box);
            return box;
        }

        private Button MakeButton(string text, int x, int y, int width, int height, Color back, Color fore)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void BrowseTarget()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta que contiene el cliente WoW instalado.";
                dialog.SelectedPath = targetBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    targetBox.Text = dialog.SelectedPath;
            }
        }

        private void SelectClient()
        {
            DialogResult choice = MessageBox.Show(this,
                "Sí: seleccionar una carpeta de cliente instalada.\r\nNo: instalar desde un paquete ZIP completo.",
                "Cliente WoW", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel)
                return;
            if (choice == DialogResult.No)
            {
                InstallClientPackage();
                return;
            }

            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona una carpeta de cliente WoW 3.3.5a que ya tengas instalada.";
                dialog.SelectedPath = targetBox.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                string invalidClient = ClientValidation.GetInvalidClient(dialog.SelectedPath);
                if (invalidClient != null)
                {
                    ShowWarning("La carpeta no contiene un cliente WoW 3.3.5a build 12340 completo: " + invalidClient + ".");
                    return;
                }
                targetBox.Text = dialog.SelectedPath;
                status.Text = "Cliente seleccionado. Realmlist configurada al pulsar JUGAR.";
            }
        }

        private void InstallClientPackage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona el ZIP completo del cliente WoW 3.3.5a";
                dialog.Filter = "Paquete ZIP del cliente (*.zip)|*.zip";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string package = dialog.FileName;
                string target = targetBox.Text.Trim();
                if (String.IsNullOrWhiteSpace(target))
                {
                    ShowWarning("Indica una carpeta nueva para instalar el cliente.");
                    return;
                }
                string realm = realmBox.Text.Trim();
                if (!Regex.IsMatch(realm, "^[A-Za-z0-9.-]+$"))
                {
                    ShowWarning("La dirección del reino debe ser una IP o nombre DNS, sin puerto ni ruta.");
                    return;
                }

                RunOperation("Instalando cliente", delegate(Action<string> log, Action<int, string> report)
                {
                    ClientPackage.InstallZip(package, target, delegate(long done, long total, string path)
                    {
                        int percent = total <= 0 ? 0 : (int)Math.Min(99, done * 100 / total);
                        report(percent, "Instalando " + path);
                    });
                    foreach (string locale in new[] { "enUS", "enGB", "esES" })
                    {
                        string localeDirectory = Path.Combine(target, "Data", locale);
                        if (!Directory.Exists(localeDirectory))
                            continue;
                        File.WriteAllText(Path.Combine(localeDirectory, "realmlist.wtf"),
                            "set realmlist " + realm + "\r\n", Encoding.ASCII);
                    }
                    BeginInvoke((MethodInvoker)delegate { targetBox.Text = target; });
                    log("Cliente instalado y realmlist configurada: " + realm);
                    report(100, "Cliente instalado y listo para jugar.");
                });
            }
        }

        private static string FindExistingClient()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates = new[]
            {
                Path.Combine(Application.StartupPath, "Client"),
                Path.Combine(documents, "World of Warcraft 3.3.5a"),
                Path.Combine(programFiles, "World of Warcraft"),
                Path.Combine(programFilesX86, "World of Warcraft"),
                Path.Combine(programFiles, "World of Warcraft 3.3.5a"),
                Path.Combine(programFilesX86, "World of Warcraft 3.3.5a")
            };
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(candidate) && ClientValidation.GetInvalidClient(candidate) == null)
                    return candidate;
            }
            return null;
        }

        private void OpenClientFolder()
        {
            string target = targetBox.Text.Trim();
            if (ClientValidation.GetInvalidClient(target) != null)
            {
                ShowWarning("No se encontró un cliente WoW 3.3.5a build 12340 completo en la carpeta seleccionada.");
                return;
            }
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "explorer.exe";
            start.Arguments = "\"" + target + "\"";
            Process.Start(start);
        }

        private void RunOperation(string name, Action<Action<string>, Action<int, string>> operation)
        {
            if (Interlocked.CompareExchange(ref operationRunning, 1, 0) != 0)
                return;
            foreach (Button button in actionButtons)
                button.Enabled = false;
            progress.Value = 0;
            status.Text = name + "...";
            AppendLog("> " + name);

            Thread worker = new Thread(delegate()
            {
                try
                {
                    operation(AppendLog, ReportProgress);
                    BeginInvoke((MethodInvoker)delegate
                    {
                        progress.Value = 100;
                        status.Text = name + " completado.";
                        AppendLog(name + " completado.");
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        status.Text = "La operación ha fallado.";
                        AppendLog("ERROR: " + ex.Message);
                        MessageBox.Show(this, ex.Message, "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
                finally
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        foreach (Button button in actionButtons)
                            button.Enabled = true;
                        Interlocked.Exchange(ref operationRunning, 0);
                    });
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void AppendLog(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { AppendLog(message); }); }
                catch (InvalidOperationException) { }
                return;
            }
            outputBox.AppendText(message + Environment.NewLine);
            if (outputBox.TextLength > 12000)
            {
                outputBox.Text = outputBox.Text.Substring(outputBox.TextLength - 9000);
                outputBox.SelectionStart = outputBox.TextLength;
                outputBox.ScrollToCaret();
            }
        }

        private void ReportProgress(int value, string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    progress.Value = Math.Max(progress.Minimum, Math.Min(progress.Maximum, value));
                    if (!String.IsNullOrEmpty(message))
                        status.Text = message;
                });
            }
            catch (InvalidOperationException) { }
        }

        private static HttpWebRequest CreatePinnedRequest(Uri uri, string pin)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET";
            request.Proxy = WebRequest.DefaultWebProxy;
            request.ServicePoint.Expect100Continue = false;
            request.ServerCertificateValidationCallback = delegate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors errors)
            {
                if (certificate == null)
                    return false;
                using (SHA256 sha = SHA256.Create())
                {
                    string actual = ManifestTools.ToHex(sha.ComputeHash(certificate.GetRawCertData()));
                    return String.Equals(actual, pin, StringComparison.OrdinalIgnoreCase);
                }
            };
            return request;
        }

        internal static Uri BuildRegistrationUri(Uri server)
        {
            return new Uri(server, "/api/register");
        }

        private void Play()
        {
            string realm = realmBox.Text.Trim();
            if (!Regex.IsMatch(realm, "^[A-Za-z0-9.-]+$"))
            {
                ShowWarning("La dirección del reino debe ser una IP o nombre DNS, sin puerto ni ruta.");
                return;
            }
            string target = targetBox.Text.Trim();
            string executable = Path.Combine(target, "Wow.exe");
            if (!File.Exists(executable))
            {
                ShowWarning("No se encontró Wow.exe. Instala o repara el cliente primero.");
                return;
            }
            string invalidClient = ClientValidation.GetInvalidClient(target);
            if (invalidClient != null)
            {
                ShowWarning("No se puede iniciar este cliente: falta o no coincide " + invalidClient + ".");
                return;
            }

            try
            {
                foreach (string localeName in new[] { "enUS", "enGB", "esES" })
                {
                    string locale = Path.Combine(target, "Data", localeName);
                    if (!Directory.Exists(locale))
                        continue;
                    if ((File.GetAttributes(locale) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("La carpeta de idioma es un enlace y no es segura: " + locale);
                    string realmlist = Path.Combine(locale, "realmlist.wtf");
                    if (File.Exists(realmlist) && (File.GetAttributes(realmlist) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("El archivo realmlist.wtf es un enlace y no es seguro.");
                    File.WriteAllText(realmlist, "set realmlist " + realm + "\r\n", Encoding.ASCII);
                }
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = executable;
                start.WorkingDirectory = target;
                Process.Start(start);
                status.Text = "WoW iniciado. Realmlist: " + realm;
                AppendLog("WoW iniciado. Realmlist: " + realm);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo iniciar WoW: " + ex.Message, "AzerothCore Launcher",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PrepareServer()
        {
            DialogResult answer = MessageBox.Show(this,
                "Se iniciará un servicio HTTPS solo para crear cuentas. No distribuye archivos del cliente. " +
                "El servicio acepta registros desde la red local y usa el puerto 8787. ¿Continuar?",
                "Activar cuentas LAN", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            RunOperation("Activando servicio de cuentas", delegate(Action<string> log, Action<int, string> report)
            {
                string fingerprint = LauncherServer.GetFingerprint();
                LauncherServer.EnsureServerRunning();
                BeginInvoke((MethodInvoker)delegate
                {
                    pinBox.Text = fingerprint;
                    status.Text = "Servicio de cuentas activo. Comparte la huella por un canal independiente.";
                });
                log("Servicio HTTPS de cuentas activo en " + serverBox.Text.Trim());
                log("Huella SHA-256: " + fingerprint);
            });
        }

        private void RegisterAccount()
        {
            string username = usernameBox.Text.Trim();
            string password = passwordBox.Text;
            if (password != confirmBox.Text)
            {
                ShowWarning("Las contraseñas no coinciden.");
                return;
            }
            if (!LauncherServer.IsValidAccount(username, password))
            {
                ShowWarning("Usuario: 3-16 letras, números o _. Contraseña: 8-16 caracteres permitidos.");
                return;
            }
            string pin = NormalizePin(pinBox.Text);
            if (!Regex.IsMatch(pin, "^[a-f0-9]{64}$"))
            {
                ShowWarning("Introduce la huella SHA-256 del servidor y compruébala por separado.");
                return;
            }
            Uri server;
            if (!TryNormalizeServer(serverBox.Text, out server))
            {
                ShowWarning("Introduce una dirección HTTPS válida para el servidor.");
                return;
            }

            passwordBox.Clear();
            confirmBox.Clear();
            RunOperation("Creando cuenta", delegate(Action<string> log, Action<int, string> report)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Uri registrationUri = BuildRegistrationUri(server);
                HttpWebRequest request = CreatePinnedRequest(registrationUri, pin);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 90000;
                request.ReadWriteTimeout = 90000;
                byte[] payload = Encoding.UTF8.GetBytes(JsonTools.Serialize(new AccountRequest { Username = username, Password = password }));
                request.ContentLength = payload.Length;
                using (Stream output = request.GetRequestStream())
                    output.Write(payload, 0, payload.Length);

                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream input = response.GetResponseStream())
                    {
                        ApiResponse result = JsonTools.Deserialize<ApiResponse>(input);
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300 || result == null || !result.Ok)
                            throw new InvalidOperationException(result == null ? "El servidor devolvió una respuesta no válida." : result.Message);
                        log(result.Message);
                    }
                }
                catch (WebException ex)
                {
                    HttpWebResponse response = ex.Response as HttpWebResponse;
                    if (response == null)
                        throw;
                    using (response)
                    using (Stream input = response.GetResponseStream())
                    {
                        ApiResponse result = JsonTools.Deserialize<ApiResponse>(input);
                        throw new InvalidOperationException(result == null ? "El servidor rechazó el registro." : result.Message);
                    }
                }
                report(100, "Cuenta creada.");
            });
        }

        private void CopyFingerprint()
        {
            if (String.IsNullOrWhiteSpace(pinBox.Text))
            {
                ShowWarning("Prepara primero el servidor LAN para generar la huella.");
                return;
            }
            Clipboard.SetText(pinBox.Text.Trim());
            MessageBox.Show(this, "Huella copiada. Compártela con tus amigos por un canal separado del launcher.",
                "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool TryNormalizeServer(string value, out Uri uri)
        {
            uri = null;
            string server = (value ?? String.Empty).Trim();
            if (String.IsNullOrEmpty(server))
                return false;
            if (!server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Regex.IsMatch(server, ":[0-9]+$"))
                    server += ":" + LauncherServer.Port;
                server = "https://" + server;
            }
            if (!Uri.TryCreate(server.TrimEnd('/'), UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Query) ||
                !String.IsNullOrEmpty(uri.Fragment))
            {
                uri = null;
                return false;
            }
            return true;
        }

        private static string NormalizePin(string value)
        {
            return Regex.Replace((value ?? String.Empty).Trim(), "[: ]", String.Empty).ToLowerInvariant();
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

}
