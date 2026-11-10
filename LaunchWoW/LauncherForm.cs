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

        private readonly Color background = Color.FromArgb(8, 18, 31);
        private readonly Color panel = Color.FromArgb(18, 29, 43);
        private readonly Color muted = Color.FromArgb(151, 174, 193);
        private readonly Color foreground = Color.FromArgb(240, 231, 205);
        private readonly Color accent = Color.FromArgb(217, 182, 124);
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
        private Label homeStatus;
        private Panel homeProgress;
        private Image heroArtwork;
        private int homeProgressPercent;
        private bool autoPlayAfterInstall = true;
        private bool existingClientDetected;
        private readonly List<Button> actionButtons = new List<Button>();
        private int operationRunning;
        private readonly object progressUpdateLock = new object();
        private int pendingProgressValue;
        private string pendingProgressMessage;
        private bool progressUpdateQueued;

        internal LauncherForm()
        {
            Text = "AzerotBatzoki | Launcher 1.2.2";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 730);
            MinimumSize = SizeFromClientSize(new Size(1000, 660));
            BackColor = background;
            ForeColor = foreground;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(880, 102);
            header.BackColor = Color.FromArgb(12, 25, 40);
            header.Paint += PaintBatzokiHeader;
            Controls.Add(header);

            Label title = new Label();
            title.Text = "AZEROTBATZOKI";
            title.Font = new Font("Palatino Linotype", 22F, FontStyle.Bold);
            title.ForeColor = foreground;
            title.Location = new Point(30, 15);
            title.Size = new Size(420, 42);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "CUENTA, CLIENTE Y CONEXIÓN";
            subtitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            subtitle.ForeColor = muted;
            subtitle.Location = new Point(31, 63);
            subtitle.Size = new Size(460, 22);
            header.Controls.Add(subtitle);

            Label buildBadge = new Label();
            buildBadge.Text = "REINO PRIVADO  ·  ES";
            buildBadge.TextAlign = ContentAlignment.MiddleCenter;
            buildBadge.ForeColor = Color.FromArgb(205, 222, 243);
            buildBadge.BackColor = Color.FromArgb(24, 46, 62);
            buildBadge.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            buildBadge.Location = new Point(676, 36);
            buildBadge.Size = new Size(172, 28);
            header.Controls.Add(buildBadge);

            Panel serverCard = MakeCard(24, 120, 832, 191);
            Controls.Add(serverCard);
            AddSectionTitle(serverCard, "Servidor y cliente", 18, 12);
            AddFieldLabel(serverCard, "Servidor HTTPS", 18, 48, 170);
            serverBox = AddInput(serverCard, DefaultServerAddress, 190, 45, 621);
            AddFieldLabel(serverCard, "Huella SHA-256", 18, 83, 170);
            pinBox = AddInput(serverCard, "", 190, 80, 621);
            AddFieldLabel(serverCard, "Dirección del reino", 18, 118, 170);
            realmBox = AddInput(serverCard, DefaultRealmAddress, 190, 115, 621);
            AddFieldLabel(serverCard, "Carpeta del cliente", 18, 153, 170);
            targetBox = AddInput(serverCard,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerotBatzoki", "Client"),
                190, 150, 524);
            Button browse = MakeButton("Examinar", 722, 149, 91, 29, panel, foreground);
            browse.Click += delegate { BrowseTarget(); };
            serverCard.Controls.Add(browse);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Location = new Point(24, 326);
            actions.Size = new Size(832, 56);
            actions.WrapContents = false;
            actions.BackColor = background;
            Controls.Add(actions);
            Button install = MakeButton("Elegir cliente", 0, 0, 178, 46, accent, Color.White);
            Button repair = MakeButton("Abrir carpeta", 0, 0, 152, 46, panel, foreground);
            Button play = MakeButton("JUGAR", 0, 0, 132, 46, Color.FromArgb(50, 128, 83), Color.White);
            Button host = MakeButton("Compartir cliente", 0, 0, 174, 46, panel, foreground);
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
            AddSectionTitle(accountCard, "Crear cuenta", 18, 10);
            AddFieldLabel(accountCard, "Usuario", 18, 45, 75);
            usernameBox = AddInput(accountCard, "", 92, 42, 170);
            AddFieldLabel(accountCard, "Contraseña", 278, 45, 90);
            passwordBox = AddInput(accountCard, "", 367, 42, 156);
            passwordBox.UseSystemPasswordChar = true;
            AddFieldLabel(accountCard, "Repetir", 539, 45, 62);
            confirmBox = AddInput(accountCard, "", 600, 42, 213);
            confirmBox.UseSystemPasswordChar = true;
            Button register = MakeButton("Crear cuenta", 18, 75, 166, 28, accent, Color.White);
            Button copyPin = MakeButton("Copiar huella", 194, 75, 145, 28, panel, foreground);
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
            status.Text = "Ongi etorri. Instala el cliente o selecciona uno existente para entrar al reino.";
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
                existingClientDetected = true;
                targetBox.Text = detectedClient;
                status.Text = "Cliente WoW 3.3.5a build 12340 detectado.";
            }

            BuildBatzokiHome();
            Shown += delegate { FitToWorkingArea(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

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

        private void BuildBatzokiHome()
        {
            Panel settingsPage = new Panel();
            settingsPage.Name = "Settings";
            settingsPage.Dock = DockStyle.Fill;
            Panel settingsContent = new Panel();
            settingsContent.Name = "SettingsContent";
            settingsContent.Size = new Size(880, 735);
            settingsContent.BackColor = background;
            settingsPage.Controls.Add(settingsContent);
            settingsPage.AutoScrollMinSize = settingsContent.Size;
            settingsPage.BackColor = background;
            settingsPage.AutoScroll = true;
            Control[] existing = Controls.Cast<Control>().ToArray();
            foreach (Control control in existing)
            {
                Controls.Remove(control);
                settingsContent.Controls.Add(control);
            }
            Controls.Add(settingsPage);
            settingsPage.Visible = false;
            Action centerSettings = delegate
            {
                settingsContent.Location = new Point(Math.Max(0, (settingsPage.ClientSize.Width - settingsContent.Width) / 2), 0);
            };
            settingsPage.SizeChanged += delegate { centerSettings(); };
            centerSettings();

            Button back = MakeButton("Volver", 790, 7, 72, 25, Color.FromArgb(25, 38, 53), foreground);
            back.Click += delegate
            {
                settingsPage.Visible = false;
                Control homePage = Controls["Home"];
                homePage.Visible = true;
                homePage.BringToFront();
                homePage.SelectNextControl(null, true, true, true, false);
            };
            settingsContent.Controls.Add(back);
            back.BringToFront();

            Panel home = new PaintedPanel();
            home.Name = "Home";
            home.Dock = DockStyle.Fill;
            home.BackColor = background;
            home.Paint += PaintBatzokiHome;
            Controls.Add(home);
            home.BringToFront();

            Panel brand = new PaintedPanel();
            brand.Location = new Point(24, 7);
            brand.Size = new Size(235, 100);
            brand.BackColor = Color.Transparent;
            brand.Paint += PaintBatzokiCrest;
            brand.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            home.Controls.Add(brand);

            Label banner = new Label();
            banner.Text = "Wrath of the Lich King   /   3.3.5a";
            banner.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            banner.ForeColor = Color.FromArgb(226, 192, 116);
            banner.BackColor = Color.Transparent;
            banner.TextAlign = ContentAlignment.MiddleRight;
            banner.Location = new Point(783, 32);
            banner.Size = new Size(280, 28);
            banner.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            home.Controls.Add(banner);

            Panel news = new PaintedPanel();
            news.Location = new Point(24, 111);
            news.Size = new Size(260, 463);
            news.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            news.BackColor = Color.FromArgb(12, 25, 40);
            home.Controls.Add(news);
            Label newsTitle = new Label();
            newsTitle.Text = "CRÓNICAS DEL BATZOKI";
            newsTitle.Font = new Font("Georgia", 13F, FontStyle.Bold);
            newsTitle.ForeColor = Color.FromArgb(244, 206, 117);
            newsTitle.Location = new Point(17, 18);
            newsTitle.Size = new Size(226, 45);
            news.Controls.Add(newsTitle);
            Label newsCopy = new Label();
            newsCopy.Text = "ONGI ETORRI, AVENTURERO\n\nEl fuego del Batzoki vuelve a arder. Reúne a tu hermandad y escribe vuestra historia en Azeroth.\n\nREINO\nAzerotBatzoki · Wrath 3.3.5a\n\nCOMUNIDAD\nUn mundo, una cuadrilla.";
            newsCopy.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            newsCopy.ForeColor = foreground;
            newsCopy.Location = new Point(18, 67);
            newsCopy.Size = new Size(220, 280);
            newsCopy.BackColor = Color.Transparent;
            news.Controls.Add(newsCopy);
            Label ikurrina = new Label();
            ikurrina.Text = "MÁS CRÓNICAS";
            ikurrina.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            ikurrina.ForeColor = Color.FromArgb(211, 224, 238);
            ikurrina.BackColor = Color.FromArgb(31, 58, 77);
            ikurrina.TextAlign = ContentAlignment.MiddleCenter;
            ikurrina.Location = new Point(18, 397);
            ikurrina.Size = new Size(220, 42);
            ikurrina.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            news.Controls.Add(ikurrina);

            Panel hero = new PaintedPanel();
            hero.Location = new Point(298, 111);
            hero.Size = new Size(765, 463);
            hero.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            hero.BackColor = Color.FromArgb(10, 20, 35);
            hero.Paint += PaintBatzokiScene;
            home.Controls.Add(hero);
            Label sceneCaption = new Label();
            sceneCaption.Text = "NORTHREND  ·  EL REINO AGUARDA";
            sceneCaption.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            sceneCaption.ForeColor = Color.FromArgb(224, 231, 236);
            sceneCaption.BackColor = Color.FromArgb(170, 7, 16, 27);
            sceneCaption.TextAlign = ContentAlignment.MiddleLeft;
            sceneCaption.Location = new Point(316, 534);
            sceneCaption.Size = new Size(400, 27);
            sceneCaption.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            home.Controls.Add(sceneCaption);

            homeStatus = new Label();
            homeStatus.Text = existingClientDetected ? "Cliente 3.3.5a detectado · listo para entrar" : "Cliente no detectado · instala o selecciona tu cliente";
            homeStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            homeStatus.ForeColor = existingClientDetected ? Color.FromArgb(122, 224, 149) : Color.FromArgb(237, 190, 105);
            homeStatus.Location = new Point(302, 589);
            homeStatus.Size = new Size(550, 22);
            homeStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            home.Controls.Add(homeStatus);

            homeProgress = new PaintedPanel();
            homeProgress.Location = new Point(302, 614);
            homeProgress.Size = new Size(760, 14);
            homeProgress.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            homeProgress.BackColor = Color.FromArgb(5, 9, 15);
            homeProgress.Paint += PaintHomeProgress;
            home.Controls.Add(homeProgress);

            CheckBox autoPlay = new CheckBox();
            autoPlay.Text = "Abrir el juego al terminar la instalación";
            autoPlay.Checked = true;
            autoPlay.ForeColor = foreground;
            autoPlay.BackColor = Color.Transparent;
            autoPlay.Location = new Point(28, 650);
            autoPlay.Size = new Size(260, 27);
            autoPlay.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            home.Controls.Add(autoPlay);
            autoPlay.CheckedChanged += delegate { autoPlayAfterInstall = autoPlay.Checked; };

            Button options = MakeButton("CUENTA · AJUSTES", 300, 650, 160, 35, Color.FromArgb(20, 33, 47), foreground);
            options.FlatAppearance.BorderSize = 1;
            options.FlatAppearance.BorderColor = Color.FromArgb(88, 108, 124);
            options.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            AddButtonHover(options, Color.FromArgb(31, 51, 68), Color.FromArgb(128, 158, 178));
            options.Click += delegate
            {
                home.Visible = false;
                settingsPage.Visible = true;
                settingsPage.BringToFront();
                centerSettings();
                serverBox.Focus();
            };
            home.Controls.Add(options);

            Button update = MakeButton("ACTUALIZAR", 472, 643, 150, 52, Color.FromArgb(29, 62, 76), Color.FromArgb(196, 224, 229));
            update.FlatAppearance.BorderSize = 1;
            update.FlatAppearance.BorderColor = Color.FromArgb(91, 153, 171);
            update.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AddButtonHover(update, Color.FromArgb(39, 85, 103), Color.FromArgb(125, 196, 215));
            update.Click += delegate { UpdateClient(); };
            home.Controls.Add(update);
            actionButtons.Add(update);

            Button install = MakeButton("INSTALAR", 634, 643, 150, 52, Color.FromArgb(27, 51, 69), Color.FromArgb(242, 211, 143));
            install.FlatAppearance.BorderSize = 1;
            install.FlatAppearance.BorderColor = accent;
            install.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            install.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AddButtonHover(install, Color.FromArgb(39, 71, 93), Color.FromArgb(228, 187, 94));
            install.Click += delegate { SelectClient(); };
            home.Controls.Add(install);
            actionButtons.Add(install);
            Button play = MakeButton("JUGAR", 796, 638, 266, 61, Color.FromArgb(124, 37, 34), Color.FromArgb(255, 226, 150));
            play.FlatAppearance.BorderSize = 2;
            play.FlatAppearance.BorderColor = Color.FromArgb(218, 169, 73);
            play.Font = new Font("Georgia", 17F, FontStyle.Bold);
            play.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            AddButtonHover(play, Color.FromArgb(151, 52, 45), Color.FromArgb(255, 216, 134));
            play.Click += delegate { Play(); };
            home.Controls.Add(play);
            actionButtons.Add(play);
            ArrangeHome(home, brand, banner, news, newsTitle, newsCopy, ikurrina,
                hero, sceneCaption, autoPlay, options, update, install, play);
        }

        private void ArrangeHome(Panel home, Panel brand, Label banner, Panel news,
            Label newsTitle, Label newsCopy, Label edition, Panel hero, Label caption,
            CheckBox autoPlay, params Button[] buttons)
        {
            home.SuspendLayout();
            home.Controls.Clear();
            TableLayoutPanel layout = CreateLayout(3, 4);
            layout.Padding = new Padding(28, 12, 28, 24);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            home.Controls.Add(layout);
            brand.Dock = DockStyle.Left;
            brand.Margin = Padding.Empty;
            layout.Controls.Add(brand, 0, 0);
            banner.Dock = DockStyle.Fill;
            banner.Margin = new Padding(0, 12, 12, 12);
            layout.Controls.Add(banner, 1, 0);
            layout.SetColumnSpan(banner, 2);
            news.Dock = DockStyle.Fill;
            news.Margin = Padding.Empty;
            layout.Controls.Add(news, 0, 1);
            news.Controls.Clear();
            TableLayoutPanel newsLayout = CreateLayout(1, 3);
            newsLayout.Padding = new Padding(16);
            newsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            newsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            newsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            newsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            news.Controls.Add(newsLayout);
            newsTitle.Text = "Ongi etorri.";
            newsTitle.Font = new Font("Palatino Linotype", 20F, FontStyle.Bold);
            newsTitle.Dock = DockStyle.Fill;
            newsTitle.Margin = Padding.Empty;
            newsLayout.Controls.Add(newsTitle, 0, 0);
            newsCopy.Dock = DockStyle.Fill;
            newsCopy.Margin = new Padding(0, 6, 0, 8);
            newsCopy.Font = new Font("Segoe UI", 9.5F);
            newsCopy.ForeColor = Color.FromArgb(179, 197, 209);
            newsCopy.Text = "Un reino, una cuadrilla.\n\nReúne a tu hermandad y vuelve a escribir tu historia en Azeroth.\n\nTU REINO\nAzerotBatzoki\n\nTU AVENTURA\nWrath of the Lich King\n\nAntes de actualizar, cierra WoW. Tus ajustes se conservan.";
            newsLayout.Controls.Add(newsCopy, 0, 1);
            edition.Text = "WoW 3.3.5a · BUILD 12340";
            edition.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            edition.BackColor = Color.Transparent;
            edition.ForeColor = muted;
            edition.Dock = DockStyle.Fill;
            edition.Margin = Padding.Empty;
            newsLayout.Controls.Add(edition, 0, 2);
            hero.Dock = DockStyle.Fill;
            hero.Margin = Padding.Empty;
            layout.Controls.Add(hero, 2, 1);
            caption.Dock = DockStyle.Bottom;
            caption.Height = 30;
            caption.Padding = new Padding(12, 0, 0, 0);
            caption.BackColor = Color.FromArgb(12, 21, 34);
            caption.Dispose();
            TableLayoutPanel state = CreateLayout(1, 2);
            state.Padding = new Padding(0, 10, 0, 8);
            state.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            state.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            state.RowStyles.Add(new RowStyle(SizeType.Absolute, 6));
            layout.Controls.Add(state, 2, 2);
            homeStatus.Dock = DockStyle.Fill;
            homeStatus.Margin = Padding.Empty;
            homeStatus.AutoEllipsis = true;
            homeStatus.AccessibleName = "Estado del cliente";
            state.Controls.Add(homeStatus, 0, 0);
            homeProgress.Dock = DockStyle.Fill;
            homeProgress.Margin = Padding.Empty;
            state.Controls.Add(homeProgress, 0, 1);
            autoPlay.Dock = DockStyle.Fill;
            autoPlay.Margin = new Padding(0, 4, 8, 4);
            autoPlay.Font = new Font("Segoe UI", 9F);
            autoPlay.Text = "Abrir WoW al terminar la instalación";
            layout.Controls.Add(autoPlay, 0, 3);
            TableLayoutPanel toolbar = CreateLayout(4, 1);
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(toolbar, 2, 3);
            for (int i = 0; i < buttons.Length; i++)
            {
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i == 3 ? 31 : 23));
                buttons[i].Dock = DockStyle.Fill;
                buttons[i].Margin = new Padding(0, 4, i == 3 ? 0 : 8, 4);
                buttons[i].TabIndex = i;
                buttons[i].AccessibleName = buttons[i].Text;
                toolbar.Controls.Add(buttons[i], i, 0);
            }
            string[] names = { "Cuenta y ajustes", "Actualizar", "Instalar cliente", "Jugar" };
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                button.Text = names[i];
                button.AccessibleName = names[i];
                button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                button.BackColor = Color.FromArgb(18, 36, 53);
                button.ForeColor = Color.FromArgb(213, 224, 229);
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Color.FromArgb(54, 79, 96);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 53, 72);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(13, 27, 41);
            }
            buttons[3].Font = new Font("Palatino Linotype", 17F, FontStyle.Bold);
            buttons[3].BackColor = accent;
            buttons[3].ForeColor = Color.FromArgb(18, 27, 34);
            buttons[3].FlatAppearance.BorderColor = Color.FromArgb(241, 211, 157);
            buttons[3].FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 208, 156);
            buttons[3].FlatAppearance.MouseDownBackColor = Color.FromArgb(191, 153, 92);
            home.ResumeLayout(true);
        }

        private static TableLayoutPanel CreateLayout(int columns, int rows)
        {
            TableLayoutPanel result = new TableLayoutPanel();
            result.ColumnCount = columns;
            result.RowCount = rows;
            result.Dock = DockStyle.Fill;
            result.Margin = Padding.Empty;
            result.BackColor = Color.Transparent;
            result.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            return result;
        }

        // Repaint the full surface after resizing, including transparent children.
        private sealed class PaintedPanel : Panel
        {
            internal PaintedPanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        private void PaintBatzokiCrest(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Icon != null) g.DrawIcon(Icon, new Rectangle(0, 14, 62, 62));
            using (Font title = new Font("Palatino Linotype", 17F, FontStyle.Bold))
            using (SolidBrush gold = new SolidBrush(Color.FromArgb(231, 202, 149)))
                g.DrawString("AZEROT\nBATZOKI", title, gold, new RectangleF(70, 12, 162, 58));
            using (Font small = new Font("Segoe UI", 7F))
            using (SolidBrush dim = new SolidBrush(muted))
                g.DrawString("EL BATZOKI DE AZEROTH", small, dim, 72, 73);
        }

        private void PaintHomeProgress(object sender, PaintEventArgs e)
        {
            Panel bar = (Panel)sender;
            if (bar.Width <= 0 || bar.Height <= 0) return;
            using (SolidBrush rail = new SolidBrush(Color.FromArgb(31, 52, 69)))
                e.Graphics.FillRectangle(rail, bar.ClientRectangle);
            int width = (int)((long)bar.Width * homeProgressPercent / 100);
            if (width > 0)
                using (SolidBrush fill = new SolidBrush(accent))
                    e.Graphics.FillRectangle(fill, 0, 0, width, bar.Height);
        }

        private void PaintBatzokiHome(object sender, PaintEventArgs e)
        {
            Control surface = (Control)sender;
            if (surface.Width <= 0 || surface.Height <= 0) return;
            using (System.Drawing.Drawing2D.LinearGradientBrush wash = new System.Drawing.Drawing2D.LinearGradientBrush(
                surface.ClientRectangle, Color.FromArgb(8, 18, 31), Color.FromArgb(16, 31, 45), 90F))
                e.Graphics.FillRectangle(wash, surface.ClientRectangle);
            using (Pen rule = new Pen(Color.FromArgb(40, 61, 77)))
                e.Graphics.DrawLine(rule, 28, 99, surface.Width - 28, 99);
        }

        private void PaintBatzokiScene(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = ((Control)sender).Width, h = ((Control)sender).Height;
            if (w <= 0 || h <= 0) return;
            Image suppliedArtwork = LoadHeroArtwork();
            if (suppliedArtwork != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                // Preserve the complete illustration, including its foreground details.
                float scale = Math.Min((float)w / suppliedArtwork.Width, (float)h / suppliedArtwork.Height);
                float imageWidth = suppliedArtwork.Width * scale, imageHeight = suppliedArtwork.Height * scale;
                g.Clear(Color.FromArgb(6, 14, 24));
                g.DrawImage(suppliedArtwork, new RectangleF((w - imageWidth) / 2, (h - imageHeight) / 2,
                    imageWidth, imageHeight), new RectangleF(0, 0, suppliedArtwork.Width, suppliedArtwork.Height), GraphicsUnit.Pixel);
                DrawOrnateFrame(g, w, h);
                return;
            }
            using (System.Drawing.Drawing2D.LinearGradientBrush sky = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, w, h), Color.FromArgb(42, 92, 139), Color.FromArgb(7, 20, 39), 90F))
                g.FillRectangle(sky, 0, 0, w, h);
            // Moon, stars and thin northern lights establish depth behind the citadel.
            using (Pen aurora = new Pen(Color.FromArgb(42, 95, 186, 201), 9))
            {
                aurora.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawBezier(aurora, 24, 119, 173, 4, 269, 155, 445, 76);
            }
            using (SolidBrush moon = new SolidBrush(Color.FromArgb(205, 199, 221, 230))) g.FillEllipse(moon, 185, 48, 104, 104);
            using (SolidBrush crater = new SolidBrush(Color.FromArgb(55, 122, 155, 180)))
            {
                g.FillEllipse(crater, 207, 74, 17, 13); g.FillEllipse(crater, 247, 65, 11, 9);
                g.FillEllipse(crater, 226, 119, 22, 12); g.FillEllipse(crater, 267, 105, 9, 15);
            }
            Point[] stars = { new Point(42, 54), new Point(94, 103), new Point(150, 36), new Point(337, 59), new Point(402, 112), new Point(657, 77), new Point(715, 44), new Point(375, 167), new Point(74, 158), new Point(699, 196) };
            using (Pen star = new Pen(Color.FromArgb(184, 221, 236), 1))
                foreach (Point p in stars) { g.DrawLine(star, p.X - 3, p.Y, p.X + 3, p.Y); g.DrawLine(star, p.X, p.Y - 3, p.X, p.Y + 3); }

            // Three cold mountain ranges, each with a lit snow face.
            DrawRidge(g, new[] { new Point(0, 342), new Point(68, 229), new Point(110, 278), new Point(160, 191), new Point(215, 281), new Point(269, 217), new Point(324, 307), new Point(382, 211), new Point(447, 326), new Point(515, 235), new Point(583, 337), new Point(651, 211), new Point(765, 350), new Point(765, 463), new Point(0, 463) }, Color.FromArgb(48, 91, 126));
            DrawRidge(g, new[] { new Point(0, 382), new Point(96, 289), new Point(143, 331), new Point(225, 246), new Point(287, 339), new Point(359, 276), new Point(417, 350), new Point(506, 258), new Point(573, 343), new Point(642, 278), new Point(765, 387), new Point(765, 463), new Point(0, 463) }, Color.FromArgb(29, 66, 99));
            using (SolidBrush snow = new SolidBrush(Color.FromArgb(163, 195, 212)))
            {
                g.FillPolygon(snow, new[] { new Point(125, 252), new Point(160, 191), new Point(199, 255), new Point(164, 236), new Point(145, 259) });
                g.FillPolygon(snow, new[] { new Point(345, 273), new Point(382, 211), new Point(423, 283), new Point(387, 252), new Point(365, 278) });
                g.FillPolygon(snow, new[] { new Point(613, 271), new Point(651, 211), new Point(702, 288), new Point(658, 257), new Point(638, 277) });
            }

            // Far pine forest silhouettes.
            using (SolidBrush pines = new SolidBrush(Color.FromArgb(10, 29, 48)))
                for (int x = 0; x < w; x += 25)
                {
                    int top = 310 + (x * 17 % 71);
                    g.FillPolygon(pines, new[] { new Point(x, top + 44), new Point(x + 12, top), new Point(x + 24, top + 44), new Point(x + 19, top + 40), new Point(x + 25, top + 58), new Point(x - 3, top + 58) });
                }

            DrawCitadel(g);

            // Foreground snow shelf and a winding path toward the gate.
            using (SolidBrush snowShelf = new SolidBrush(Color.FromArgb(199, 212, 219)))
                g.FillPolygon(snowShelf, new[] { new Point(0, 403), new Point(106, 366), new Point(204, 392), new Point(294, 355), new Point(374, 394), new Point(476, 365), new Point(588, 398), new Point(690, 361), new Point(765, 382), new Point(765, 463), new Point(0, 463) });
            using (SolidBrush shelfShade = new SolidBrush(Color.FromArgb(112, 139, 160)))
                g.FillPolygon(shelfShade, new[] { new Point(0, 427), new Point(108, 386), new Point(213, 412), new Point(292, 374), new Point(374, 413), new Point(482, 381), new Point(588, 419), new Point(697, 378), new Point(765, 397), new Point(765, 463), new Point(0, 463) });
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            using (Pen track = new Pen(Color.FromArgb(177, 213, 224, 226), 11))
            {
                path.AddBezier(263, 463, 334, 433, 350, 414, 372, 388);
                g.DrawPath(track, path);
            }
            DrawOrnateFrame(g, w, h);
        }

        private Image LoadHeroArtwork()
        {
            if (heroArtwork != null)
                return heroArtwork;
            using (Stream stream = typeof(LauncherForm).Assembly.GetManifestResourceStream("AzerothCoreLauncher.Batzoki-Northrend.png"))
            {
                if (stream == null)
                    return null;
                using (Bitmap decoded = new Bitmap(stream))
                    heroArtwork = new Bitmap(decoded);
            }
            return heroArtwork;
        }

        private static void DrawRidge(Graphics g, Point[] points, Color color)
        {
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, points);
        }

        private static void DrawCitadel(Graphics g)
        {
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(95, 8, 17, 31)))
                g.FillEllipse(shadow, 445, 87, 226, 273);
            using (SolidBrush stone = new SolidBrush(Color.FromArgb(13, 30, 52)))
            using (SolidBrush face = new SolidBrush(Color.FromArgb(25, 48, 72)))
            using (SolidBrush ice = new SolidBrush(Color.FromArgb(74, 134, 172)))
            using (SolidBrush window = new SolidBrush(Color.FromArgb(84, 160, 204)))
            using (Pen edge = new Pen(Color.FromArgb(47, 83, 111), 2))
            {
                // Keep and outer walls.
                g.FillPolygon(stone, new[] { new Point(419, 344), new Point(433, 263), new Point(458, 263), new Point(458, 209), new Point(482, 209), new Point(482, 162), new Point(508, 162), new Point(508, 127), new Point(529, 93), new Point(550, 127), new Point(550, 162), new Point(576, 162), new Point(576, 209), new Point(600, 209), new Point(600, 264), new Point(625, 264), new Point(641, 344) });
                g.FillPolygon(face, new[] { new Point(484, 344), new Point(484, 223), new Point(509, 196), new Point(550, 196), new Point(576, 226), new Point(576, 344) });
                g.FillPolygon(stone, new[] { new Point(389, 358), new Point(389, 303), new Point(410, 303), new Point(410, 283), new Point(431, 283), new Point(431, 304), new Point(455, 304), new Point(455, 358) });
                g.FillPolygon(stone, new[] { new Point(596, 358), new Point(596, 304), new Point(619, 304), new Point(619, 283), new Point(641, 283), new Point(641, 304), new Point(663, 304), new Point(663, 358) });
                g.FillPolygon(stone, new[] { new Point(367, 357), new Point(367, 329), new Point(383, 329), new Point(383, 314), new Point(400, 314), new Point(400, 329), new Point(417, 329), new Point(417, 357) });
                g.FillPolygon(stone, new[] { new Point(635, 357), new Point(635, 329), new Point(652, 329), new Point(652, 314), new Point(669, 314), new Point(669, 329), new Point(687, 329), new Point(687, 357) });
                g.DrawLine(edge, 433, 263, 625, 263); g.DrawLine(edge, 458, 209, 600, 209);
                // Tall icy spire and its blue beacon.
                g.FillPolygon(stone, new[] { new Point(509, 162), new Point(514, 89), new Point(522, 69), new Point(529, 29), new Point(536, 69), new Point(545, 89), new Point(550, 162) });
                g.FillPolygon(ice, new[] { new Point(526, 68), new Point(529, 29), new Point(536, 69), new Point(533, 143) });
                using (Pen beam = new Pen(Color.FromArgb(112, 100, 192, 241), 3)) g.DrawLine(beam, 529, 33, 529, 138);
                // Roof edges and icy architectural highlights.
                g.DrawLine(edge, 482, 162, 529, 127); g.DrawLine(edge, 550, 127, 576, 162);
                g.DrawLine(edge, 458, 209, 482, 186); g.DrawLine(edge, 576, 186, 600, 209);
                // Narrow lit lancet windows.
                for (int x = 477; x <= 588; x += 22)
                    for (int y = 231; y < 321; y += 37)
                    {
                        g.FillEllipse(window, x, y, 7, 15);
                        using (Pen mullion = new Pen(Color.FromArgb(16, 38, 58), 1)) g.DrawLine(mullion, x + 3, y + 2, x + 3, y + 13);
                    }
                // Gate arch.
                g.FillEllipse(window, 518, 295, 23, 28);
                g.FillRectangle(window, 518, 309, 23, 37);
                using (Pen gate = new Pen(Color.FromArgb(8, 19, 32), 2)) g.DrawLine(gate, 529, 309, 529, 344);
            }
            using (Pen glow = new Pen(Color.FromArgb(69, 93, 171, 222), 3))
            {
                g.DrawLine(glow, 529, 31, 529, 147);
                g.DrawLine(glow, 530, 195, 530, 254);
            }
        }

        private static void DrawOrnateFrame(Graphics g, int w, int h)
        {
            if (w < 3 || h < 3) return;
            using (Pen edge = new Pen(Color.FromArgb(83, 111, 126)))
                g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
            using (Pen gold = new Pen(Color.FromArgb(215, 181, 122), 2))
            {
                g.DrawLine(gold, 0, 0, 26, 0);
                g.DrawLine(gold, 0, 0, 0, 26);
                g.DrawLine(gold, w - 27, h - 1, w - 1, h - 1);
                g.DrawLine(gold, w - 1, h - 27, w - 1, h - 1);
            }
        }

        private void PaintBatzokiHeader(object sender, PaintEventArgs e)
        {
            int width = ((Control)sender).Width;
            using (Pen line = new Pen(Color.FromArgb(53, 77, 93)))
                e.Graphics.DrawLine(line, 0, 100, width, 100);
            using (Pen gold = new Pen(accent, 2))
                e.Graphics.DrawLine(gold, 30, 100, 170, 100);
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
            box.BackColor = Color.FromArgb(10, 20, 32);
            box.ForeColor = foreground;
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
            button.ForeColor = back.ToArgb() == accent.ToArgb() ? background : fore;
            button.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static void AddButtonHover(Button button, Color hoverBack, Color hoverBorder)
        {
            Color normalBorder = button.FlatAppearance.BorderColor;
            button.FlatAppearance.MouseOverBackColor = hoverBack;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(hoverBack.R * 3 / 4, hoverBack.G * 3 / 4, hoverBack.B * 3 / 4);
            button.MouseEnter += delegate { button.FlatAppearance.BorderColor = hoverBorder; };
            button.MouseLeave += delegate { button.FlatAppearance.BorderColor = normalBorder; };
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
                "Sí: seleccionar un cliente ya instalado.\r\nNo: descargar e instalar el cliente de AzerotBatzoki.",
                "Cliente WoW", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel)
                return;
            if (choice == DialogResult.No)
            {
                InstallClientFromServer();
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
                SetClientReady("Cliente seleccionado · listo para entrar");
            }
        }

        private void InstallClientFromServer()
        {
            string target = targetBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(target))
            {
                ShowWarning("Indica dónde instalar el cliente.");
                return;
            }
            string realm = realmBox.Text.Trim();
            if (!Regex.IsMatch(realm, "^[A-Za-z0-9.-]+$"))
            {
                ShowWarning("La dirección del reino debe ser una IP o nombre DNS, sin puerto ni ruta.");
                return;
            }
            if (Directory.Exists(target) || File.Exists(target))
            {
                ShowWarning("La carpeta de destino ya existe. Elige una carpeta nueva para no sobrescribir archivos.");
                return;
            }

            RunOperation("Descargando cliente", delegate(Action<string> log, Action<int, string> report)
            {
                log("Conectando con el host privado " + ClientDownload.Server.Host + ".");
                ClientDownload.Install(ClientDownload.Server, ClientDownload.CertificatePin, target, realm,
                    delegate(long done, long total, string path)
                    {
                        int percent = total <= 0 ? 0 : (int)Math.Min(99, done * 100 / total);
                        report(percent, "Descargando " + path + " · " + FormatBytes(done) + " / " + FormatBytes(total));
                    });
                report(100, "Cliente instalado y verificado.");
            }, delegate
            {
                targetBox.Text = target;
                SetClientReady("Cliente instalado · listo para entrar");
                AppendLog("Cliente instalado, verificado y configurado para " + realm + ".");
                if (autoPlayAfterInstall)
                    Play();
            });
        }

        private void UpdateClient()
        {
            string target = targetBox.Text.Trim();
            if (!Directory.Exists(target))
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Selecciona la carpeta existente de WoW 3.3.5a que quieres comparar y actualizar.";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;
                    target = dialog.SelectedPath;
                }
            }
            string invalid = ClientValidation.GetInvalidClient(target);
            if (invalid != null)
            {
                ShowWarning("La carpeta no contiene un cliente WoW 3.3.5a build 12340 válido: " + invalid + ".");
                return;
            }

            targetBox.Text = target;
            RunOperation("Comparando cliente", delegate(Action<string> log, Action<int, string> report)
            {
                log("Comparando tamaños y SHA-256 con el host privado " + ClientDownload.Server.Host + ".");
                ClientDownload.Update(ClientDownload.Server, ClientDownload.CertificatePin, target, null, report);
                report(100, "Cliente actualizado.");
            }, delegate
            {
                SetClientReady("Cliente actualizado · listo para entrar");
                AppendLog("Actualización completa. Los archivos locales no incluidos en el manifiesto se han conservado.");
            });
        }

        private void SetClientReady(string message)
        {
            existingClientDetected = true;
            status.Text = message;
            homeStatus.Text = message;
            homeStatus.ForeColor = Color.FromArgb(122, 224, 149);
        }

        private static string FormatBytes(long bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0") + " GB";
        }
        private static string FindExistingClient()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates = new[]
            {
                Path.Combine(documents, "AzerotBatzoki-ClientDistribution"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerotBatzoki", "Client"),
                Path.Combine(Application.StartupPath, "Client"),
                Application.StartupPath,
                Path.Combine(documents, "World of Warcraft 3.3.5a"),
                Path.Combine(documents, "World of Warcraft"),
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

        private void RunOperation(string name, Action<Action<string>, Action<int, string>> operation, Action completed = null)
        {
            if (Interlocked.CompareExchange(ref operationRunning, 1, 0) != 0)
                return;
            foreach (Button button in actionButtons)
                button.Enabled = false;
            progress.Value = 0;
            status.Text = name + "...";
            homeStatus.Text = status.Text;
            homeStatus.ForeColor = Color.FromArgb(237, 190, 105);
            homeProgressPercent = 0;
            homeProgress.Invalidate();
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
                        homeStatus.Text = status.Text;
                        homeStatus.ForeColor = Color.FromArgb(122, 224, 149);
                        homeProgressPercent = 100;
                        homeProgress.Invalidate();
                        AppendLog(name + " completado.");
                        if (completed != null)
                            completed();
                    });
                }
                catch (Exception ex)
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        status.Text = "La operación ha fallado.";
                        homeStatus.Text = status.Text + " " + ex.Message;
                        homeStatus.ForeColor = Color.FromArgb(244, 135, 125);
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
            if (IsDisposed || !IsHandleCreated) return;
            lock (progressUpdateLock)
            {
                pendingProgressValue = Math.Max(0, Math.Min(100, value));
                pendingProgressMessage = message;
                if (progressUpdateQueued) return;
                progressUpdateQueued = true;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    int latestValue;
                    string latestMessage;
                    lock (progressUpdateLock)
                    {
                        latestValue = pendingProgressValue;
                        latestMessage = pendingProgressMessage;
                        progressUpdateQueued = false;
                    }
                    progress.Value = latestValue;
                    homeProgressPercent = latestValue;
                    if (homeProgress != null) homeProgress.Invalidate();
                    if (!String.IsNullOrEmpty(latestMessage))
                    {
                        status.Text = latestMessage;
                        if (homeStatus != null) homeStatus.Text = latestMessage;
                    }
                });
            }
            catch (InvalidOperationException)
            {
                lock (progressUpdateLock) progressUpdateQueued = false;
            }
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
                if (!Directory.Exists(target))
                    InstallClientFromServer();
                else
                    ShowWarning("La carpeta seleccionada no contiene Wow.exe. Elige otra ruta o una carpeta nueva para instalar.");
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
                ClientValidation.EnsureClientStopped(target);
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
                SetClientReady("WoW iniciado. Realmlist: " + realm);
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
                "Este PC servirá el cliente verificado desde Documentos a la red local por HTTPS (puerto 8788). " +
                "Mantén este equipo encendido mientras otros jugadores descargan. ¿Continuar?",
                "Activar host de AzerotBatzoki", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
                return;

            string distribution = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "AzerotBatzoki-ClientDistribution");
            string invalidSource = ClientValidation.GetInvalidClient(distribution);
            if (invalidSource != null)
            {
                ShowWarning("El host no encuentra un cliente completo en Documents\\AzerotBatzoki-ClientDistribution: " +
                    invalidSource + ".");
                return;
            }

            RunOperation("Activando host de descarga", delegate(Action<string> log, Action<int, string> report)
            {
                string fingerprint = LauncherServer.GetFingerprint();
                LauncherServer.EnsureClientServerRunning();
                BeginInvoke((MethodInvoker)delegate
                {
                    status.Text = "Host de cliente activo en 192.168.201.209:8788 · reino 192.168.201.213.";
                });
                log("Host HTTPS de cliente activo en " + ClientDownload.Server.Host + ":" + ClientDownload.Server.Port);
                log("Huella pública SHA-256: " + fingerprint);
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

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)FitToWorkingArea); }
            catch (InvalidOperationException) { }
        }

        private void FitToWorkingArea()
        {
            if (IsDisposed || WindowState != FormWindowState.Normal) return;
            Rectangle area = Screen.FromHandle(Handle).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            if (disposing && heroArtwork != null)
            {
                heroArtwork.Dispose();
                heroArtwork = null;
            }
            base.Dispose(disposing);
        }

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

}
