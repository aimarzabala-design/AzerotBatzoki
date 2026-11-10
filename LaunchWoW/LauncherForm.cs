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

        private readonly Color background = Color.FromArgb(7, 13, 23);
        private readonly Color panel = Color.FromArgb(18, 29, 43);
        private readonly Color muted = Color.FromArgb(151, 174, 193);
        private readonly Color foreground = Color.FromArgb(240, 231, 205);
        private readonly Color accent = Color.FromArgb(179, 132, 54);
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

        internal LauncherForm()
        {
            Text = "AzerotBatzoki | Azeroth";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 730);
            MinimumSize = new Size(1100, 730);
            MaximumSize = new Size(1100, 730);
            BackColor = background;
            ForeColor = foreground;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Panel header = new Panel();
            header.Location = new Point(0, 0);
            header.Size = new Size(880, 102);
            header.BackColor = Color.FromArgb(18, 31, 27);
            header.Paint += PaintBatzokiHeader;
            Controls.Add(header);

            Label title = new Label();
            title.Text = "AZEROTBATZOKI";
            title.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
            title.ForeColor = foreground;
            title.Location = new Point(30, 15);
            title.Size = new Size(420, 42);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "EL BATZOKI DE AZEROTH  •  WRATH OF THE LICH KING";
            subtitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            subtitle.ForeColor = muted;
            subtitle.Location = new Point(31, 63);
            subtitle.Size = new Size(460, 22);
            header.Controls.Add(subtitle);

            Label buildBadge = new Label();
            buildBadge.Text = "REINO PRIVADO  ·  ES";
            buildBadge.TextAlign = ContentAlignment.MiddleCenter;
            buildBadge.ForeColor = Color.FromArgb(205, 222, 243);
            buildBadge.BackColor = Color.FromArgb(37, 59, 47);
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
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzerotBatzoki", "Client"),
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
            Button host = MakeButton("ACTIVAR LAN", 0, 0, 174, 46, panel, foreground);
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
            settingsPage.Size = new Size(880, 735);
            settingsPage.Location = new Point(110, 0);
            settingsPage.BackColor = background;
            Control[] existing = Controls.Cast<Control>().ToArray();
            foreach (Control control in existing)
            {
                Controls.Remove(control);
                settingsPage.Controls.Add(control);
            }
            Controls.Add(settingsPage);
            settingsPage.Visible = false;

            Button back = MakeButton("VOLVER", 790, 7, 72, 25, Color.FromArgb(25, 38, 53), foreground);
            back.Click += delegate { settingsPage.Visible = false; };
            settingsPage.Controls.Add(back);
            back.BringToFront();

            Panel home = new Panel();
            home.Dock = DockStyle.Fill;
            home.BackColor = background;
            home.Paint += PaintBatzokiHome;
            Controls.Add(home);
            home.BringToFront();

            Panel brand = new Panel();
            brand.Location = new Point(24, 7);
            brand.Size = new Size(235, 100);
            brand.BackColor = Color.Transparent;
            brand.Paint += PaintBatzokiCrest;
            home.Controls.Add(brand);

            Label banner = new Label();
            banner.Text = "REINO PRIVADO  ·  3.3.5a  ·  ES";
            banner.Font = new Font("Georgia", 10F, FontStyle.Bold);
            banner.ForeColor = Color.FromArgb(226, 192, 116);
            banner.BackColor = Color.Transparent;
            banner.TextAlign = ContentAlignment.MiddleRight;
            banner.Location = new Point(783, 32);
            banner.Size = new Size(280, 28);
            home.Controls.Add(banner);

            Panel news = new Panel();
            news.Location = new Point(24, 111);
            news.Size = new Size(260, 463);
            news.BackColor = Color.FromArgb(12, 21, 34);
            news.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(169, 126, 54), 1))
                    e.Graphics.DrawRectangle(p, 1, 1, news.Width - 3, news.Height - 3);
                using (Pen p = new Pen(Color.FromArgb(66, 84, 101), 1))
                    e.Graphics.DrawRectangle(p, 5, 5, news.Width - 11, news.Height - 11);
            };
            home.Controls.Add(news);
            Label newsTitle = new Label();
            newsTitle.Text = "CRÓNICAS DEL BATZOKI";
            newsTitle.Font = new Font("Georgia", 13F, FontStyle.Bold);
            newsTitle.ForeColor = Color.FromArgb(244, 206, 117);
            newsTitle.Location = new Point(17, 18);
            newsTitle.Size = new Size(226, 28);
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
            news.Controls.Add(ikurrina);

            Panel hero = new Panel();
            hero.Location = new Point(298, 111);
            hero.Size = new Size(765, 463);
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
            home.Controls.Add(sceneCaption);

            homeStatus = new Label();
            homeStatus.Text = existingClientDetected ? "Cliente 3.3.5a detectado · listo para entrar" : "Cliente no detectado · instala o selecciona tu cliente";
            homeStatus.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            homeStatus.ForeColor = existingClientDetected ? Color.FromArgb(122, 224, 149) : Color.FromArgb(237, 190, 105);
            homeStatus.Location = new Point(302, 589);
            homeStatus.Size = new Size(550, 22);
            home.Controls.Add(homeStatus);

            homeProgress = new Panel();
            homeProgress.Location = new Point(302, 614);
            homeProgress.Size = new Size(760, 14);
            homeProgress.BackColor = Color.FromArgb(5, 9, 15);
            homeProgress.Paint += PaintHomeProgress;
            home.Controls.Add(homeProgress);

            CheckBox autoPlay = new CheckBox();
            autoPlay.Text = "Abrir el juego al terminar la instalación";
            autoPlay.Checked = true;
            autoPlay.ForeColor = foreground;
            autoPlay.BackColor = background;
            autoPlay.Location = new Point(28, 650);
            autoPlay.Size = new Size(330, 27);
            home.Controls.Add(autoPlay);
            autoPlay.CheckedChanged += delegate { autoPlayAfterInstall = autoPlay.Checked; };

            Button install = MakeButton("INSTALAR CLIENTE", 570, 643, 190, 52, Color.FromArgb(27, 51, 69), Color.FromArgb(242, 211, 143));
            install.FlatAppearance.BorderSize = 1;
            install.FlatAppearance.BorderColor = accent;
            install.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            AddButtonHover(install, Color.FromArgb(39, 71, 93), Color.FromArgb(228, 187, 94));
            install.Click += delegate { SelectClient(); };
            home.Controls.Add(install);
            Button play = MakeButton("JUGAR", 778, 638, 284, 61, Color.FromArgb(124, 37, 34), Color.FromArgb(255, 226, 150));
            play.FlatAppearance.BorderSize = 2;
            play.FlatAppearance.BorderColor = Color.FromArgb(218, 169, 73);
            play.Font = new Font("Georgia", 17F, FontStyle.Bold);
            AddButtonHover(play, Color.FromArgb(151, 52, 45), Color.FromArgb(255, 216, 134));
            play.Click += delegate { Play(); };
            home.Controls.Add(play);
            Button options = MakeButton("CUENTA · AJUSTES", 368, 650, 183, 35, Color.FromArgb(20, 33, 47), foreground);
            options.FlatAppearance.BorderSize = 1;
            options.FlatAppearance.BorderColor = Color.FromArgb(88, 108, 124);
            AddButtonHover(options, Color.FromArgb(31, 51, 68), Color.FromArgb(128, 158, 178));
            options.Click += delegate { settingsPage.Visible = true; settingsPage.BringToFront(); };
            home.Controls.Add(options);
        }

        private void PaintBatzokiCrest(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Point[] shield = { new Point(8, 6), new Point(226, 6), new Point(215, 67), new Point(117, 91), new Point(19, 67) };
            using (SolidBrush b = new SolidBrush(Color.FromArgb(245, 10, 18, 29))) g.FillPolygon(b, shield);
            using (Pen p = new Pen(Color.FromArgb(218, 174, 78), 2)) g.DrawPolygon(p, shield);
            using (Pen p = new Pen(Color.FromArgb(94, 117, 132), 1))
                g.DrawPolygon(p, new[] { new Point(15, 12), new Point(219, 12), new Point(209, 62), new Point(117, 83), new Point(25, 62) });
            using (Font f = new Font("Georgia", 17F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(249, 218, 139)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("AZEROT\nBATZOKI", f, b, new RectangleF(15, 10, 204, 48), sf);
            using (Font f = new Font("Segoe UI", 7.5F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(181, 203, 216)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("EL BATZOKI DE AZEROTH", f, b, new RectangleF(22, 62, 190, 14), sf);
        }

        private void PaintHomeProgress(object sender, PaintEventArgs e)
        {
            Panel bar = (Panel)sender;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen p = new Pen(Color.FromArgb(157, 119, 54), 1)) e.Graphics.DrawRectangle(p, 0, 0, bar.Width - 1, bar.Height - 1);
            int width = Math.Max(0, (bar.Width - 4) * homeProgressPercent / 100);
            if (width > 0)
            {
                using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(2, 2, Math.Max(1, width), bar.Height - 4), Color.FromArgb(218, 177, 80), Color.FromArgb(116, 146, 172), 0F))
                    e.Graphics.FillRectangle(b, 2, 2, width, bar.Height - 4);
            }
            using (Font f = new Font("Segoe UI", 7F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(218, 222, 222)))
                e.Graphics.DrawString(homeProgressPercent + "%", f, b, bar.Width - 38, -1);
        }

        private void PaintBatzokiHome(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, 1100, 730), Color.FromArgb(9, 17, 29), Color.FromArgb(24, 34, 44), 90F))
                g.FillRectangle(b, 0, 0, 1100, 730);
            using (Pen p = new Pen(Color.FromArgb(174, 130, 56), 1))
                g.DrawRectangle(p, 9, 9, 1081, 711);
            using (Pen p = new Pen(Color.FromArgb(54, 75, 91), 1))
                g.DrawRectangle(p, 14, 14, 1071, 701);
            using (Pen p = new Pen(Color.FromArgb(129, 99, 49), 1))
            {
                g.DrawLine(p, 25, 104, 1074, 104);
                g.DrawLine(p, 25, 108, 1074, 108);
            }
            using (SolidBrush b = new SolidBrush(Color.FromArgb(183, 39, 38)))
                g.FillRectangle(b, 1061, 23, 6, 26);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(42, 117, 71)))
                g.FillRectangle(b, 1068, 23, 6, 26);
        }

        private void PaintBatzokiScene(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = ((Control)sender).Width, h = ((Control)sender).Height;
            Image suppliedArtwork = LoadHeroArtwork();
            if (suppliedArtwork != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(suppliedArtwork, new Rectangle(0, 0, w, h));
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
            using (Pen gold = new Pen(Color.FromArgb(181, 135, 57), 2))
            using (Pen dim = new Pen(Color.FromArgb(86, 105, 116), 1))
            {
                g.DrawRectangle(gold, 1, 1, w - 3, h - 3);
                g.DrawRectangle(dim, 5, 5, w - 11, h - 11);
                int[][] corners = { new[] { 9, 9, 1, 1 }, new[] { w - 10, 9, -1, 1 }, new[] { 9, h - 10, 1, -1 }, new[] { w - 10, h - 10, -1, -1 } };
                foreach (int[] c in corners)
                {
                    g.DrawLine(gold, c[0], c[1], c[0] + 24 * c[2], c[1]);
                    g.DrawLine(gold, c[0], c[1], c[0], c[1] + 24 * c[3]);
                    g.DrawLine(dim, c[0] + 5 * c[2], c[1] + 5 * c[3], c[0] + 16 * c[2], c[1] + 5 * c[3]);
                    g.DrawLine(dim, c[0] + 5 * c[2], c[1] + 5 * c[3], c[0] + 5 * c[2], c[1] + 16 * c[3]);
                }
            }
        }

        private void PaintBatzokiHeader(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            int width = ((Control)sender).Width;
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(30, 8, 15, 13)))
                graphics.FillPolygon(shadow, new[] { new Point(width - 360, 102), new Point(width - 255, 26),
                    new Point(width - 190, 72), new Point(width - 112, 8), new Point(width - 12, 102) });
            using (SolidBrush mountain = new SolidBrush(Color.FromArgb(255, 34, 57, 44)))
                graphics.FillPolygon(mountain, new[] { new Point(width - 310, 102), new Point(width - 226, 50),
                    new Point(width - 163, 88), new Point(width - 102, 38), new Point(width - 30, 102) });
            using (SolidBrush sun = new SolidBrush(Color.FromArgb(90, 205, 171, 105)))
                graphics.FillEllipse(sun, width - 174, 14, 50, 50);
            using (SolidBrush green = new SolidBrush(Color.FromArgb(28, 101, 65)))
                graphics.FillRectangle(green, 0, 98, width, 2);
            using (SolidBrush red = new SolidBrush(Color.FromArgb(165, 40, 47)))
                graphics.FillRectangle(red, 0, 100, width, 2);
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

        private static void AddButtonHover(Button button, Color hoverBack, Color hoverBorder)
        {
            Color normalBorder = button.FlatAppearance.BorderColor;
            button.FlatAppearance.MouseOverBackColor = hoverBack;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, hoverBack);
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
                status.Text = "Cliente seleccionado. Realmlist configurada al pulsar JUGAR.";
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
                BeginInvoke((MethodInvoker)delegate
                {
                    targetBox.Text = target;
                    log("Cliente instalado, verificado y configurado para " + realm + ".");
                    if (autoPlayAfterInstall)
                        Play();
                    else
                        status.Text = "Cliente instalado. Pulsa JUGAR cuando quieras entrar.";
                });
                report(100, "Cliente listo. Iniciando el juego...");
            });
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
                    if (homeProgress != null)
                    {
                        homeProgressPercent = Math.Max(0, Math.Min(100, value));
                        homeProgress.Invalidate();
                    }
                    if (!String.IsNullOrEmpty(message))
                    {
                        status.Text = message;
                        if (homeStatus != null)
                            homeStatus.Text = message;
                    }
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

        private void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "AzerothCore Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

}
