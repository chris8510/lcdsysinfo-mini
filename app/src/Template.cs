// Renders a GOverlay LCDSysInfo 3.5 template (*.gov) with the device's own primitives.
// Approximated (not reconstructed exactly): maxdigits, font_fixedspacing, t1_* colour thresholds.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LcdSysInfoMini
{
    sealed class FontMetrics
    {
        readonly Dictionary<char, int> widths = new Dictionary<char, int>();

        public FontMetrics(string info)
        {
            string[] f = info.Split(',');
            for (int k = 2; k < f.Length; k++)     // field 2 is ' ', field k is (char)(0x1E + k)
            {
                int w;
                if (int.TryParse(f[k].Replace("#", ""), out w)) widths[(char)(0x1E + k)] = w;
            }
        }

        public int TextWidth(string s)
        {
            int def = widths.Count > 0 ? widths.Values.Max() : 10;
            int sum = 0;
            foreach (char c in s) { int w; sum += widths.TryGetValue(c, out w) ? w : def; }
            return sum;
        }
    }

    sealed class Template
    {
        public readonly List<Dictionary<string, string>> Items = new List<Dictionary<string, string>>();
        public readonly Dictionary<string, string> Config = new Dictionary<string, string>();

        public Template(string path)
        {
            var elements = new Dictionary<string, Dictionary<string, string>>();
            var order = new List<string>();
            foreach (string line in File.ReadAllLines(path, Encoding.GetEncoding(28591)))
            {
                if (line.StartsWith("--------------------FILE START")) break;   // embedded blobs follow
                int comma = line.IndexOf(',');
                if (comma < 0) continue;
                string kind = line.Substring(0, comma);
                string[] p = line.Substring(comma + 1).Split(new[] { '=' }, 3);
                if (p.Length < 3 || p[0].Length == 0) continue;
                if (kind == "1")
                {
                    Dictionary<string, string> e;
                    if (!elements.TryGetValue(p[0], out e)) { e = new Dictionary<string, string>(); elements[p[0]] = e; order.Add(p[0]); }
                    e[p[1]] = p[2];
                }
                else if (kind == "2" && p[0] == "config") Config[p[1]] = p[2];
            }
            foreach (string id in order)
            {
                var e = elements[id];
                if (e.ContainsKey("type") && Get(e, "disabled", "Enabled") == "Enabled") Items.Add(e);
            }
        }

        public static string Get(Dictionary<string, string> e, string key, string def)
        {
            string v;
            return e.TryGetValue(key, out v) ? v : def;
        }

        public static int Int(Dictionary<string, string> e, string key, int def)
        {
            double d;
            string v = Get(e, key, "");
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (int)d : def;
        }

        public IEnumerable<string> SensorIds
        {
            get
            {
                return Items.Where(e => e["type"] == "sensor" && Get(e, "sensor_id", "") != "")
                            .Select(e => e["sensor_id"]).Distinct();
            }
        }
    }

    sealed class Renderer
    {
        readonly Lcd lcd;
        readonly Dictionary<string, FileEntry> files;
        readonly Template tpl;
        readonly Dictionary<string, FontMetrics> metrics = new Dictionary<string, FontMetrics>();
        readonly Dictionary<int, string> shown = new Dictionary<int, string>();
        public readonly HashSet<string> Missing = new HashSet<string>();

        public Renderer(Lcd lcd, Dictionary<string, FileEntry> files, Template tpl)
        {
            this.lcd = lcd; this.files = files; this.tpl = tpl;
        }

        void DrawText(Dictionary<string, string> e, string s, bool clearBox)
        {
            int x = Template.Int(e, "x", 0), y = Template.Int(e, "y", 0);
            int w = Template.Int(e, "width", 0), h = Template.Int(e, "height", 0);
            bool bgTrans = Template.Get(e, "font_bgtrans", "0") == "1";
            int bg = bgTrans ? Lcd.TransparentBg : Template.Int(e, "font_bgcolor", 0xFFFF);
            if (clearBox && !bgTrans) lcd.Rect(x, y, x + w, y + h, bg, false, 0);
            string fontName = Template.Get(e, "font_style", "");
            FileEntry font;
            if (!files.TryGetValue(fontName, out font)) { Missing.Add(fontName); return; }
            FontMetrics m;
            if (!metrics.TryGetValue(fontName, out m)) { m = new FontMetrics(font.Info); metrics[fontName] = m; }
            int tw = m.TextWidth(s);
            int align = Template.Int(e, "font_alignment", -1);
            int dx = align < 0 ? 0 : (align > 0 ? w - tw : (w - tw) / 2);
            int tx = x + Math.Max(0, dx);
            lcd.TextFont(tx, y, s, font, Template.Int(e, "font_color", 0), bg, x + w - tx);
        }

        public void DrawStatic()
        {
            lcd.Clear(Template.Int(tpl.Config, "backgroundColor", 0xFFFF));
            shown.Clear();
            foreach (var e in tpl.Items)
            {
                switch (e["type"])
                {
                    case "Icon":
                        FileEntry icon;
                        string name = Template.Get(e, "icon", "");
                        if (files.TryGetValue(name, out icon)) lcd.Icon(Template.Int(e, "x", 0), Template.Int(e, "y", 0), icon);
                        else Missing.Add(name);
                        break;
                    case "Text":
                        DrawText(e, Template.Get(e, "text", ""), false);
                        break;
                    case "Rectangle":
                        int x = Template.Int(e, "x", 0), y = Template.Int(e, "y", 0);
                        bool filled = Template.Get(e, "filled", "1") == "1";
                        int radius = Template.Get(e, "rounded", "0") == "1" ? Template.Int(e, "roundradius", 0) : 0;
                        lcd.Rect(x, y, x + Template.Int(e, "width", 0), y + Template.Int(e, "height", 0),
                                 Template.Int(e, "color", 0), !filled, radius);
                        break;
                }
            }
        }

        public static string Format(Dictionary<string, string> e, double? value)
        {
            string s;
            if (!value.HasValue) s = "--";
            else
            {
                double v = value.Value;
                int conv = Template.Int(e, "convertpercent", 0);
                if (conv > 0) v = v * 100.0 / conv;
                long n = (long)Math.Floor(v + 0.5);
                int digits = Template.Int(e, "maxdigits", 0);
                if (digits > 0 && Template.Get(e, "maxdigits_strict", "0") == "1") n = Math.Min(n, (long)Math.Pow(10, digits) - 1);
                s = n.ToString(CultureInfo.InvariantCulture);
            }
            return Template.Get(e, "format", "[s]").Replace("[s]", s);
        }

        public void Update(Dictionary<string, double?> values)
        {
            for (int i = 0; i < tpl.Items.Count; i++)
            {
                var e = tpl.Items[i];
                if (e["type"] != "sensor") continue;
                double? v;
                values.TryGetValue(Template.Get(e, "sensor_id", ""), out v);
                string s = Format(e, v);
                string old;
                if (shown.TryGetValue(i, out old) && old == s) continue;
                DrawText(e, s, true);
                shown[i] = s;
            }
        }
    }
}
