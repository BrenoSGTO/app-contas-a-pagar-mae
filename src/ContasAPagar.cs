using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Contas a Pagar - Planilha Inteligente")]
[assembly: AssemblyDescription("Controle de Contas Recorrentes e Parceladas")]
[assembly: AssemblyCompany("Contas da Mãe")]
[assembly: AssemblyProduct("Contas a Pagar")]
[assembly: AssemblyCopyright("Copyright © 2026")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace ContasAPagar
{
    // =========================================================================
    // DATA MODELS
    // =========================================================================
    [DataContract]
    public class BillDefinition
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Category { get; set; }
        [DataMember] public decimal Amount { get; set; }
        [DataMember] public int DueDay { get; set; }
        [DataMember] public int StartMonth { get; set; } // 1..12
        [DataMember] public int StartYear { get; set; }  // e.g. 2026
        [DataMember] public int RecurrenceCount { get; set; } // 0 = Fixa/Contínua (todo mês), >0 = Quantidade de meses/parcelas
        [DataMember] public string Notes { get; set; }
        [DataMember] public bool Active { get; set; }

        public BillDefinition()
        {
            Id = Guid.NewGuid().ToString("N");
            Category = "Casa";
            DueDay = 10;
            StartMonth = DateTime.Now.Month;
            StartYear = DateTime.Now.Year;
            RecurrenceCount = 12;
            Notes = "";
            Active = true;
        }

        public int StartMonthIndex
        {
            get { return StartYear * 12 + (StartMonth - 1); }
        }

        public bool AppliesToMonth(int year, int month, out int installmentNumber)
        {
            installmentNumber = 0;
            if (!Active) return false;
            int targetIdx = year * 12 + (month - 1);
            int diff = targetIdx - StartMonthIndex;
            if (diff < 0) return false;
            if (RecurrenceCount > 0 && diff >= RecurrenceCount) return false;
            installmentNumber = diff + 1;
            return true;
        }

        public DateTime GetDueDateForMonth(int year, int month)
        {
            int maxDays = DateTime.DaysInMonth(year, month);
            int day = Math.Min(Math.Max(1, DueDay), maxDays);
            return new DateTime(year, month, day);
        }
    }

    [DataContract]
    public class MonthlyPaymentRecord
    {
        [DataMember] public string BillId { get; set; }
        [DataMember] public int Year { get; set; }
        [DataMember] public int Month { get; set; }
        [DataMember] public bool IsPaid { get; set; }
        [DataMember] public string PaidDateStr { get; set; } // "dd/MM/yyyy"
        [DataMember] public decimal CustomAmount { get; set; } // 0 = use BillDefinition.Amount
        [DataMember] public string CustomNotes { get; set; }
    }

    [DataContract]
    public class AppDatabase
    {
        [DataMember] public List<BillDefinition> Bills { get; set; }
        [DataMember] public List<MonthlyPaymentRecord> Payments { get; set; }

        public AppDatabase()
        {
            Bills = new List<BillDefinition>();
            Payments = new List<MonthlyPaymentRecord>();
        }
    }

    // =========================================================================
    // STORAGE SERVICE
    // =========================================================================
    public static class StorageService
    {
        public static readonly string AppVersion = "1.2.0";
        public static readonly string GitHubRepo = "BrenoSGTO/app-contas-a-pagar-mae";

        public static string GetDataFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "ContasAPagarMae");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetDatabasePath()
        {
            return Path.Combine(GetDataFolder(), "contas_dados.json");
        }

        public static string GetBackupFolder()
        {
            string dir = Path.Combine(GetDataFolder(), "Backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static AppDatabase Load()
        {
            string path = GetDatabasePath();
            if (!File.Exists(path))
            {
                var db = new AppDatabase();
                Save(db);
                return db;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppDatabase));
                    var loaded = (AppDatabase)ser.ReadObject(ms);
                    if (loaded == null) return new AppDatabase();
                    if (loaded.Bills == null) loaded.Bills = new List<BillDefinition>();
                    if (loaded.Payments == null) loaded.Payments = new List<MonthlyPaymentRecord>();
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Aviso ao ler banco de dados: " + ex.Message, "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new AppDatabase();
            }
        }

        public static void Save(AppDatabase db)
        {
            try
            {
                string path = GetDatabasePath();
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(AppDatabase));
                    ser.WriteObject(ms, db);
                    File.WriteAllBytes(path, ms.ToArray());
                }

                // Daily automatic backup
                string backupFile = Path.Combine(GetBackupFolder(), "contas_backup_" + DateTime.Now.ToString("yyyy-MM-dd") + ".json");
                if (!File.Exists(backupFile))
                {
                    File.Copy(path, backupFile, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao salvar dados: " + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // =========================================================================
    // ICON GENERATOR (Generates crisp icon dynamically for Windows Forms & Shortcuts)
    // =========================================================================
    public static class AppIconBuilder
    {
        public static Icon CreateAppIcon()
        {
            using (Bitmap bmp = CreateIconBitmap(64))
            {
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static Bitmap CreateIconBitmap(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                float s = size / 64f;

                // Deep Slate rounded rect background
                using (GraphicsPath path = RoundedRect(new Rectangle((int)(3 * s), (int)(3 * s), (int)(size - 6 * s), (int)(size - 6 * s)), Math.Max(2, (int)(8 * s))))
                using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, size, size),
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(30, 41, 59),
                    45f))
                {
                    g.FillPath(bgBrush, path);
                }

                // Emerald top bar
                using (GraphicsPath barPath = RoundedRect(new Rectangle((int)(4 * s), (int)(4 * s), (int)(size - 8 * s), Math.Max(4, (int)(14 * s))), Math.Max(2, (int)(4 * s))))
                using (LinearGradientBrush accentBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, size, Math.Max(4, (int)(16 * s))),
                    Color.FromArgb(16, 185, 129),
                    Color.FromArgb(5, 150, 105),
                    0f))
                {
                    g.FillPath(accentBrush, barPath);
                }

                // Grid subtle lines
                using (SolidBrush gridBrush = new SolidBrush(Color.FromArgb(71, 85, 105)))
                {
                    g.FillRectangle(gridBrush, 12f * s, 26f * s, 16f * s, Math.Max(2f, 3.5f * s));
                    g.FillRectangle(gridBrush, 12f * s, 34f * s, 13f * s, Math.Max(2f, 3.5f * s));
                    g.FillRectangle(gridBrush, 12f * s, 42f * s, 11f * s, Math.Max(2f, 3.5f * s));
                }

                // Emerald check badge
                using (SolidBrush badgeBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    g.FillEllipse(badgeBrush, 28f * s, 26f * s, 26f * s, 26f * s);
                }

                using (Pen pen = new Pen(Color.White, Math.Max(2f, 3.8f * s)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    PointF[] pts = new PointF[]
                    {
                        new PointF(34f * s, 39f * s),
                        new PointF(39f * s, 44f * s),
                        new PointF(48f * s, 34f * s)
                    };
                    g.DrawLines(pen, pts);
                }
            }
            return bmp;
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(bounds.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // =========================================================================
    // INSTALLER MODE (When launched as instalador.exe or with --install)
    // =========================================================================
    public class InstallerForm : Form
    {
        private Label lblStatus;
        private ProgressBar progressBar;
        private Button btnInstall;
        private Button btnClose;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkStartMenuShortcut;
        private CheckBox chkLaunchAfter;

        public InstallerForm()
        {
            Text = "Instalador - Contas a Pagar da Mãe (v" + StorageService.AppVersion + ")";
            Size = new Size(540, 390);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 10F);
            Icon = AppIconBuilder.CreateAppIcon();

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            header.Paint += (s, e) =>
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    header.ClientRectangle,
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(30, 41, 59),
                    0f))
                {
                    e.Graphics.FillRectangle(br, header.ClientRectangle);
                }
                using (SolidBrush lineBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillRectangle(lineBrush, 0, header.Height - 3, header.Width, 3);
                }
            };

            PictureBox pic = new PictureBox
            {
                Image = AppIconBuilder.CreateIconBitmap(56),
                Size = new Size(56, 56),
                Location = new Point(22, 17),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Transparent
            };
            header.Controls.Add(pic);

            Label lblTitle = new Label
            {
                Text = "Contas a Pagar - Planilha Fácil",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(92, 18),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblTitle);

            Label lblSub = new Label
            {
                Text = "Instalador oficial para o computador (Versão " + StorageService.AppVersion + ")",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5F),
                AutoSize = true,
                Location = new Point(94, 50),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblSub);
            Controls.Add(header);

            Label lblDesc = new Label
            {
                Text = "Este assistente vai instalar o aplicativo Contas a Pagar no seu computador e criar um atalho na sua Área de Trabalho para facilitar o acesso diário.",
                Location = new Point(24, 108),
                Size = new Size(480, 48),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 10F)
            };
            Controls.Add(lblDesc);

            chkDesktopShortcut = new CheckBox
            {
                Text = "Criar atalho na Área de Trabalho (Recomendado)",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 165),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            Controls.Add(chkDesktopShortcut);

            chkStartMenuShortcut = new CheckBox
            {
                Text = "Criar atalho no Menu Iniciar do Windows",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 195),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            Controls.Add(chkStartMenuShortcut);

            chkLaunchAfter = new CheckBox
            {
                Text = "Abrir o programa automaticamente ao concluir",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 225),
                ForeColor = Color.FromArgb(30, 41, 59)
            };
            Controls.Add(chkLaunchAfter);

            lblStatus = new Label
            {
                Text = "Pronto para instalar.",
                Location = new Point(24, 262),
                Size = new Size(480, 22),
                ForeColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 9.5F)
            };
            Controls.Add(lblStatus);

            progressBar = new ProgressBar
            {
                Location = new Point(24, 286),
                Size = new Size(476, 12),
                Value = 0
            };
            Controls.Add(progressBar);

            btnInstall = new Button
            {
                Text = "Instalar Agora",
                Size = new Size(170, 40),
                Location = new Point(195, 308),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Click += BtnInstall_Click;
            Controls.Add(btnInstall);

            btnClose = new Button
            {
                Text = "Cancelar",
                Size = new Size(120, 40),
                Location = new Point(380, 308),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        private async void BtnInstall_Click(object sender, EventArgs e)
        {
            btnInstall.Enabled = false;
            chkDesktopShortcut.Enabled = false;
            chkStartMenuShortcut.Enabled = false;
            chkLaunchAfter.Enabled = false;

            try
            {
                lblStatus.Text = "Preparando pasta de instalação...";
                progressBar.Value = 20;
                await Task.Delay(250);

                string installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "ContasAPagarMae");

                if (!Directory.Exists(installDir))
                    Directory.CreateDirectory(installDir);

                string currentExe = Application.ExecutablePath;
                string targetExe = Path.Combine(installDir, "ContasAPagar.exe");

                lblStatus.Text = "Copiando arquivos do aplicativo...";
                progressBar.Value = 50;
                await Task.Delay(250);

                if (!string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase))
                {
                    // Fechar qualquer instância de ContasAPagar em execução para não travar o arquivo
                    try
                    {
                        int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ContasAPagar"))
                        {
                            if (p.Id != currentPid)
                            {
                                try
                                {
                                    p.CloseMainWindow();
                                    if (!p.WaitForExit(1500))
                                    {
                                        p.Kill();
                                        p.WaitForExit(1000);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    // Remover atributo ReadOnly caso exista
                    if (File.Exists(targetExe))
                    {
                        try { File.SetAttributes(targetExe, FileAttributes.Normal); } catch { }
                    }

                    // Cópia resiliente com fallback de renomeação caso o Windows mantenha o arquivo em uso
                    bool copied = false;
                    for (int attempt = 0; attempt < 3 && !copied; attempt++)
                    {
                        try
                        {
                            File.Copy(currentExe, targetExe, true);
                            copied = true;
                        }
                        catch (Exception)
                        {
                            if (attempt < 2)
                            {
                                try
                                {
                                    string tempOld = targetExe + ".old." + Guid.NewGuid().ToString("N");
                                    File.Move(targetExe, tempOld);
                                    File.Copy(currentExe, targetExe, true);
                                    copied = true;
                                    try { File.Delete(tempOld); } catch { }
                                }
                                catch
                                {
                                    System.Threading.Thread.Sleep(500);
                                }
                            }
                            else
                            {
                                throw;
                            }
                        }
                    }
                }

                // Clear Mark-of-the-Web (Zone.Identifier) on the installed exe so Windows never prompts again
                try
                {
                    DeleteFileW(targetExe + ":Zone.Identifier");
                    DeleteFileW(currentExe + ":Zone.Identifier");
                }
                catch { }

                // Save an .ico file in installDir for shortcut icon if needed
                if (chkDesktopShortcut.Checked)
                {
                    lblStatus.Text = "Criando atalho na Área de Trabalho...";
                    progressBar.Value = 75;
                    await Task.Delay(200);
                    string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    CreateShortcut(Path.Combine(desktopDir, "Contas a Pagar.lnk"), targetExe, installDir, "Controle de Contas Recorrentes");
                }

                if (chkStartMenuShortcut.Checked)
                {
                    lblStatus.Text = "Criando atalho no Menu Iniciar...";
                    progressBar.Value = 90;
                    await Task.Delay(150);
                    string startMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                    CreateShortcut(Path.Combine(startMenuDir, "Contas a Pagar.lnk"), targetExe, installDir, "Controle de Contas Recorrentes");
                }

                progressBar.Value = 100;
                lblStatus.Text = "Instalação concluída com sucesso!";
                lblStatus.ForeColor = Color.FromArgb(16, 185, 129);

                MessageBox.Show(
                    "O aplicativo 'Contas a Pagar' foi instalado com sucesso!\n\n" +
                    "Um atalho foi colocado na sua Área de Trabalho para você abrir sempre que quiser.",
                    "Instalação Concluída",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                if (chkLaunchAfter.Checked)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetExe,
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    });
                }

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro durante a instalação:\n" + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = true;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DeleteFileW(string lpFileName);

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        public static void CreateShortcut(string shortcutPath, string targetExe, string workingDir, string description)
        {
            try
            {
                IShellLinkW link = (IShellLinkW)new ShellLink();
                link.SetPath(targetExe);
                link.SetWorkingDirectory(workingDir);
                link.SetDescription(description);
                link.SetIconLocation(targetExe, 0);
                System.Runtime.InteropServices.ComTypes.IPersistFile file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link;
                file.Save(shortcutPath, false);
            }
            catch
            {
                // Ignore shortcut error if restricted environment
            }
        }
    }

    // =========================================================================
    // GITHUB AUTO-UPDATER SERVICE
    // =========================================================================
    public static class AutoUpdater
    {
        public class ReleaseInfo
        {
            public string TagName;
            public string VersionString;
            public string DownloadUrl;
            public string AssetName;
            public string ReleaseNotes;
            public string HtmlUrl;
        }

        public static async Task CheckAndPromptUpdate(Form parentForm, bool silentIfUpToDate = false)
        {
            Cursor oldCursor = parentForm.Cursor;
            try
            {
                parentForm.Cursor = Cursors.WaitCursor;
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                ReleaseInfo latest = await Task.Run(() => FetchLatestRelease());
                parentForm.Cursor = oldCursor;

                if (latest == null || string.IsNullOrEmpty(latest.VersionString))
                {
                    if (!silentIfUpToDate)
                    {
                        MessageBox.Show(
                            parentForm,
                            "Não foi possível verificar atualizações no momento ou ainda não há versões publicadas no GitHub.",
                            "Verificar Atualizações",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                bool isNewer = IsNewerVersion(StorageService.AppVersion, latest.VersionString);
                if (!isNewer)
                {
                    if (!silentIfUpToDate)
                    {
                        MessageBox.Show(
                            parentForm,
                            "Seu programa já está na versão mais recente! (v" + StorageService.AppVersion + ")\n\n" +
                            "Repositório GitHub: " + StorageService.GitHubRepo,
                            "Programa Atualizado ✓",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                string msg = string.Format(
                    "Uma nova atualização foi encontrada no GitHub!\n\n" +
                    "Versão atual: v{0}\n" +
                    "Nova versão: v{1}\n\n" +
                    "{2}\n\n" +
                    "Deseja baixar e instalar a atualização automaticamente agora?\n" +
                    "(Suas contas cadastradas serão mantidas com total segurança!)",
                    StorageService.AppVersion,
                    latest.VersionString,
                    string.IsNullOrWhiteSpace(latest.ReleaseNotes) ? "Melhorias e correções de bugs." : latest.ReleaseNotes);

                var res = MessageBox.Show(parentForm, msg, "Nova Atualização Disponível!", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (res == DialogResult.Yes)
                {
                    await DownloadAndApplyUpdate(parentForm, latest);
                }
            }
            catch (Exception ex)
            {
                parentForm.Cursor = oldCursor;
                if (!silentIfUpToDate)
                {
                    MessageBox.Show(
                        parentForm,
                        "Erro ao consultar atualizações no GitHub:\n" + ex.Message,
                        "Verificar Atualizações",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private static ReleaseInfo FetchLatestRelease()
        {
            string apiUrl = "https://api.github.com/repos/" + StorageService.GitHubRepo + "/releases/latest";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(apiUrl);
            req.UserAgent = "ContasAPagarMae-AutoUpdater/" + StorageService.AppVersion;
            req.Accept = "application/vnd.github.v3+json";
            req.Timeout = 10000;

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                string tag = ExtractJsonValue(json, "tag_name");
                string body = ExtractJsonValue(json, "body");
                string htmlUrl = ExtractJsonValue(json, "html_url");

                // Find browser_download_url for .exe or .zip
                var matches = Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                string bestUrl = null;
                string bestName = null;

                foreach (Match m in matches)
                {
                    string url = m.Groups[1].Value;
                    if (url.EndsWith("ContasAPagar.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestUrl = url;
                        bestName = Path.GetFileName(new Uri(url).LocalPath);
                        break;
                    }
                    if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && bestUrl == null)
                    {
                        bestUrl = url;
                        bestName = Path.GetFileName(new Uri(url).LocalPath);
                    }
                }

                string cleanVer = (tag ?? "").Trim().TrimStart('v', 'V');
                return new ReleaseInfo
                {
                    TagName = tag,
                    VersionString = cleanVer,
                    DownloadUrl = bestUrl,
                    AssetName = bestName,
                    ReleaseNotes = UnescapeJson(body),
                    HtmlUrl = htmlUrl
                };
            }
        }

        private static async Task DownloadAndApplyUpdate(Form parentForm, ReleaseInfo release)
        {
            if (string.IsNullOrEmpty(release.DownloadUrl))
            {
                if (!string.IsNullOrEmpty(release.HtmlUrl))
                {
                    System.Diagnostics.Process.Start(release.HtmlUrl);
                }
                return;
            }

            Form progressModal = new Form
            {
                Text = "Baixando Atualização v" + release.VersionString + "...",
                Size = new Size(420, 150),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false,
                Font = new Font("Segoe UI", 10F)
            };

            Label lbl = new Label
            {
                Text = "Baixando nova versão do GitHub, aguarde...",
                Location = new Point(20, 22),
                AutoSize = true
            };
            ProgressBar pb = new ProgressBar
            {
                Location = new Point(20, 56),
                Size = new Size(365, 22),
                Style = ProgressBarStyle.Marquee
            };
            progressModal.Controls.Add(lbl);
            progressModal.Controls.Add(pb);
            progressModal.Show(parentForm);

            try
            {
                string currentExe = Application.ExecutablePath;
                string appDir = Path.GetDirectoryName(currentExe) ?? StorageService.GetDataFolder();
                string workDir = Path.Combine(StorageService.GetDataFolder(), "UpdateStaging");
                if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
                Directory.CreateDirectory(workDir);

                string downloadedFile = Path.Combine(workDir, release.AssetName ?? "update_pkg.exe");

                await Task.Run(() =>
                {
                    using (WebClient wc = new WebClient())
                    {
                        wc.Headers.Add("User-Agent", "ContasAPagarMae-AutoUpdater");
                        wc.DownloadFile(release.DownloadUrl, downloadedFile);
                    }
                });

                string newExePath = downloadedFile;
                if (downloadedFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    string extractDir = Path.Combine(workDir, "extracted");
                    ZipFile.ExtractToDirectory(downloadedFile, extractDir);
                    string[] exes = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
                    if (exes.Length == 0)
                        throw new Exception("Nenhum arquivo .exe encontrado no pacote de atualização.");
                    newExePath = exes[0];
                }

                // Native Windows in-place rename (Windows allows renaming a running .exe and placing the new one in its place without any .bat script!)
                string backupExe = currentExe + ".old";
                if (File.Exists(backupExe))
                {
                    try { File.Delete(backupExe); } catch { }
                }

                File.Move(currentExe, backupExe);
                File.Copy(newExePath, currentExe, true);
                try { InstallerForm.DeleteFileW(currentExe + ":Zone.Identifier"); } catch { }

                progressModal.Close();

                MessageBox.Show(
                    parentForm,
                    "Download concluído! O aplicativo será reiniciado agora na nova versão v" + release.VersionString + ".",
                    "Atualização Concluída",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = currentExe,
                    WorkingDirectory = appDir,
                    UseShellExecute = true
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                progressModal.Close();
                MessageBox.Show(
                    parentForm,
                    "Não foi possível aplicar a atualização automática:\n" + ex.Message,
                    "Erro na Atualização",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        public static bool IsNewerVersion(string currentVer, string remoteVer)
        {
            try
            {
                Version v1 = new Version(NormalizeVer(currentVer));
                Version v2 = new Version(NormalizeVer(remoteVer));
                return v2 > v1;
            }
            catch
            {
                return !string.Equals(currentVer, remoteVer, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeVer(string v)
        {
            string clean = Regex.Replace(v ?? "1.0.0", @"[^0-9\.]", "");
            string[] parts = clean.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0] + ".0.0";
            if (parts.Length == 2) return parts[0] + "." + parts[1] + ".0";
            return string.Join(".", parts.Take(4));
        }

        private static string ExtractJsonValue(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    // =========================================================================
    // DIALOG: NOVA / EDITAR CONTA RECORRENTE
    // =========================================================================
    public class BillDialog : Form
    {
        public BillDefinition ResultBill { get; private set; }

        private TextBox txtName;
        private ComboBox cmbCategory;
        private NumericUpDown numAmount;
        private NumericUpDown numDueDay;
        private ComboBox cmbStartMonth;
        private NumericUpDown numStartYear;
        private RadioButton rbFixedCount;
        private RadioButton rbContinuous;
        private NumericUpDown numRecurrenceCount;
        private TextBox txtNotes;
        private Label lblPreviewSchedule;

        private static readonly string[] MonthNames = new[]
        {
            "01 - Janeiro", "02 - Fevereiro", "03 - Março", "04 - Abril",
            "05 - Maio", "06 - Junho", "07 - Julho", "08 - Agosto",
            "09 - Setembro", "10 - Outubro", "11 - Novembro", "12 - Dezembro"
        };

        public BillDialog(BillDefinition existing, int defaultMonth, int defaultYear)
        {
            bool isEdit = existing != null;
            ResultBill = existing != null ? CloneBill(existing) : new BillDefinition
            {
                StartMonth = defaultMonth,
                StartYear = defaultYear,
                RecurrenceCount = 12,
                DueDay = 10
            };

            Text = isEdit ? "Editar Conta Recorrente" : "Nova Conta Recorrente / Parcelada";
            Size = new Size(560, 610);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10F);
            Icon = AppIconBuilder.CreateAppIcon();

            // Header Banner
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Color.FromArgb(15, 23, 42)
            };
            header.Paint += (s, e) =>
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    header.ClientRectangle,
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(30, 41, 59),
                    0f))
                {
                    e.Graphics.FillRectangle(br, header.ClientRectangle);
                }
                using (SolidBrush lineBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillRectangle(lineBrush, 0, header.Height - 3, header.Width, 3);
                }
            };
            Label lblHeader = new Label
            {
                Text = isEdit ? "Editar Conta" : "Cadastrar Nova Conta Recorrente",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(22, 19),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblHeader);
            Controls.Add(header);

            int y = 84;
            int leftCol = 24;
            int rightCol = 290;

            // Nome da Conta
            Controls.Add(MakeLabel("Nome / Descrição da Conta *", leftCol, y));
            y += 24;
            txtName = new TextBox
            {
                Location = new Point(leftCol, y),
                Size = new Size(495, 30),
                Font = new Font("Segoe UI", 11F),
                Text = ResultBill.Name ?? ""
            };
            Controls.Add(txtName);
            y += 42;

            // Categoria e Valor
            Controls.Add(MakeLabel("Categoria", leftCol, y));
            Controls.Add(MakeLabel("Valor Mensal (R$) *", rightCol, y));
            y += 24;

            cmbCategory = new ComboBox
            {
                Location = new Point(leftCol, y),
                Size = new Size(240, 30),
                Font = new Font("Segoe UI", 10.5F),
                DropDownStyle = ComboBoxStyle.DropDown
            };
            cmbCategory.Items.AddRange(new object[] {
                "Casa / Moradia", "Luz / Água / Gás", "Internet / Telefone",
                "Cartão de Crédito", "Saúde / Farmácia", "Mercado",
                "Impostos / Taxas", "Empréstimo / Parcelamento", "Outros"
            });
            cmbCategory.Text = string.IsNullOrWhiteSpace(ResultBill.Category) ? "Casa / Moradia" : ResultBill.Category;
            Controls.Add(cmbCategory);

            numAmount = new NumericUpDown
            {
                Location = new Point(rightCol, y),
                Size = new Size(228, 30),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                DecimalPlaces = 2,
                Maximum = 9999999,
                Minimum = 0,
                Value = ResultBill.Amount,
                ThousandsSeparator = true
            };
            Controls.Add(numAmount);
            y += 44;

            // Dia do Vencimento & Mês/Ano do Primeiro Pagamento
            Controls.Add(MakeLabel("Dia do Vencimento (1 a 31) *", leftCol, y));
            Controls.Add(MakeLabel("Mês e Ano do 1º Pagamento *", rightCol, y));
            y += 24;

            numDueDay = new NumericUpDown
            {
                Location = new Point(leftCol, y),
                Size = new Size(120, 30),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Minimum = 1,
                Maximum = 31,
                Value = Math.Min(31, Math.Max(1, ResultBill.DueDay))
            };
            numDueDay.ValueChanged += (s, e) => UpdatePreview();
            Controls.Add(numDueDay);

            Label lblDiaHint = new Label
            {
                Text = "todo mês",
                Location = new Point(leftCol + 128, y + 5),
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            Controls.Add(lblDiaHint);

            cmbStartMonth = new ComboBox
            {
                Location = new Point(rightCol, y),
                Size = new Size(140, 30),
                Font = new Font("Segoe UI", 10.5F),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbStartMonth.Items.AddRange(MonthNames);
            cmbStartMonth.SelectedIndex = Math.Min(11, Math.Max(0, ResultBill.StartMonth - 1));
            cmbStartMonth.SelectedIndexChanged += (s, e) => UpdatePreview();
            Controls.Add(cmbStartMonth);

            numStartYear = new NumericUpDown
            {
                Location = new Point(rightCol + 148, y),
                Size = new Size(80, 30),
                Font = new Font("Segoe UI", 10.5F),
                Minimum = 2020,
                Maximum = 2100,
                Value = Math.Min(2100, Math.Max(2020, ResultBill.StartYear))
            };
            numStartYear.ValueChanged += (s, e) => UpdatePreview();
            Controls.Add(numStartYear);
            y += 46;

            // Quantidade de Recorrências Group
            GroupBox grpRecurrence = new GroupBox
            {
                Text = "Quantidade de Recorrências (Meses / Parcelas)",
                Location = new Point(leftCol, y),
                Size = new Size(495, 118),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 41, 59)
            };

            rbFixedCount = new RadioButton
            {
                Text = "Definir quantidade de meses (recorrências):",
                Location = new Point(16, 28),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                Checked = ResultBill.RecurrenceCount > 0
            };
            rbFixedCount.CheckedChanged += (s, e) =>
            {
                numRecurrenceCount.Enabled = rbFixedCount.Checked;
                UpdatePreview();
            };
            grpRecurrence.Controls.Add(rbFixedCount);

            numRecurrenceCount = new NumericUpDown
            {
                Location = new Point(310, 25),
                Size = new Size(85, 28),
                Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold),
                Minimum = 1,
                Maximum = 240,
                Value = ResultBill.RecurrenceCount > 0 ? ResultBill.RecurrenceCount : 12,
                Enabled = ResultBill.RecurrenceCount > 0
            };
            numRecurrenceCount.ValueChanged += (s, e) => UpdatePreview();
            grpRecurrence.Controls.Add(numRecurrenceCount);

            Label lblMeses = new Label
            {
                Text = "meses",
                Location = new Point(402, 29),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(100, 116, 139)
            };
            grpRecurrence.Controls.Add(lblMeses);

            rbContinuous = new RadioButton
            {
                Text = "Conta fixa contínua (repete todos os meses sem data final)",
                Location = new Point(16, 62),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F),
                Checked = ResultBill.RecurrenceCount == 0
            };
            rbContinuous.CheckedChanged += (s, e) => UpdatePreview();
            grpRecurrence.Controls.Add(rbContinuous);

            lblPreviewSchedule = new Label
            {
                Location = new Point(16, 90),
                Size = new Size(465, 22),
                Font = new Font("Segoe UI", 9F, FontStyle.Italic),
                ForeColor = Color.FromArgb(5, 150, 105)
            };
            grpRecurrence.Controls.Add(lblPreviewSchedule);

            Controls.Add(grpRecurrence);
            y += 130;

            // Observações
            Controls.Add(MakeLabel("Observações (opcional - ex: código PIX, débito automático, etc.)", leftCol, y));
            y += 24;
            txtNotes = new TextBox
            {
                Location = new Point(leftCol, y),
                Size = new Size(495, 28),
                Font = new Font("Segoe UI", 10F),
                Text = ResultBill.Notes ?? ""
            };
            Controls.Add(txtNotes);
            y += 45;

            // Buttons
            Button btnSave = new Button
            {
                Text = "✔  Salvar Conta",
                Location = new Point(205, y),
                Size = new Size(185, 42),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            Button btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(400, y),
                Size = new Size(118, 42),
                BackColor = Color.FromArgb(226, 232, 240),
                ForeColor = Color.FromArgb(51, 65, 85),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F),
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            int startM = cmbStartMonth.SelectedIndex + 1;
            int startY = (int)numStartYear.Value;
            int day = (int)numDueDay.Value;
            int firstMax = DateTime.DaysInMonth(startY, startM);
            DateTime firstDate = new DateTime(startY, startM, Math.Min(day, firstMax));

            if (rbContinuous.Checked)
            {
                lblPreviewSchedule.Text = string.Format(
                    "• 1º pagamento em {0:dd/MM/yyyy} • Repetirá todo dia {1:00} de cada mês.",
                    firstDate, day);
            }
            else
            {
                int count = (int)numRecurrenceCount.Value;
                DateTime lastMonth = new DateTime(startY, startM, 1).AddMonths(count - 1);
                int lastMax = DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month);
                DateTime lastDate = new DateTime(lastMonth.Year, lastMonth.Month, Math.Min(day, lastMax));
                lblPreviewSchedule.Text = string.Format(
                    "• 1º pagamento: {0:dd/MM/yyyy}  até  Último ({1}º): {2:dd/MM/yyyy}",
                    firstDate, count, lastDate);
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show(this, "Por favor, digite o nome da conta (ex: Conta de Luz, Internet, Parcela Geladeira).", "Atenção", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtName.Focus();
                return;
            }

            ResultBill.Name = txtName.Text.Trim();
            ResultBill.Category = string.IsNullOrWhiteSpace(cmbCategory.Text) ? "Geral" : cmbCategory.Text.Trim();
            ResultBill.Amount = numAmount.Value;
            ResultBill.DueDay = (int)numDueDay.Value;
            ResultBill.StartMonth = cmbStartMonth.SelectedIndex + 1;
            ResultBill.StartYear = (int)numStartYear.Value;
            ResultBill.RecurrenceCount = rbContinuous.Checked ? 0 : (int)numRecurrenceCount.Value;
            ResultBill.Notes = txtNotes.Text.Trim();
            ResultBill.Active = true;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 65, 85)
            };
        }

        private static BillDefinition CloneBill(BillDefinition b)
        {
            return new BillDefinition
            {
                Id = b.Id,
                Name = b.Name,
                Category = b.Category,
                Amount = b.Amount,
                DueDay = b.DueDay,
                StartMonth = b.StartMonth,
                StartYear = b.StartYear,
                RecurrenceCount = b.RecurrenceCount,
                Notes = b.Notes,
                Active = b.Active
            };
        }
    }

    // =========================================================================
    // MAIN SPREADSHEET FORM
    // =========================================================================
    // =========================================================================
    // MAIN SPREADSHEET FORM
    // =========================================================================
    public class MainForm : Form
    {
        private AppDatabase db;
        private int selectedMonth;
        private int selectedYear;
        private string currentFilter = "TODAS"; // TODAS, PENDENTES, PAGAS, ATRASADAS

        // Controls
        private Label lblCurrentMonthTitle;
        private Label lblMonthCaption;
        private ComboBox cmbMonthSelect;
        private NumericUpDown numYearSelect;

        private Panel dashboardPanel;
        private Panel monthBox;
        private Panel cardTotal;
        private Panel cardPaid;
        private Panel cardPending;

        private Label lblCardTotalTitle;
        private Label lblCardTotalValue;
        private Label lblCardTotalSub;

        private Label lblCardPaidTitle;
        private Label lblCardPaidValue;
        private Label lblCardPaidSub;

        private Label lblCardPendingTitle;
        private Label lblCardPendingValue;
        private Label lblCardPendingSub;

        private TabControl tabControl;
        private DataGridView gridMonthly;
        private DataGridView gridAnnual;

        private Panel filterBar;
        private Label lblSearchIcon;
        private TextBox txtSearch;
        private Button btnFilterAll;
        private Button btnFilterPending;
        private Button btnFilterPaid;
        private Button btnFilterOverdue;
        private Button btnEditSelected;
        private Button btnDeleteSelected;
        private Label lblStatusFooter;

        private bool isPopulating = false;

        private static readonly string[] FullMonths = new[]
        {
            "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
            "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"
        };

        private static readonly string[] ShortMonths = new[]
        {
            "Jan", "Fev", "Mar", "Abr", "Mai", "Jun",
            "Jul", "Ago", "Set", "Out", "Nov", "Dez"
        };

        public MainForm()
        {
            db = StorageService.Load();
            selectedMonth = DateTime.Now.Month;
            selectedYear = DateTime.Now.Year;

            InitializeComponent();
            EnsureSampleWelcomeIfNeeded();
            SyncTabContext();
            RefreshAllViews();
        }

        private void EnsureSampleWelcomeIfNeeded()
        {
            // Keep clean if user has data; if brand new and empty, no forced clutter unless requested
        }

        private void InitializeComponent()
        {
            Text = "Contas a Pagar da Mãe - Planilha Inteligente (v" + StorageService.AppVersion + ")";
            Size = new Size(1260, 800);
            MinimumSize = new Size(1024, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 250, 252);
            Font = new Font("Segoe UI", 10F);
            Icon = AppIconBuilder.CreateAppIcon();

            // -----------------------------------------------------------------
            // 1. TOP HEADER BAR (Deep Slate with Emerald Accent)
            // -----------------------------------------------------------------
            Panel topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(18, 10, 18, 10)
            };
            topBar.Paint += (s, e) =>
            {
                using (LinearGradientBrush br = new LinearGradientBrush(
                    topBar.ClientRectangle,
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(30, 41, 59),
                    0f))
                {
                    e.Graphics.FillRectangle(br, topBar.ClientRectangle);
                }
                using (SolidBrush lineBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillRectangle(lineBrush, 0, topBar.Height - 3, topBar.Width, 3);
                }
            };

            PictureBox logoBox = new PictureBox
            {
                Image = AppIconBuilder.CreateIconBitmap(46),
                Size = new Size(46, 46),
                Location = new Point(18, 13),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.Transparent
            };
            topBar.Controls.Add(logoBox);

            Label lblAppTitle = new Label
            {
                Text = "Planilha de Contas a Pagar",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(74, 11),
                BackColor = Color.Transparent
            };
            topBar.Controls.Add(lblAppTitle);

            Label lblAppSubtitle = new Label
            {
                Text = "Visão Anual completa e controle mensal com checkmarks rápidos de pagamento",
                ForeColor = Color.FromArgb(203, 213, 225),
                Font = new Font("Segoe UI", 9.5F),
                AutoSize = true,
                Location = new Point(76, 41),
                BackColor = Color.Transparent
            };
            topBar.Controls.Add(lblAppSubtitle);

            // Right-aligned action buttons in topBar
            FlowLayoutPanel rightTopActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 8, 0, 0)
            };

            Button btnUpdate = CreateHeaderButton("Atualizações", Color.FromArgb(30, 41, 59), Color.FromArgb(241, 245, 249), 140, Color.FromArgb(71, 85, 105));
            btnUpdate.Click += async (s, e) => await AutoUpdater.CheckAndPromptUpdate(this, false);
            rightTopActions.Controls.Add(btnUpdate);

            Button btnExportCsv = CreateHeaderButton("Exportar Excel", Color.FromArgb(30, 41, 59), Color.FromArgb(241, 245, 249), 140, Color.FromArgb(71, 85, 105));
            btnExportCsv.Click += BtnExportCsv_Click;
            rightTopActions.Controls.Add(btnExportCsv);

            Button btnNewBillTop = CreateHeaderButton("+ Nova Conta", Color.FromArgb(16, 185, 129), Color.White, 150, Color.FromArgb(5, 150, 105));
            btnNewBillTop.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            btnNewBillTop.Click += BtnNewBill_Click;
            rightTopActions.Controls.Add(btnNewBillTop);

            topBar.Controls.Add(rightTopActions);

            // -----------------------------------------------------------------
            // 2. PERIOD NAVIGATION + SUMMARY CARDS BAR
            // -----------------------------------------------------------------
            dashboardPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(18, 12, 18, 12)
            };

            // Period Selector Box on Left
            monthBox = new Panel
            {
                Location = new Point(18, 12),
                Size = new Size(365, 88),
                BackColor = Color.White
            };
            monthBox.Paint += (s, e) =>
            {
                Control c = (Control)s;
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
                }
                using (SolidBrush acc = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    e.Graphics.FillRectangle(acc, 0, 0, 4, c.Height);
                }
            };

            lblMonthCaption = new Label
            {
                Text = "ANO DE REFERÊNCIA (VISÃO ANUAL)",
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 10),
                AutoSize = true
            };
            monthBox.Controls.Add(lblMonthCaption);

            Button btnPrevMonth = new Button
            {
                Text = "◀",
                Location = new Point(14, 34),
                Size = new Size(38, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnPrevMonth.FlatAppearance.BorderSize = 0;
            btnPrevMonth.Click += (s, e) => ShiftPeriod(-1);
            monthBox.Controls.Add(btnPrevMonth);

            cmbMonthSelect = new ComboBox
            {
                Location = new Point(56, 38),
                Size = new Size(130, 34),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMonthSelect.Items.AddRange(FullMonths);
            cmbMonthSelect.SelectedIndex = selectedMonth - 1;
            cmbMonthSelect.SelectedIndexChanged += (s, e) =>
            {
                if (isPopulating) return;
                selectedMonth = cmbMonthSelect.SelectedIndex + 1;
                RefreshAllViews();
            };
            monthBox.Controls.Add(cmbMonthSelect);

            numYearSelect = new NumericUpDown
            {
                Location = new Point(192, 38),
                Size = new Size(76, 34),
                Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                Minimum = 2020,
                Maximum = 2100,
                Value = selectedYear
            };
            numYearSelect.ValueChanged += (s, e) =>
            {
                if (isPopulating) return;
                selectedYear = (int)numYearSelect.Value;
                RefreshAllViews();
            };
            monthBox.Controls.Add(numYearSelect);

            Button btnNextMonth = new Button
            {
                Text = "▶",
                Location = new Point(274, 34),
                Size = new Size(38, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(241, 245, 249),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnNextMonth.FlatAppearance.BorderSize = 0;
            btnNextMonth.Click += (s, e) => ShiftPeriod(1);
            monthBox.Controls.Add(btnNextMonth);

            Button btnTodayMonth = new Button
            {
                Text = "Hoje",
                Location = new Point(316, 34),
                Size = new Size(42, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(209, 250, 229),
                ForeColor = Color.FromArgb(6, 95, 70),
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnTodayMonth.FlatAppearance.BorderSize = 0;
            btnTodayMonth.Click += (s, e) =>
            {
                selectedMonth = DateTime.Now.Month;
                selectedYear = DateTime.Now.Year;
                SyncMonthYearControls();
                RefreshAllViews();
            };
            monthBox.Controls.Add(btnTodayMonth);

            dashboardPanel.Controls.Add(monthBox);

            // Summary Cards: Total, Já Pago, Falta Pagar
            cardTotal = CreateSummaryCard(
                "TOTAL PREVISTO (ANO)",
                new Point(395, 12),
                Color.FromArgb(37, 99, 235), // Blue accent
                Color.FromArgb(15, 23, 42),
                out lblCardTotalTitle,
                out lblCardTotalValue,
                out lblCardTotalSub);
            dashboardPanel.Controls.Add(cardTotal);

            cardPaid = CreateSummaryCard(
                "JÁ PAGO NO ANO ✔",
                new Point(665, 12),
                Color.FromArgb(16, 185, 129), // Emerald accent
                Color.FromArgb(5, 150, 105),
                out lblCardPaidTitle,
                out lblCardPaidValue,
                out lblCardPaidSub);
            dashboardPanel.Controls.Add(cardPaid);

            cardPending = CreateSummaryCard(
                "RESTANTE NO ANO",
                new Point(935, 12),
                Color.FromArgb(225, 29, 72), // Rose accent
                Color.FromArgb(190, 18, 60),
                out lblCardPendingTitle,
                out lblCardPendingValue,
                out lblCardPendingSub);
            dashboardPanel.Controls.Add(cardPending);

            dashboardPanel.Resize += DashboardPanel_Resize;

            // -----------------------------------------------------------------
            // 3. FILTER & TOOLBAR ROW
            // -----------------------------------------------------------------
            filterBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(18, 6, 18, 6)
            };

            lblCurrentMonthTitle = new Label
            {
                Text = "Visão Anual Consolidada — Ano 2026",
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(15, 23, 42),
                Location = new Point(18, 12),
                AutoSize = true
            };
            filterBar.Controls.Add(lblCurrentMonthTitle);

            btnFilterAll = CreateFilterChip("Todas", new Point(310, 9), true);
            btnFilterAll.Click += (s, e) => SetFilter("TODAS");
            filterBar.Controls.Add(btnFilterAll);

            btnFilterPending = CreateFilterChip("Pendentes", new Point(386, 9), false);
            btnFilterPending.Click += (s, e) => SetFilter("PENDENTES");
            filterBar.Controls.Add(btnFilterPending);

            btnFilterPaid = CreateFilterChip("Pagas (✔)", new Point(486, 9), false);
            btnFilterPaid.Click += (s, e) => SetFilter("PAGAS");
            filterBar.Controls.Add(btnFilterPaid);

            btnFilterOverdue = CreateFilterChip("Atrasadas (!)", new Point(586, 9), false);
            btnFilterOverdue.Click += (s, e) => SetFilter("ATRASADAS");
            filterBar.Controls.Add(btnFilterOverdue);

            lblSearchIcon = new Label
            {
                Text = "Buscar:",
                Location = new Point(700, 14),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(71, 85, 105)
            };
            filterBar.Controls.Add(lblSearchIcon);

            txtSearch = new TextBox
            {
                Location = new Point(758, 11),
                Size = new Size(180, 28),
                Font = new Font("Segoe UI", 10F)
            };
            txtSearch.TextChanged += (s, e) => RefreshAllViews();
            filterBar.Controls.Add(txtSearch);

            btnEditSelected = new Button
            {
                Text = "Editar Conta",
                Location = new Point(950, 9),
                Size = new Size(115, 33),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnEditSelected.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            btnEditSelected.Click += BtnEditSelected_Click;
            filterBar.Controls.Add(btnEditSelected);

            btnDeleteSelected = new Button
            {
                Text = "Excluir",
                Location = new Point(1075, 9),
                Size = new Size(90, 33),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(254, 242, 242),
                ForeColor = Color.FromArgb(185, 28, 28),
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnDeleteSelected.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
            btnDeleteSelected.Click += BtnDeleteSelected_Click;
            filterBar.Controls.Add(btnDeleteSelected);

            filterBar.Resize += FilterBar_Resize;

            // -----------------------------------------------------------------
            // 4. STATUS FOOTER BAR
            // -----------------------------------------------------------------
            Panel footerBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(241, 245, 249),
                Padding = new Padding(18, 8, 18, 8)
            };

            lblStatusFooter = new Label
            {
                Text = "Dica: Na Visão Anual, clique em qualquer mês (Jan a Dez) para marcar ou desmarcar o pagamento (✔). Duplo clique na conta para editar.",
                AutoSize = true,
                Location = new Point(18, 9),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(51, 65, 85)
            };
            footerBar.Controls.Add(lblStatusFooter);

            // -----------------------------------------------------------------
            // 5. MAIN TAB CONTROL (Visão Anual como Principal + Detalhes do Mês)
            // -----------------------------------------------------------------
            Panel mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 4, 18, 10),
                BackColor = Color.FromArgb(248, 250, 252)
            };

            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ItemSize = new Size(270, 42),
                SizeMode = TabSizeMode.Fixed,
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            tabControl.DrawItem += TabControl_DrawItem;
            tabControl.SelectedIndexChanged += (s, e) =>
            {
                SyncTabContext();
                RefreshAllViews();
            };

            TabPage tabAnnual = new TabPage("Visão Anual (Principal)");
            tabAnnual.BackColor = Color.White;
            gridAnnual = CreateAnnualMatrixGrid();
            tabAnnual.Controls.Add(gridAnnual);

            TabPage tabMonthly = new TabPage("Detalhes do Mês (Planilha)");
            tabMonthly.BackColor = Color.White;
            gridMonthly = CreateMonthlySpreadsheetGrid();
            tabMonthly.Controls.Add(gridMonthly);

            // Aba Anual como Principal (Tab 0) e Detalhes do Mês como Segunda (Tab 1)
            tabControl.TabPages.Add(tabAnnual);
            tabControl.TabPages.Add(tabMonthly);
            tabControl.SelectedIndex = 0;
            mainContainer.Controls.Add(tabControl);

            // Add in proper WinForms Dock order
            Controls.Add(mainContainer);
            Controls.Add(filterBar);
            Controls.Add(dashboardPanel);
            Controls.Add(topBar);
            Controls.Add(footerBar);
        }

        private DataGridView CreateMonthlySpreadsheetGrid()
        {
            DataGridView dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(226, 232, 240),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                RowTemplate = { Height = 44 }
            };

            // Enable double buffering via reflection for ultra-smooth scrolling
            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null,
                dgv,
                new object[] { true });

            dgv.ColumnHeadersHeight = 44;
            dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0),
                SelectionBackColor = Color.FromArgb(248, 250, 252),
                SelectionForeColor = Color.FromArgb(51, 65, 85)
            };

            dgv.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI", 10.5F),
                SelectionBackColor = Color.FromArgb(241, 245, 249),
                SelectionForeColor = Color.FromArgb(15, 23, 42),
                Padding = new Padding(8, 0, 8, 0)
            };

            // Columns:
            // 0: Checkbox PAGA?
            var colPaid = new DataGridViewCheckBoxColumn
            {
                Name = "ColPaid",
                HeaderText = "✔ PAGA?",
                FillWeight = 48,
                MinimumWidth = 85,
                ReadOnly = false
            };
            colPaid.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colPaid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgv.Columns.Add(colPaid);

            // 1: Data de Vencimento
            var colDueDate = new DataGridViewTextBoxColumn
            {
                Name = "ColDueDate",
                HeaderText = "DATA DO PAGAMENTO",
                FillWeight = 78,
                MinimumWidth = 140,
                ReadOnly = true
            };
            colDueDate.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            dgv.Columns.Add(colDueDate);

            // 2: Nome da Conta
            var colName = new DataGridViewTextBoxColumn
            {
                Name = "ColName",
                HeaderText = "CONTA / DESCRIÇÃO",
                FillWeight = 135,
                MinimumWidth = 200,
                ReadOnly = true
            };
            colName.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            dgv.Columns.Add(colName);

            // 3: Categoria
            var colCategory = new DataGridViewTextBoxColumn
            {
                Name = "ColCategory",
                HeaderText = "CATEGORIA",
                FillWeight = 80,
                MinimumWidth = 120,
                ReadOnly = true
            };
            dgv.Columns.Add(colCategory);

            // 4: Parcela / Recorrência
            var colInstallment = new DataGridViewTextBoxColumn
            {
                Name = "ColInstallment",
                HeaderText = "RECORRÊNCIA",
                FillWeight = 75,
                MinimumWidth = 120,
                ReadOnly = true
            };
            colInstallment.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colInstallment.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgv.Columns.Add(colInstallment);

            // 5: 1º Pagamento
            var colStart = new DataGridViewTextBoxColumn
            {
                Name = "ColStart",
                HeaderText = "1º PAGAMENTO",
                FillWeight = 68,
                MinimumWidth = 110,
                ReadOnly = true
            };
            colStart.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colStart.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgv.Columns.Add(colStart);

            // 6: Valor (Editável diretamente na planilha se quiser ajustar no mês)
            var colAmount = new DataGridViewTextBoxColumn
            {
                Name = "ColAmount",
                HeaderText = "VALOR (R$)",
                FillWeight = 75,
                MinimumWidth = 120,
                ReadOnly = false
            };
            colAmount.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            colAmount.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colAmount.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
            dgv.Columns.Add(colAmount);

            // 7: Situação (Paga / Em dia / Vence Hoje / Atrasada)
            var colStatus = new DataGridViewTextBoxColumn
            {
                Name = "ColStatus",
                HeaderText = "SITUAÇÃO NO MÊS",
                FillWeight = 95,
                MinimumWidth = 150,
                ReadOnly = true
            };
            dgv.Columns.Add(colStatus);

            // 8: Pago em / Observações
            var colNotes = new DataGridViewTextBoxColumn
            {
                Name = "ColNotes",
                HeaderText = "OBSERVAÇÕES / DATA PAGO",
                FillWeight = 110,
                MinimumWidth = 150,
                ReadOnly = false
            };
            dgv.Columns.Add(colNotes);

            // Immediate checkbox toggle on single click
            dgv.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgv.IsCurrentCellDirty && dgv.CurrentCell is DataGridViewCheckBoxCell)
                {
                    dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            dgv.CellValueChanged += DgvMonthly_CellValueChanged;
            dgv.CellDoubleClick += DgvMonthly_CellDoubleClick;

            return dgv;
        }

        private DataGridView CreateAnnualMatrixGrid()
        {
            DataGridView dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.Single,
                GridColor = Color.FromArgb(226, 232, 240),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false,
                RowTemplate = { Height = 44 }
            };

            typeof(DataGridView).InvokeMember(
                "DoubleBuffered",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                null,
                dgv,
                new object[] { true });

            dgv.ColumnHeadersHeight = 44;
            dgv.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgv.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = Color.FromArgb(51, 65, 85),
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Color.FromArgb(248, 250, 252),
                SelectionForeColor = Color.FromArgb(51, 65, 85)
            };

            dgv.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(30, 41, 59),
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                SelectionBackColor = Color.FromArgb(241, 245, 249),
                SelectionForeColor = Color.FromArgb(15, 23, 42),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };

            var colBill = new DataGridViewTextBoxColumn
            {
                Name = "AnnualBillName",
                HeaderText = "CONTA / DESCRIÇÃO (CLIQUE NO MÊS PARA ✔)",
                FillWeight = 165,
                MinimumWidth = 190,
                ReadOnly = true
            };
            colBill.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            colBill.DefaultCellStyle.Padding = new Padding(10, 0, 4, 0);
            colBill.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dgv.Columns.Add(colBill);

            var colDue = new DataGridViewTextBoxColumn
            {
                Name = "AnnualDueDay",
                HeaderText = "VENC.",
                FillWeight = 48,
                MinimumWidth = 65,
                ReadOnly = true
            };
            colDue.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgv.Columns.Add(colDue);

            for (int m = 1; m <= 12; m++)
            {
                var colM = new DataGridViewTextBoxColumn
                {
                    Name = "Month_" + m,
                    HeaderText = ShortMonths[m - 1].ToUpper(),
                    FillWeight = 50,
                    MinimumWidth = 64,
                    ReadOnly = true
                };
                colM.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                colM.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
                dgv.Columns.Add(colM);
            }

            var colTotal = new DataGridViewTextBoxColumn
            {
                Name = "AnnualTotal",
                HeaderText = "TOTAL NO ANO",
                FillWeight = 78,
                MinimumWidth = 110,
                ReadOnly = true
            };
            colTotal.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colTotal.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            colTotal.DefaultCellStyle.Padding = new Padding(0, 0, 10, 0);
            colTotal.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            dgv.Columns.Add(colTotal);

            dgv.CellPainting += DgvAnnual_CellPainting;
            dgv.CellClick += DgvAnnual_CellClick;
            dgv.CellDoubleClick += DgvAnnual_CellDoubleClick;
            dgv.ColumnHeaderMouseClick += DgvAnnual_ColumnHeaderMouseClick;
            return dgv;
        }

        // ---------------------------------------------------------------------
        // POPULATE & REFRESH LOGIC
        // ---------------------------------------------------------------------
        public void RefreshAllViews()
        {
            isPopulating = true;
            try
            {
                SyncTabContext();
                PopulateMonthlyGrid();
                PopulateAnnualGrid();
            }
            finally
            {
                isPopulating = false;
            }
        }

        private void SyncTabContext()
        {
            if (tabControl.SelectedIndex == 0)
            {
                lblMonthCaption.Text = "ANO DE REFERÊNCIA (VISÃO ANUAL)";
                lblCurrentMonthTitle.Text = string.Format("Visão Anual Consolidada — Ano {0}", selectedYear);
                lblStatusFooter.Text = "• Dica: Na Visão Anual, clique em qualquer mês (Jan a Dez) para marcar ou desmarcar o pagamento (✔). Duplo clique na conta para editar.";
            }
            else
            {
                lblMonthCaption.Text = "MÊS DE REFERÊNCIA DA PLANILHA";
                lblCurrentMonthTitle.Text = string.Format("Planilha de {0} de {1}", FullMonths[selectedMonth - 1], selectedYear);
                lblStatusFooter.Text = "• Dica: Marque a caixinha '✔ PAGA?' para marcar como paga. Você também pode alterar o valor do mês diretamente na planilha.";
            }
        }

        private void PopulateMonthlyGrid()
        {
            string selectedBillId = GetSelectedBillId();
            gridMonthly.Rows.Clear();

            decimal totalMonth = 0m;
            decimal totalPaid = 0m;
            decimal totalPending = 0m;
            int countTotal = 0;
            int countPaid = 0;
            int countPending = 0;

            string search = (txtSearch.Text ?? "").Trim().ToLowerInvariant();

            var orderedBills = db.Bills
                .Where(b => b.Active)
                .OrderBy(b => b.DueDay)
                .ThenBy(b => b.Name)
                .ToList();

            foreach (var bill in orderedBills)
            {
                int installmentNum;
                if (!bill.AppliesToMonth(selectedYear, selectedMonth, out installmentNum))
                    continue;

                var payment = GetOrCreatePaymentRecord(bill.Id, selectedYear, selectedMonth, false);
                bool isPaid = payment != null && payment.IsPaid;
                decimal amount = (payment != null && payment.CustomAmount > 0) ? payment.CustomAmount : bill.Amount;

                // Totals always reflect all active bills in the month
                totalMonth += amount;
                countTotal++;
                if (isPaid)
                {
                    totalPaid += amount;
                    countPaid++;
                }
                else
                {
                    totalPending += amount;
                    countPending++;
                }

                DateTime dueDate = bill.GetDueDateForMonth(selectedYear, selectedMonth);
                DateTime today = DateTime.Today;
                bool isOverdue = !isPaid && dueDate < today;

                // Filter check
                if (currentFilter == "PENDENTES" && isPaid) continue;
                if (currentFilter == "PAGAS" && !isPaid) continue;
                if (currentFilter == "ATRASADAS" && !isOverdue) continue;

                if (!string.IsNullOrEmpty(search))
                {
                    bool match = (bill.Name ?? "").ToLowerInvariant().Contains(search) ||
                                 (bill.Category ?? "").ToLowerInvariant().Contains(search) ||
                                 (bill.Notes ?? "").ToLowerInvariant().Contains(search);
                    if (!match) continue;
                }

                string dueDateFormatted = dueDate.ToString("dd/MM/yyyy");

                string recurrenceText = bill.RecurrenceCount > 0
                    ? string.Format("Parcela {0} de {1}", installmentNum, bill.RecurrenceCount)
                    : "Fixa Mensal";

                string startText = string.Format("{0}/{1}", ShortMonths[Math.Min(11, Math.Max(0, bill.StartMonth - 1))], bill.StartYear);

                string statusText;
                Color statusBack;
                Color statusFore;
                Color rowBack;

                if (isPaid)
                {
                    statusText = "✔ PAGA";
                    statusBack = Color.FromArgb(220, 252, 231);
                    statusFore = Color.FromArgb(21, 128, 61);
                    rowBack = Color.FromArgb(247, 254, 250);
                }
                else if (dueDate < today)
                {
                    int daysLate = (today - dueDate).Days;
                    statusText = string.Format("! ATRASADA ({0}d)", daysLate);
                    statusBack = Color.FromArgb(254, 226, 226);
                    statusFore = Color.FromArgb(185, 28, 28);
                    rowBack = Color.FromArgb(255, 250, 250);
                }
                else if (dueDate == today)
                {
                    statusText = "VENCE HOJE!";
                    statusBack = Color.FromArgb(254, 243, 199);
                    statusFore = Color.FromArgb(180, 83, 9);
                    rowBack = Color.FromArgb(255, 253, 245);
                }
                else
                {
                    int daysLeft = (dueDate - today).Days;
                    statusText = daysLeft <= 5
                        ? string.Format("Vence em {0} dia(s)", daysLeft)
                        : "Em aberto";
                    statusBack = Color.FromArgb(241, 245, 249);
                    statusFore = Color.FromArgb(71, 85, 105);
                    rowBack = Color.White;
                }

                string noteCellText = "";
                if (isPaid && payment != null && !string.IsNullOrEmpty(payment.PaidDateStr))
                {
                    noteCellText = "Pago em " + payment.PaidDateStr;
                    if (!string.IsNullOrWhiteSpace(bill.Notes))
                        noteCellText += " • " + bill.Notes;
                }
                else
                {
                    noteCellText = (payment != null && !string.IsNullOrWhiteSpace(payment.CustomNotes))
                        ? payment.CustomNotes
                        : (bill.Notes ?? "");
                }

                int rowIndex = gridMonthly.Rows.Add(
                    isPaid,
                    dueDateFormatted,
                    bill.Name,
                    bill.Category,
                    recurrenceText,
                    startText,
                    amount.ToString("N2", new CultureInfo("pt-BR")),
                    statusText,
                    noteCellText
                );

                DataGridViewRow row = gridMonthly.Rows[rowIndex];
                row.Tag = bill.Id;
                row.DefaultCellStyle.BackColor = rowBack;

                if (isPaid)
                {
                    row.Cells["ColName"].Style.ForeColor = Color.FromArgb(21, 128, 61);
                }

                row.Cells["ColStatus"].Style.BackColor = statusBack;
                row.Cells["ColStatus"].Style.ForeColor = statusFore;
                row.Cells["ColStatus"].Style.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
                row.Cells["ColStatus"].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

                if (selectedBillId != null && bill.Id == selectedBillId)
                {
                    row.Selected = true;
                }
            }

            // Update Summary Cards if Detalhes do Mês is the active tab
            if (tabControl.SelectedIndex == 1)
            {
                CultureInfo ptBr = new CultureInfo("pt-BR");
                lblCardTotalTitle.Text = string.Format("TOTAL NO MÊS ({0})", ShortMonths[selectedMonth - 1].ToUpper());
                lblCardTotalValue.Text = totalMonth.ToString("C2", ptBr);
                lblCardTotalSub.Text = string.Format("{0} conta(s) neste mês", countTotal);

                lblCardPaidTitle.Text = string.Format("JÁ PAGO NO MÊS ({0}) ✔", ShortMonths[selectedMonth - 1].ToUpper());
                lblCardPaidValue.Text = totalPaid.ToString("C2", ptBr);
                lblCardPaidSub.Text = string.Format("{0} de {1} conta(s) pagas", countPaid, countTotal);

                lblCardPendingTitle.Text = string.Format("FALTA PAGAR ({0})", ShortMonths[selectedMonth - 1].ToUpper());
                lblCardPendingValue.Text = totalPending.ToString("C2", ptBr);
                lblCardPendingSub.Text = countPending == 0 && countTotal > 0
                    ? "✔ Tudo pago neste mês!"
                    : string.Format("{0} conta(s) pendente(s)", countPending);
            }
        }

        private void PopulateAnnualGrid()
        {
            gridAnnual.Rows.Clear();
            CultureInfo ptBr = new CultureInfo("pt-BR");

            var orderedBills = db.Bills
                .Where(b => b.Active)
                .OrderBy(b => b.DueDay)
                .ThenBy(b => b.Name)
                .ToList();

            string search = (txtSearch.Text ?? "").Trim().ToLowerInvariant();

            decimal[] monthTotals = new decimal[12];
            decimal[] monthPaidTotals = new decimal[12];
            decimal grandYearTotal = 0m;
            decimal grandYearPaid = 0m;
            int totalActiveBillsInYear = 0;
            int totalYearInstallments = 0;
            int totalYearPaidInstallments = 0;

            foreach (var bill in orderedBills)
            {
                // Check if applies to any month in selectedYear
                bool appliesInYear = false;
                decimal billYearTotal = 0m;
                int billPaidCount = 0;
                int billTotalCount = 0;
                bool billHasOverdue = false;
                bool billHasPending = false;

                for (int m = 1; m <= 12; m++)
                {
                    int inst;
                    if (bill.AppliesToMonth(selectedYear, m, out inst))
                    {
                        appliesInYear = true;
                        billTotalCount++;
                        var rec = GetOrCreatePaymentRecord(bill.Id, selectedYear, m, false);
                        bool isPaid = rec != null && rec.IsPaid;
                        decimal amount = (rec != null && rec.CustomAmount > 0) ? rec.CustomAmount : bill.Amount;
                        billYearTotal += amount;
                        monthTotals[m - 1] += amount;

                        DateTime dueDate = bill.GetDueDateForMonth(selectedYear, m);
                        if (isPaid)
                        {
                            billPaidCount++;
                            monthPaidTotals[m - 1] += amount;
                        }
                        else
                        {
                            billHasPending = true;
                            if (dueDate < DateTime.Today)
                            {
                                billHasOverdue = true;
                            }
                        }
                    }
                }

                if (!appliesInYear) continue;

                totalActiveBillsInYear++;
                grandYearTotal += billYearTotal;
                totalYearInstallments += billTotalCount;
                totalYearPaidInstallments += billPaidCount;

                // Search check
                if (!string.IsNullOrEmpty(search))
                {
                    bool match = (bill.Name ?? "").ToLowerInvariant().Contains(search) ||
                                 (bill.Category ?? "").ToLowerInvariant().Contains(search) ||
                                 (bill.Notes ?? "").ToLowerInvariant().Contains(search);
                    if (!match) continue;
                }

                // Filter check for Annual View
                if (currentFilter == "PENDENTES" && !billHasPending) continue;
                if (currentFilter == "PAGAS" && (billHasPending || billTotalCount == 0)) continue;
                if (currentFilter == "ATRASADAS" && !billHasOverdue) continue;

                object[] rowVals = new object[15];
                string recInfo = bill.RecurrenceCount > 0 ? string.Format(" ({0}x)", bill.RecurrenceCount) : " (Fixa)";
                rowVals[0] = bill.Name + recInfo;
                rowVals[1] = "Dia " + bill.DueDay.ToString("00");

                for (int m = 1; m <= 12; m++)
                {
                    int inst;
                    if (!bill.AppliesToMonth(selectedYear, m, out inst))
                    {
                        rowVals[m + 1] = "—";
                    }
                    else
                    {
                        var rec = GetOrCreatePaymentRecord(bill.Id, selectedYear, m, false);
                        bool paid = rec != null && rec.IsPaid;
                        DateTime dueDate = bill.GetDueDateForMonth(selectedYear, m);
                        rowVals[m + 1] = paid
                            ? string.Format("✔ {0:00}/{1:00}", dueDate.Day, m)
                            : string.Format("{0:00}/{1:00}", dueDate.Day, m);
                    }
                }
                rowVals[14] = billYearTotal.ToString("C2", ptBr);

                int idx = gridAnnual.Rows.Add(rowVals);
                DataGridViewRow row = gridAnnual.Rows[idx];
                row.Tag = bill.Id;
            }

            // Summary rows at the bottom
            grandYearPaid = monthPaidTotals.Sum();
            decimal grandYearPending = grandYearTotal - grandYearPaid;

            if (gridAnnual.Rows.Count > 0)
            {
                // Summary Row 1: Total Previsto por Mês
                object[] sumTotalVals = new object[15];
                sumTotalVals[0] = "∑ TOTAL ESTIMADO DO MÊS";
                sumTotalVals[1] = "—";
                for (int m = 1; m <= 12; m++)
                {
                    sumTotalVals[m + 1] = monthTotals[m - 1] > 0 ? monthTotals[m - 1].ToString("C0", ptBr) : "R$ 0";
                }
                sumTotalVals[14] = grandYearTotal.ToString("C2", ptBr);

                int sIdx1 = gridAnnual.Rows.Add(sumTotalVals);
                DataGridViewRow sRow1 = gridAnnual.Rows[sIdx1];
                sRow1.Tag = "SUMMARY_TOTAL";
                sRow1.DefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
                sRow1.DefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
                sRow1.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);

                // Summary Row 2: Total Pago por Mês
                object[] sumPaidVals = new object[15];
                sumPaidVals[0] = "✔ JÁ PAGO NO MÊS";
                sumPaidVals[1] = "—";
                for (int m = 1; m <= 12; m++)
                {
                    sumPaidVals[m + 1] = monthPaidTotals[m - 1] > 0 ? monthPaidTotals[m - 1].ToString("C0", ptBr) : "R$ 0";
                }
                sumPaidVals[14] = grandYearPaid.ToString("C2", ptBr);

                int sIdx2 = gridAnnual.Rows.Add(sumPaidVals);
                DataGridViewRow sRow2 = gridAnnual.Rows[sIdx2];
                sRow2.Tag = "SUMMARY_PAID";
                sRow2.DefaultCellStyle.BackColor = Color.FromArgb(240, 253, 244);
                sRow2.DefaultCellStyle.ForeColor = Color.FromArgb(21, 128, 61);
                sRow2.DefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            }

            // If Visão Anual is the active tab, update the Dashboard Summary Cards with Year stats
            if (tabControl.SelectedIndex == 0)
            {
                lblCardTotalTitle.Text = string.Format("TOTAL PREVISTO ({0})", selectedYear);
                lblCardTotalValue.Text = grandYearTotal.ToString("C2", ptBr);
                lblCardTotalSub.Text = string.Format("{0} conta(s) ativas no ano", totalActiveBillsInYear);

                lblCardPaidTitle.Text = string.Format("JÁ PAGO NO ANO ({0}) ✔", selectedYear);
                lblCardPaidValue.Text = grandYearPaid.ToString("C2", ptBr);
                lblCardPaidSub.Text = string.Format("{0} de {1} parcelas pagas", totalYearPaidInstallments, totalYearInstallments);

                lblCardPendingTitle.Text = string.Format("RESTANTE NO ANO ({0})", selectedYear);
                lblCardPendingValue.Text = grandYearPending.ToString("C2", ptBr);
                lblCardPendingSub.Text = grandYearPending <= 0 && grandYearTotal > 0
                    ? "✔ Todas as contas quitadas!"
                    : string.Format("{0} parcelas pendentes", totalYearInstallments - totalYearPaidInstallments);
            }
        }

        // ---------------------------------------------------------------------
        // GRID INTERACTION EVENTS
        // ---------------------------------------------------------------------
        private void DgvMonthly_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isPopulating || e.RowIndex < 0) return;

            DataGridViewRow row = gridMonthly.Rows[e.RowIndex];
            string billId = row.Tag as string;
            if (string.IsNullOrEmpty(billId)) return;

            var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
            if (bill == null) return;

            var record = GetOrCreatePaymentRecord(billId, selectedYear, selectedMonth, true);

            if (e.ColumnIndex == gridMonthly.Columns["ColPaid"].Index)
            {
                bool isChecked = Convert.ToBoolean(row.Cells["ColPaid"].Value);
                record.IsPaid = isChecked;
                record.PaidDateStr = isChecked ? DateTime.Now.ToString("dd/MM/yyyy") : "";
                StorageService.Save(db);
                RefreshAllViews();
            }
            else if (e.ColumnIndex == gridMonthly.Columns["ColAmount"].Index)
            {
                string raw = Convert.ToString(row.Cells["ColAmount"].Value) ?? "";
                raw = raw.Replace("R$", "").Trim();
                decimal val;
                if (decimal.TryParse(raw, NumberStyles.Any, new CultureInfo("pt-BR"), out val) && val >= 0)
                {
                    record.CustomAmount = val;
                    bill.Amount = val;
                    StorageService.Save(db);
                }
                RefreshAllViews();
            }
            else if (e.ColumnIndex == gridMonthly.Columns["ColNotes"].Index)
            {
                string note = Convert.ToString(row.Cells["ColNotes"].Value) ?? "";
                record.CustomNotes = note;
                StorageService.Save(db);
            }
        }

        private void DgvMonthly_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0) return;
            BtnEditSelected_Click(sender, EventArgs.Empty);
        }

        private void DgvAnnual_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (isPopulating || e.RowIndex < 0 || e.ColumnIndex < 2 || e.ColumnIndex > 13) return;

            string billId = gridAnnual.Rows[e.RowIndex].Tag as string;
            if (string.IsNullOrEmpty(billId) || billId.StartsWith("SUMMARY_")) return;

            int monthClicked = e.ColumnIndex - 1; // 1..12
            var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
            if (bill == null) return;

            int inst;
            if (!bill.AppliesToMonth(selectedYear, monthClicked, out inst)) return;

            var rec = GetOrCreatePaymentRecord(billId, selectedYear, monthClicked, true);
            rec.IsPaid = !rec.IsPaid;
            rec.PaidDateStr = rec.IsPaid ? DateTime.Now.ToString("dd/MM/yyyy") : "";

            StorageService.Save(db);
            RefreshAllViews();

            lblStatusFooter.Text = string.Format("✔ Conta '{0}' marcada como {1} em {2}/{3}!",
                bill.Name,
                rec.IsPaid ? "PAGA" : "PENDENTE",
                ShortMonths[monthClicked - 1],
                selectedYear);
        }

        private void DgvAnnual_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string billId = gridAnnual.Rows[e.RowIndex].Tag as string;
            if (string.IsNullOrEmpty(billId) || billId.StartsWith("SUMMARY_")) return;

            BtnEditSelected_Click(sender, EventArgs.Empty);
        }

        private void DgvAnnual_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex >= 2 && e.ColumnIndex <= 13)
            {
                int monthClicked = e.ColumnIndex - 1;
                selectedMonth = monthClicked;
                SyncMonthYearControls();
                RefreshAllViews();
                lblStatusFooter.Text = string.Format("• Dica: Mês alterado para {0}/{1}. Para ver a planilha detalhada deste mês, abra a aba 'Detalhes do Mês'.", FullMonths[selectedMonth - 1], selectedYear);
            }
        }

        private void DgvAnnual_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                // Column Headers
                if (e.ColumnIndex >= 2 && e.ColumnIndex <= 13)
                {
                    int m = e.ColumnIndex - 1;
                    bool isCurrentMonth = (m == DateTime.Now.Month && selectedYear == DateTime.Now.Year);
                    bool isSelectedMonth = (m == selectedMonth);

                    // Paint background
                    Color headerBg = isCurrentMonth
                        ? Color.FromArgb(236, 253, 245)
                        : (isSelectedMonth ? Color.FromArgb(241, 245, 249) : Color.FromArgb(248, 250, 252));

                    using (SolidBrush b = new SolidBrush(headerBg))
                    {
                        e.Graphics.FillRectangle(b, e.CellBounds);
                    }

                    // Grid borders
                    using (Pen borderPen = new Pen(Color.FromArgb(226, 232, 240)))
                    {
                        e.Graphics.DrawLine(borderPen, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                        e.Graphics.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                    }

                    // If current month, draw green indicator bar at top
                    if (isCurrentMonth)
                    {
                        using (SolidBrush topBar = new SolidBrush(Color.FromArgb(16, 185, 129)))
                        {
                            e.Graphics.FillRectangle(topBar, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width, 3);
                        }
                    }

                    // Text
                    string headerText = ShortMonths[m - 1].ToUpper();
                    if (isCurrentMonth) headerText = "● " + headerText;

                    Color textCol = isCurrentMonth ? Color.FromArgb(4, 120, 87) : Color.FromArgb(51, 65, 85);
                    Font f = isCurrentMonth
                        ? new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold)
                        : new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);

                    TextRenderer.DrawText(
                        e.Graphics,
                        headerText,
                        f,
                        e.CellBounds,
                        textCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                    e.Handled = true;
                    return;
                }
                return;
            }

            DataGridViewRow row = gridAnnual.Rows[e.RowIndex];
            string tag = row.Tag as string;

            // Check if this is a summary row
            if (tag != null && tag.StartsWith("SUMMARY_"))
            {
                bool isPaidRow = tag == "SUMMARY_PAID";
                Color sumBg = isPaidRow ? Color.FromArgb(240, 253, 244) : Color.FromArgb(241, 245, 249);
                Color sumTextCol = isPaidRow ? Color.FromArgb(21, 128, 61) : Color.FromArgb(15, 23, 42);

                using (SolidBrush b = new SolidBrush(sumBg))
                {
                    e.Graphics.FillRectangle(b, e.CellBounds);
                }

                using (Pen p = new Pen(Color.FromArgb(226, 232, 240)))
                {
                    e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Right, e.CellBounds.Top);
                    e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                    e.Graphics.DrawLine(p, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                }

                string val = Convert.ToString(e.Value) ?? "";
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
                if (e.ColumnIndex == 0)
                {
                    flags |= TextFormatFlags.Left;
                    Rectangle padRect = new Rectangle(e.CellBounds.Left + 10, e.CellBounds.Top, e.CellBounds.Width - 14, e.CellBounds.Height);
                    TextRenderer.DrawText(e.Graphics, val, new Font("Segoe UI Semibold", 10F, FontStyle.Bold), padRect, sumTextCol, flags);
                }
                else if (e.ColumnIndex == 14)
                {
                    flags |= TextFormatFlags.Right;
                    Rectangle padRect = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Width - 10, e.CellBounds.Height);
                    TextRenderer.DrawText(e.Graphics, val, new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold), padRect, sumTextCol, flags);
                }
                else
                {
                    flags |= TextFormatFlags.HorizontalCenter;
                    TextRenderer.DrawText(e.Graphics, val, new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold), e.CellBounds, sumTextCol, flags);
                }

                e.Handled = true;
                return;
            }

            // Bill row: Handle Month columns (2..13)
            if (e.ColumnIndex >= 2 && e.ColumnIndex <= 13)
            {
                int m = e.ColumnIndex - 1;
                Color rowBg = (e.RowIndex % 2 == 0) ? Color.White : Color.FromArgb(250, 252, 255);
                using (SolidBrush b = new SolidBrush(rowBg))
                {
                    e.Graphics.FillRectangle(b, e.CellBounds);
                }

                using (Pen p = new Pen(Color.FromArgb(241, 245, 249)))
                {
                    e.Graphics.DrawLine(p, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                    e.Graphics.DrawLine(p, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
                }

                string val = Convert.ToString(e.Value) ?? "";
                if (val == "—" || string.IsNullOrEmpty(val))
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        "—",
                        new Font("Segoe UI", 11F),
                        e.CellBounds,
                        Color.FromArgb(203, 213, 225),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    e.Handled = true;
                    return;
                }

                // Determine state from bill and payment
                string billId = tag;
                var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
                if (bill != null)
                {
                    var rec = GetOrCreatePaymentRecord(billId, selectedYear, m, false);
                    bool isPaid = rec != null && rec.IsPaid;
                    DateTime dueDate = bill.GetDueDateForMonth(selectedYear, m);
                    DateTime today = DateTime.Today;

                    Color pillBg;
                    Color pillBorder;
                    Color pillText;
                    string pillLabel;

                    if (isPaid)
                    {
                        pillBg = Color.FromArgb(220, 252, 231);
                        pillBorder = Color.FromArgb(134, 239, 172);
                        pillText = Color.FromArgb(21, 128, 61);
                        pillLabel = string.Format("✔ {0:00}/{1:00}", dueDate.Day, m);
                    }
                    else if (dueDate < today)
                    {
                        pillBg = Color.FromArgb(254, 226, 226);
                        pillBorder = Color.FromArgb(252, 165, 165);
                        pillText = Color.FromArgb(185, 28, 28);
                        pillLabel = string.Format("! {0:00}/{1:00}", dueDate.Day, m);
                    }
                    else if (dueDate == today)
                    {
                        pillBg = Color.FromArgb(254, 243, 199);
                        pillBorder = Color.FromArgb(252, 211, 77);
                        pillText = Color.FromArgb(180, 83, 9);
                        pillLabel = "HOJE";
                    }
                    else
                    {
                        pillBg = Color.FromArgb(241, 245, 249);
                        pillBorder = Color.FromArgb(226, 232, 240);
                        pillText = Color.FromArgb(71, 85, 105);
                        pillLabel = string.Format("{0:00}/{1:00}", dueDate.Day, m);
                    }

                    Rectangle pillRect = new Rectangle(e.CellBounds.Left + 4, e.CellBounds.Top + 6, e.CellBounds.Width - 8, e.CellBounds.Height - 12);
                    using (GraphicsPath path = AppIconBuilder.RoundedRect(pillRect, 6))
                    {
                        using (SolidBrush pb = new SolidBrush(pillBg))
                        {
                            e.Graphics.FillPath(pb, path);
                        }
                        using (Pen pp = new Pen(pillBorder, 1))
                        {
                            e.Graphics.DrawPath(pp, path);
                        }
                    }

                    TextRenderer.DrawText(
                        e.Graphics,
                        pillLabel,
                        new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                        pillRect,
                        pillText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

                    e.Handled = true;
                    return;
                }
            }
        }

        // ---------------------------------------------------------------------
        // BUTTON ACTIONS (ADD, EDIT, DELETE, EXPORT)
        // ---------------------------------------------------------------------
        private void BtnNewBill_Click(object sender, EventArgs e)
        {
            using (var dlg = new BillDialog(null, selectedMonth, selectedYear))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultBill != null)
                {
                    db.Bills.Add(dlg.ResultBill);
                    StorageService.Save(db);

                    // Jump to the start year/month if user created a bill starting elsewhere
                    selectedMonth = dlg.ResultBill.StartMonth;
                    selectedYear = dlg.ResultBill.StartYear;
                    SyncMonthYearControls();
                    RefreshAllViews();
                }
            }
        }

        private void BtnEditSelected_Click(object sender, EventArgs e)
        {
            string billId = GetSelectedBillId();
            if (string.IsNullOrEmpty(billId))
            {
                MessageBox.Show(this, "Selecione uma conta na planilha para editar.", "Editar Conta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
            if (bill == null) return;

            using (var dlg = new BillDialog(bill, selectedMonth, selectedYear))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultBill != null)
                {
                    bill.Name = dlg.ResultBill.Name;
                    bill.Category = dlg.ResultBill.Category;
                    bill.Amount = dlg.ResultBill.Amount;
                    bill.DueDay = dlg.ResultBill.DueDay;
                    bill.StartMonth = dlg.ResultBill.StartMonth;
                    bill.StartYear = dlg.ResultBill.StartYear;
                    bill.RecurrenceCount = dlg.ResultBill.RecurrenceCount;
                    bill.Notes = dlg.ResultBill.Notes;

                    StorageService.Save(db);
                    RefreshAllViews();
                }
            }
        }

        private void BtnDeleteSelected_Click(object sender, EventArgs e)
        {
            string billId = GetSelectedBillId();
            if (string.IsNullOrEmpty(billId))
            {
                MessageBox.Show(this, "Selecione uma conta na planilha para excluir.", "Excluir Conta", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bill = db.Bills.FirstOrDefault(b => b.Id == billId);
            if (bill == null) return;

            var res = MessageBox.Show(
                this,
                string.Format("Tem certeza que deseja excluir a conta '{0}' de todas as planilhas?", bill.Name),
                "Confirmar Exclusão",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (res == DialogResult.Yes)
            {
                db.Bills.Remove(bill);
                db.Payments.RemoveAll(p => p.BillId == bill.Id);
                StorageService.Save(db);
                RefreshAllViews();
            }
        }

        private void BtnExportCsv_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                bool isAnnual = (tabControl.SelectedIndex == 0);
                sfd.Title = isAnnual
                    ? "Exportar Visão Anual para Excel (CSV)"
                    : "Exportar Planilha do Mês para Excel (CSV)";
                sfd.Filter = "Planilha CSV (*.csv)|*.csv";
                sfd.FileName = isAnnual
                    ? string.Format("Contas_Visao_Anual_{0}.csv", selectedYear)
                    : string.Format("Contas_{0}_{1}.csv", FullMonths[selectedMonth - 1], selectedYear);

                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    StringBuilder sb = new StringBuilder();
                    if (isAnnual)
                    {
                        sb.AppendLine("Conta;Vencimento;Jan;Fev;Mar;Abr;Mai;Jun;Jul;Ago;Set;Out;Nov;Dez;Total no Ano (R$)");
                        foreach (DataGridViewRow row in gridAnnual.Rows)
                        {
                            List<string> cols = new List<string>();
                            for (int c = 0; c < row.Cells.Count; c++)
                            {
                                cols.Add(EscapeCsv(Convert.ToString(row.Cells[c].Value)));
                            }
                            sb.AppendLine(string.Join(";", cols.ToArray()));
                        }
                    }
                    else
                    {
                        sb.AppendLine("Paga;Data do Pagamento;Conta;Categoria;Recorrencia;Primeiro Pagamento;Valor (R$);Situacao;Observacoes");
                        foreach (DataGridViewRow row in gridMonthly.Rows)
                        {
                            bool paid = Convert.ToBoolean(row.Cells["ColPaid"].Value);
                            sb.AppendFormat("{0};{1};{2};{3};{4};{5};{6};{7};{8}\r\n",
                                paid ? "SIM" : "NAO",
                                row.Cells["ColDueDate"].Value,
                                EscapeCsv(Convert.ToString(row.Cells["ColName"].Value)),
                                EscapeCsv(Convert.ToString(row.Cells["ColCategory"].Value)),
                                EscapeCsv(Convert.ToString(row.Cells["ColInstallment"].Value)),
                                EscapeCsv(Convert.ToString(row.Cells["ColStart"].Value)),
                                row.Cells["ColAmount"].Value,
                                EscapeCsv(Convert.ToString(row.Cells["ColStatus"].Value)),
                                EscapeCsv(Convert.ToString(row.Cells["ColNotes"].Value)));
                        }
                    }

                    File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show(this, "Planilha exportada com sucesso para:\n" + sfd.FileName, "Exportação Concluída", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        }

        // ---------------------------------------------------------------------
        // HELPERS
        // ---------------------------------------------------------------------
        private MonthlyPaymentRecord GetOrCreatePaymentRecord(string billId, int year, int month, bool createIfMissing)
        {
            var rec = db.Payments.FirstOrDefault(p => p.BillId == billId && p.Year == year && p.Month == month);
            if (rec == null && createIfMissing)
            {
                rec = new MonthlyPaymentRecord
                {
                    BillId = billId,
                    Year = year,
                    Month = month,
                    IsPaid = false,
                    PaidDateStr = "",
                    CustomAmount = 0m,
                    CustomNotes = ""
                };
                db.Payments.Add(rec);
            }
            return rec;
        }

        private string GetSelectedBillId()
        {
            if (tabControl.SelectedIndex == 0 && gridAnnual.CurrentRow != null)
            {
                string tag = gridAnnual.CurrentRow.Tag as string;
                if (!string.IsNullOrEmpty(tag) && !tag.StartsWith("SUMMARY_"))
                {
                    return tag;
                }
            }
            if (tabControl.SelectedIndex == 1 && gridMonthly.SelectedRows.Count > 0)
            {
                return gridMonthly.SelectedRows[0].Tag as string;
            }
            return null;
        }

        private void ShiftPeriod(int delta)
        {
            if (tabControl != null && tabControl.SelectedIndex == 0)
            {
                selectedYear += delta;
                selectedYear = Math.Min(2100, Math.Max(2020, selectedYear));
            }
            else
            {
                DateTime dt = new DateTime(selectedYear, selectedMonth, 1).AddMonths(delta);
                selectedMonth = dt.Month;
                selectedYear = dt.Year;
            }
            SyncMonthYearControls();
            RefreshAllViews();
        }

        private void SyncMonthYearControls()
        {
            isPopulating = true;
            try
            {
                cmbMonthSelect.SelectedIndex = selectedMonth - 1;
                numYearSelect.Value = Math.Min(numYearSelect.Maximum, Math.Max(numYearSelect.Minimum, selectedYear));
            }
            finally
            {
                isPopulating = false;
            }
        }

        private void SetFilter(string filter)
        {
            currentFilter = filter;
            StyleFilterChip(btnFilterAll, filter == "TODAS");
            StyleFilterChip(btnFilterPending, filter == "PENDENTES");
            StyleFilterChip(btnFilterPaid, filter == "PAGAS");
            StyleFilterChip(btnFilterOverdue, filter == "ATRASADAS");
            RefreshAllViews();
        }

        private void DashboardPanel_Resize(object sender, EventArgs e)
        {
            if (dashboardPanel == null || monthBox == null || cardTotal == null || cardPaid == null || cardPending == null) return;

            int pad = 16;
            int availableWidth = dashboardPanel.ClientSize.Width - (pad * 2);
            int boxWidth = Math.Max(350, Math.Min(380, (int)(availableWidth * 0.28f)));
            monthBox.Location = new Point(pad, 12);
            monthBox.Size = new Size(boxWidth, 88);

            int remainingWidth = availableWidth - boxWidth - (pad * 3);
            int cardWidth = Math.Max(180, remainingWidth / 3);

            cardTotal.Location = new Point(pad + boxWidth + pad, 12);
            cardTotal.Size = new Size(cardWidth, 88);

            cardPaid.Location = new Point(cardTotal.Right + pad, 12);
            cardPaid.Size = new Size(cardWidth, 88);

            cardPending.Location = new Point(cardPaid.Right + pad, 12);
            cardPending.Size = new Size(cardWidth, 88);
        }

        private void FilterBar_Resize(object sender, EventArgs e)
        {
            if (filterBar == null || btnDeleteSelected == null || btnEditSelected == null || txtSearch == null) return;

            int pad = 18;
            int rightEdge = filterBar.ClientSize.Width - pad;

            btnDeleteSelected.Location = new Point(rightEdge - btnDeleteSelected.Width, 9);
            btnEditSelected.Location = new Point(btnDeleteSelected.Left - 8 - btnEditSelected.Width, 9);

            int searchRight = btnEditSelected.Left - 16;
            int searchWidth = Math.Max(150, Math.Min(220, searchRight - 760));
            txtSearch.Size = new Size(searchWidth, 28);
            txtSearch.Location = new Point(searchRight - searchWidth, 11);

            if (lblSearchIcon != null)
            {
                lblSearchIcon.Location = new Point(txtSearch.Left - lblSearchIcon.Width - 6, 14);
            }
        }

        private void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender;
            TabPage page = tc.TabPages[e.Index];
            bool isSelected = tc.SelectedIndex == e.Index;
            Rectangle tabRect = tc.GetTabRect(e.Index);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Background
            Color backColor = isSelected ? Color.White : Color.FromArgb(241, 245, 249);
            using (SolidBrush b = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(b, tabRect);
            }

            // Divider line between tabs
            using (Pen p = new Pen(Color.FromArgb(226, 232, 240), 1))
            {
                e.Graphics.DrawLine(p, tabRect.Right - 1, tabRect.Top + 6, tabRect.Right - 1, tabRect.Bottom - 6);
            }

            // Active Tab bottom highlight indicator bar
            if (isSelected)
            {
                using (SolidBrush indicator = new SolidBrush(Color.FromArgb(16, 185, 129))) // Emerald accent
                {
                    e.Graphics.FillRectangle(indicator, tabRect.Left + 8, tabRect.Bottom - 4, tabRect.Width - 16, 4);
                }
            }

            // Tab Text
            Color textColor = isSelected ? Color.FromArgb(15, 23, 42) : Color.FromArgb(100, 116, 139);
            Font tabFont = isSelected
                ? new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold)
                : new Font("Segoe UI", 10F, FontStyle.Regular);

            TextRenderer.DrawText(
                e.Graphics,
                page.Text,
                tabFont,
                tabRect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        private static Button CreateHeaderButton(string text, Color backColor, Color foreColor, int width, Color borderColor)
        {
            Button btn = new Button
            {
                Text = text,
                Size = new Size(width, 42),
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(8, 0, 0, 0)
            };
            btn.FlatAppearance.BorderColor = borderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.MouseEnter += (s, e) =>
            {
                btn.BackColor = Color.FromArgb(
                    Math.Min(255, backColor.R + 15),
                    Math.Min(255, backColor.G + 15),
                    Math.Min(255, backColor.B + 15));
            };
            btn.MouseLeave += (s, e) =>
            {
                btn.BackColor = backColor;
            };
            return btn;
        }

        private static Button CreateFilterChip(string text, Point location, bool active)
        {
            Button btn = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(text.Length > 9 ? 116 : 82, 33),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            StyleFilterChip(btn, active);
            return btn;
        }

        private static void StyleFilterChip(Button btn, bool active)
        {
            if (active)
            {
                btn.BackColor = Color.FromArgb(15, 23, 42); // Deep Slate
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderColor = Color.FromArgb(15, 23, 42);
            }
            else
            {
                btn.BackColor = Color.White;
                btn.ForeColor = Color.FromArgb(71, 85, 105);
                btn.FlatAppearance.BorderColor = Color.FromArgb(226, 232, 240);
            }
        }

        private static Panel CreateSummaryCard(string title, Point location, Color accentColor, Color textColor, out Label lblTitle, out Label lblVal, out Label lblSub)
        {
            Panel p = new Panel
            {
                Location = location,
                Size = new Size(256, 88),
                BackColor = Color.White,
                Tag = accentColor
            };
            p.Paint += (s, e) =>
            {
                Control c = (Control)s;
                using (Pen pen = new Pen(Color.FromArgb(226, 232, 240), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
                }
                using (SolidBrush acc = new SolidBrush(accentColor))
                {
                    e.Graphics.FillRectangle(acc, 0, 0, 4, c.Height);
                }
            };

            lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 10),
                AutoSize = true
            };
            p.Controls.Add(lblTitle);

            lblVal = new Label
            {
                Text = "R$ 0,00",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = textColor,
                Location = new Point(12, 29),
                AutoSize = true
            };
            p.Controls.Add(lblVal);

            lblSub = new Label
            {
                Text = "0 contas",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(100, 116, 139),
                Location = new Point(14, 62),
                AutoSize = true
            };
            p.Controls.Add(lblSub);

            return p;
        }
    }

    // =========================================================================
    // PROGRAM ENTRY POINT
    // =========================================================================
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                string oldFile = Application.ExecutablePath + ".old";
                if (File.Exists(oldFile)) File.Delete(oldFile);
                InstallerForm.DeleteFileW(Application.ExecutablePath + ":Zone.Identifier");
            }
            catch { }

            string exeName = Path.GetFileName(Application.ExecutablePath) ?? "";
            bool hasInstallFlag = args != null && args.Any(a =>
                string.Equals(a, "--install", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/install", StringComparison.OrdinalIgnoreCase));

            bool hasUninstallFlag = args != null && args.Any(a =>
                string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

            bool hasSelfTestFlag = args != null && args.Any(a =>
                string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase));

            if (hasSelfTestFlag)
            {
                Environment.ExitCode = RunSelfTest();
                return;
            }

            if (hasUninstallFlag)
            {
                RunUninstall();
                return;
            }

            // If launched as instalador.exe (or with --install), run the friendly installer wizard!
            if (hasInstallFlag || exeName.IndexOf("instalador", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Application.Run(new InstallerForm());
                return;
            }

            Application.Run(new MainForm());
        }

        private static int RunSelfTest()
        {
            try
            {
                var bill = new BillDefinition
                {
                    Name = "Parcela Geladeira",
                    Category = "Casa / Moradia",
                    Amount = 250.50m,
                    DueDay = 15,
                    StartMonth = 11,
                    StartYear = 2026,
                    RecurrenceCount = 6
                };

                int inst;
                // Before start month -> false
                if (bill.AppliesToMonth(2026, 10, out inst))
                    throw new Exception("Should not apply before start month");

                // 1st month -> true, inst = 1
                if (!bill.AppliesToMonth(2026, 11, out inst) || inst != 1)
                    throw new Exception("Should apply to 1st month with installment 1");

                // 6th month (April 2027) -> true, inst = 6
                if (!bill.AppliesToMonth(2027, 4, out inst) || inst != 6)
                    throw new Exception("Should apply to 6th month with installment 6");

                // 7th month (May 2027) -> false
                if (bill.AppliesToMonth(2027, 5, out inst))
                    throw new Exception("Should not apply after RecurrenceCount months");

                // Due date check including February clamping
                var billDay31 = new BillDefinition { DueDay = 31, StartMonth = 1, StartYear = 2026, RecurrenceCount = 0 };
                DateTime febDue = billDay31.GetDueDateForMonth(2026, 2);
                if (febDue.Day != 28 || febDue.Month != 2)
                    throw new Exception("February due date clamping failed");

                // Version comparison check
                if (!AutoUpdater.IsNewerVersion("1.0.0", "1.0.1") || !AutoUpdater.IsNewerVersion("1.0.0", "1.1.0"))
                    throw new Exception("AutoUpdater version comparison failed");

                // Instantiate forms to verify UI layout construction
                using (var mainForm = new MainForm())
                using (var dlg = new BillDialog(bill, 11, 2026))
                using (var instForm = new InstallerForm())
                {
                    mainForm.RefreshAllViews();
                }

                Console.WriteLine("SELF_TEST_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF_TEST_FAILED: " + ex);
                return 1;
            }
        }

        private static void RunUninstall()
        {
            var res = MessageBox.Show(
                "Deseja desinstalar o aplicativo Contas a Pagar do computador?\n(Suas contas salvas continuarão preservadas por segurança).",
                "Desinstalar Contas a Pagar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                try
                {
                    string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Contas a Pagar.lnk");
                    if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                    string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Contas a Pagar.lnk");
                    if (File.Exists(startLnk)) File.Delete(startLnk);
                }
                catch { }

                MessageBox.Show("O aplicativo foi removido dos atalhos com sucesso.", "Desinstalado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
