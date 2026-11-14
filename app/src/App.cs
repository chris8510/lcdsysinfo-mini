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
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

namespace LcdSysInfoMini
{
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
        public volatile string Status = "Startet ...";
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
                try { sensors.WriteInventory(inventoryPath); } catch (Exception ex) { Log.Write("Sensorliste: " + ex.Message); }
                bool restricted = false;
                while (!stop)
                {
                    try
                    {
                        using (Lcd lcd = Lcd.Open())
                        {
                            var files = lcd.ReadFileTable();
                            var tpl = new Template(TemplatePath);
                            var r = new Renderer(lcd, files, tpl);
                            r.DrawStatic();
                            redraw = false;
                            if (r.Missing.Count > 0) Log.Write("Fehlt auf dem Gerät: " + string.Join(", ", r.Missing));
                            Log.Write("Vorlage gezeichnet: " + Path.GetFileName(TemplatePath));
                            List<string> ids = tpl.SensorIds.ToList();
                            bool first = true;
                            while (!stop && !redraw)
                            {
                                var values = sensors.Read(ids);
                                r.Update(values);
                                Status = StatusText(values);
                                if (first)
                                {
                                    Log.Write("Werte: " + string.Join(", ", values.Select(kv => kv.Key + "=" + Fmt(kv.Value))));
                                    first = false;
                                    if (!restricted)
                                    {
                                        Log.Write("Abgefragte Hardware: " + string.Join(", ", sensors.RestrictToUsed()));
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
                        Status = "Display nicht erreichbar";
                        Log.Write("Display nicht erreichbar, neuer Versuch in 5 s: " + ex.Message);
                        Sleep(5.0);
                    }
                }
            }
            catch (Exception ex)
            {
                Status = "Fehler: " + ex.Message;
                Log.Write("Abbruch: " + ex);
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
            label("Schlanke Statusanzeige für das Coldtears LCDSysInfo 3.5 (USB) – Ersatz für GOverlay.", false);
            label("Quellcode und Anleitung auf GitHub:", false);
            link(RepoUrl, RepoUrl);
            label("Erstellt von Claude (Anthropic) & chris8510", false);
            label("Voraussetzungen", true);
            label("• PawnIO-Treiber (separat installieren) – LibreHardwareMonitor braucht ihn für CPU-Temperatur und Lüfter.", false);
            link(PawnIoUrl, PawnIoUrl);
            label("• LibreHardwareMonitor – als Bibliothek enthalten, keine eigene Installation nötig.", false);
            label("• USB-Treiber libusb-win32 für das Display – wird mit GOverlay bzw. dem LCDSysInfo-Treiber installiert.", false);
            label("• Start mit Adminrechten (Autostart-Aufgabe), sonst fehlen Temperaturen und Lüfter.", false);
            label("Status: PawnIO " + (PawnIoInstalled() ? "gefunden" : "NICHT gefunden") +
                  ", Adminrechte " + (IsAdmin() ? "ja" : "nein"), true);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            flow.Controls.Add(ok);
            ok.Click += delegate { Close(); };
            AcceptButton = CancelButton = ok;
        }
    }

    sealed class TrayApp : ApplicationContext
    {
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
            if (templatePath == null) templatePath = @"C:\Program Files (x86)\GOverlay\mystylen1 (LCD2).gov";

            worker = new Worker(templatePath, Path.Combine(dir, "sensors.txt"));
            var menu = new ContextMenuStrip();
            menu.Items.Add("Neu zeichnen", null, delegate { worker.Redraw(); });
            menu.Items.Add("Vorlage wählen ...", null, delegate { ChooseTemplate(); });
            menu.Items.Add("Log öffnen", null, delegate { OpenFile(Log.PathName); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Info ...", null, delegate { ShowInfo(); });
            menu.Items.Add("Beenden", null, delegate { Quit(); });

            icon = new NotifyIcon { Icon = MakeIcon(), Text = "LCDSysInfo Mini", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += delegate { worker.Redraw(); };
            tooltipTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            tooltipTimer.Tick += delegate
            {
                string t = "LCDSysInfo Mini\n" + worker.Status;
                icon.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            };
            tooltipTimer.Start();
            if (!File.Exists(worker.TemplatePath)) ChooseTemplate();   // first start on another PC
            worker.Start();
            Log.Write("Gestartet, Vorlage: " + worker.TemplatePath);
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
            using (var dlg = new OpenFileDialog { Filter = "GOverlay-Vorlage (*.gov)|*.gov", Title = "Vorlage wählen" })
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(worker.TemplatePath); } catch (ArgumentException) { }
                if (dlg.ShowDialog() != DialogResult.OK) return;
                worker.TemplatePath = dlg.FileName;
                File.WriteAllText(settingsPath, dlg.FileName);
                Log.Write("Vorlage gewechselt: " + dlg.FileName);
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
            Log.Write("Beendet");
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
                Application.ThreadException += (s, e) => Log.Write("UI-Fehler: " + e.Exception);
                string tpl = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
                Application.Run(new TrayApp(tpl, dir));
            }
        }
    }
}
