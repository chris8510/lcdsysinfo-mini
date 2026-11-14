// LCDSysInfo Mini: tray application that renders a GOverlay template on the LCDSysInfo 3.5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("LCDSysInfo Mini")]
[assembly: System.Reflection.AssemblyProduct("LCDSysInfo Mini")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

namespace LcdSysInfoMini
{
    /// <summary>German when Windows' UI language is German, English otherwise.</summary>
    static class Tr
    {
        public static bool German { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de"; } }
        public static string T(string de, string en) { return German ? de : en; }
    }

    static class Log
    {
        static readonly object Gate = new object();
        public static string PathName;

        public static void Write(string msg)
        {
            lock (Gate)
            {
                try
                {
                    FileInfo fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(PathName);   // keep it small
                    File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine);
                }
                catch (IOException) { }
            }
        }
    }

    sealed class Worker
    {
        public volatile string TemplatePath;
        public volatile string Status = Tr.T("Startet ...", "Starting ...");
        volatile bool stop, redraw;
        readonly Thread thread;
        readonly string inventoryPath;

        public Worker(string templatePath, string inventoryPath)
        {
            TemplatePath = templatePath;
            this.inventoryPath = inventoryPath;
            thread = new Thread(Run) { IsBackground = true, Name = "display" };
        }

        public void Start() { thread.Start(); }
        public void Redraw() { redraw = true; }
        public void Stop() { stop = true; thread.Join(5000); }

        void Sleep(double seconds)
        {
            for (int i = 0; i < seconds * 10 && !stop && !redraw; i++) Thread.Sleep(100);
        }

        void Run()
        {
            Sensors sensors = null;
            try
            {
                sensors = new Sensors();
                try { sensors.WriteInventory(inventoryPath); }
                catch (Exception ex) { Log.Write(Tr.T("Sensorliste: ", "Sensor list: ") + ex.Message); }
                bool restricted = false;
                while (!stop)
                {
                    if (string.IsNullOrEmpty(TemplatePath) || !File.Exists(TemplatePath))
                    {
                        Status = Tr.T("Keine Vorlage gewählt", "No template selected");
                        Sleep(2.0);
                        continue;
                    }
                    try
                    {
                        using (Lcd lcd = Lcd.Open())
                        {
                            var files = lcd.ReadFileTable();
                            var tpl = new Template(TemplatePath);
                            var r = new Renderer(lcd, files, tpl);
                            r.DrawStatic();
                            redraw = false;
                            if (r.Missing.Count > 0)
                                Log.Write(Tr.T("Fehlt auf dem Gerät: ", "Missing on the device: ") + string.Join(", ", r.Missing));
                            Log.Write(Tr.T("Vorlage gezeichnet: ", "Template drawn: ") + Path.GetFileName(TemplatePath));
                            List<string> ids = tpl.SensorIds.ToList();
                            bool first = true;
                            while (!stop && !redraw)
                            {
                                var values = sensors.Read(ids);
                                r.Update(values);
                                Status = StatusText(values);
                                if (first)
                                {
                                    Log.Write(Tr.T("Werte: ", "Values: ") + string.Join(", ", values.Select(kv => kv.Key + "=" + Fmt(kv.Value))));
                                    first = false;
                                    if (!restricted)
                                    {
                                        Log.Write(Tr.T("Abgefragte Hardware: ", "Polled hardware: ") + string.Join(", ", sensors.RestrictToUsed()));
                                        restricted = true;
                                    }
                                }
                                Sleep(1.0);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (stop) break;
                        Status = Tr.T("Display nicht erreichbar", "Display not reachable");
                        Log.Write(Tr.T("Display nicht erreichbar, neuer Versuch in 5 s: ", "Display not reachable, retrying in 5 s: ") + ex.Message);
                        Sleep(5.0);
                    }
                }
            }
            catch (Exception ex)
            {
                Status = Tr.T("Fehler: ", "Error: ") + ex.Message;
                Log.Write(Tr.T("Abbruch: ", "Aborted: ") + ex);
            }
            finally
            {
                if (sensors != null) sensors.Dispose();
            }
        }

        static string Fmt(double? v) { return v.HasValue ? v.Value.ToString("0.#", CultureInfo.InvariantCulture) : "--"; }

        static string StatusText(Dictionary<string, double?> v)
        {
            Func<string, string> g = id => { double? x; v.TryGetValue(id, out x); return x.HasValue ? Math.Round(x.Value).ToString(CultureInfo.InvariantCulture) : "--"; };
            return "CPU " + g("[OHMD:/amdcpu/0/load/0]") + "% " + g("[AIDA:Value.TCPU]") + "°C  GPU " +
                   g("[AIDA:Value.DGPU1]") + "% " + g("[AIDA:Value.TGPU1]") + "°C";
        }
    }

    sealed class InfoForm : Form
    {
        public const string RepoUrl = "https://github.com/chris8510/lcdsysinfo-mini";
        const string PawnIoUrl = "https://pawnio.eu";

        static bool PawnIoInstalled()
        {
            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
                return k != null;
        }

        static bool IsAdmin()
        {
            var p = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
            return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        public InfoForm()
        {
            Text = "Info – LCDSysInfo Mini";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);
            Font = SystemFonts.MessageBoxFont;

            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            Controls.Add(flow);
            Func<string, bool, Label> label = (t, bold) =>
            {
                var l = new Label { Text = t, UseMnemonic = false, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 6) };
                if (bold) l.Font = new Font(Font, FontStyle.Bold);
                flow.Controls.Add(l);
                return l;
            };
            Action<string, string> link = (t, url) =>
            {
                var l = new LinkLabel { Text = t, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
                l.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception) { } };
                flow.Controls.Add(l);
            };

            label("LCDSysInfo Mini " + typeof(InfoForm).Assembly.GetName().Version.ToString(3), true);
            label(Tr.T("Schlanke Statusanzeige für das Coldtears LCDSysInfo 3.5 (USB) – Ersatz für GOverlay.",
                       "Lightweight status display for the Coldtears LCDSysInfo 3.5 (USB) – a replacement for GOverlay."), false);
            label(Tr.T("Quellcode und Anleitung auf GitHub:", "Source code and instructions on GitHub:"), false);
            link(RepoUrl, RepoUrl);
            label(Tr.T("Erstellt von Claude (Anthropic) & chris8510", "Created by Claude (Anthropic) & chris8510"), false);
            label(Tr.T("Voraussetzungen", "Requirements"), true);
            label(Tr.T("• PawnIO-Treiber (separat installieren) – LibreHardwareMonitor braucht ihn für CPU-Temperatur und Lüfter.",
                       "• PawnIO driver (install separately) – LibreHardwareMonitor needs it for CPU temperature and fans."), false);
            link(PawnIoUrl, PawnIoUrl);
            label(Tr.T("• LibreHardwareMonitor – als Bibliothek enthalten, keine eigene Installation nötig.",
                       "• LibreHardwareMonitor – included as a library, no separate installation needed."), false);
            label(Tr.T("• USB-Treiber libusb-win32 für das Display – wird mit GOverlay bzw. dem LCDSysInfo-Treiber installiert.",
                       "• libusb-win32 USB driver for the display – installed with GOverlay or the LCDSysInfo driver."), false);
            label(Tr.T("• Start mit Adminrechten (Autostart-Aufgabe), sonst fehlen Temperaturen und Lüfter.",
                       "• Run with admin rights (autostart task), otherwise temperatures and fans are missing."), false);
            label("Status: PawnIO " + (PawnIoInstalled() ? Tr.T("gefunden", "found") : Tr.T("NICHT gefunden", "NOT found")) +
                  ", " + Tr.T("Adminrechte ", "admin rights ") + (IsAdmin() ? Tr.T("ja", "yes") : Tr.T("nein", "no")), true);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            flow.Controls.Add(ok);
            ok.Click += delegate { Close(); };
            AcceptButton = CancelButton = ok;
        }
    }

    sealed class TrayApp : ApplicationContext
    {
        const string GOverlayDir = @"C:\Program Files (x86)\GOverlay";
        readonly NotifyIcon icon;
        readonly Worker worker;
        readonly string settingsPath;
        readonly System.Windows.Forms.Timer tooltipTimer;

        public TrayApp(string templatePath, string dir)
        {
            settingsPath = Path.Combine(dir, "LCDSysInfoMini.ini");
            if (templatePath == null && File.Exists(settingsPath))
            {
                string saved = File.ReadAllText(settingsPath).Trim();
                if (File.Exists(saved)) templatePath = saved;
            }

            worker = new Worker(templatePath, Path.Combine(dir, "sensors.txt"));
            var menu = new ContextMenuStrip();
            menu.Items.Add(Tr.T("Neu zeichnen", "Redraw"), null, delegate { worker.Redraw(); });
            menu.Items.Add(Tr.T("Vorlage wählen ...", "Choose template ..."), null, delegate { ChooseTemplate(); });
            menu.Items.Add(Tr.T("Log öffnen", "Open log"), null, delegate { OpenFile(Log.PathName); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Info ...", null, delegate { ShowInfo(); });
            menu.Items.Add(Tr.T("Beenden", "Exit"), null, delegate { Quit(); });

            icon = new NotifyIcon { Icon = MakeIcon(), Text = "LCDSysInfo Mini", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += delegate { worker.Redraw(); };
            tooltipTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            tooltipTimer.Tick += delegate
            {
                string t = "LCDSysInfo Mini\n" + worker.Status;
                icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            };
            tooltipTimer.Start();
            if (string.IsNullOrEmpty(worker.TemplatePath)) ChooseTemplate();   // first start
            worker.Start();
            Log.Write(Tr.T("Gestartet, Vorlage: ", "Started, template: ") + (worker.TemplatePath ?? "-"));
        }

        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.FillRectangle(Brushes.Black, 0, 2, 16, 12);
                    g.FillRectangle(Brushes.White, 1, 3, 14, 10);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(0, 170, 255)), 3, 8, 3, 3);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(0, 170, 255)), 7, 6, 3, 5);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(0, 170, 255)), 11, 4, 2, 7);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        void ChooseTemplate()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = Tr.T("GOverlay-Vorlage (*.gov)|*.gov", "GOverlay template (*.gov)|*.gov"),
                Title = Tr.T("Vorlage wählen", "Choose template"),
            })
            {
                string start = string.IsNullOrEmpty(worker.TemplatePath) ? GOverlayDir : Path.GetDirectoryName(worker.TemplatePath);
                if (Directory.Exists(start)) dlg.InitialDirectory = start;
                if (dlg.ShowDialog() != DialogResult.OK) return;
                worker.TemplatePath = dlg.FileName;
                File.WriteAllText(settingsPath, dlg.FileName);
                Log.Write(Tr.T("Vorlage gewechselt: ", "Template changed: ") + dlg.FileName);
                worker.Redraw();
            }
        }

        InfoForm info;

        void ShowInfo()
        {
            if (info != null && !info.IsDisposed) { info.Activate(); return; }
            info = new InfoForm();
            info.Show();
        }

        static void OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
        }

        void Quit()
        {
            tooltipTimer.Stop();
            icon.Visible = false;
            worker.Stop();
            Log.Write(Tr.T("Beendet", "Exited"));
            ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Log.PathName = Path.Combine(dir, "LCDSysInfoMini.log");
            bool created;
            using (var mutex = new Mutex(true, @"Global\LCDSysInfoMini", out created))
            {
                if (!created) return;                       // already running
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => Log.Write("UI: " + e.Exception);
                string tpl = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
                Application.Run(new TrayApp(tpl, dir));
            }
        }
    }
}
