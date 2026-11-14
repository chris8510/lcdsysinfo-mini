// LibreHardwareMonitor sensor source and the mapping from GOverlay (AIDA64/OHMD) sensor ids.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace LcdSysInfoMini
{
    sealed class Sensors : IDisposable
    {
        // Seconds between hardware updates; slowly changing sources are polled less often.
        static readonly Dictionary<HardwareType, double> Intervals = new Dictionary<HardwareType, double>
        {
            { HardwareType.Storage, 10 }, { HardwareType.Motherboard, 5 }, { HardwareType.SuperIO, 5 },
        };

        readonly Computer computer;
        readonly Dictionary<string, double> values = new Dictionary<string, double>();
        readonly Dictionary<string, double> lastUpdate = new Dictionary<string, double>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        HashSet<string> only;                       // hardware ids to poll, null = all
        public readonly HashSet<string> UsedKeys = new HashSet<string>();

        public Sensors()
        {
            computer = new Computer
            {
                IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
                IsMotherboardEnabled = true, IsStorageEnabled = true,
            };
            computer.Open();
        }

        public void Dispose() { computer.Close(); }

        static IEnumerable<IHardware> Walk(IHardware hw)
        {
            yield return hw;
            foreach (IHardware sub in hw.SubHardware)
                foreach (IHardware x in Walk(sub)) yield return x;
        }

        IEnumerable<IHardware> AllHardware() { return computer.Hardware.SelectMany(Walk); }

        public void Poll()
        {
            double now = clock.Elapsed.TotalSeconds;
            foreach (IHardware hw in AllHardware())
            {
                string key = hw.Identifier.ToString();
                if (only != null && !only.Contains(key)) continue;
                double interval, last;
                if (!Intervals.TryGetValue(hw.HardwareType, out interval)) interval = 0;
                if (lastUpdate.TryGetValue(key, out last) && now - last < interval) continue;
                hw.Update();
                lastUpdate[key] = now;
                foreach (ISensor s in hw.Sensors)
                    if (s.Value.HasValue) values[s.Identifier.ToString()] = s.Value.Value;
            }
        }

        /// <summary>Poll only hardware that owns one of the sensors the template reads.</summary>
        public IList<string> RestrictToUsed()
        {
            only = new HashSet<string>(AllHardware()
                .Where(hw => hw.Sensors.Any(s => UsedKeys.Contains(s.Identifier.ToString())))
                .Select(hw => hw.Identifier.ToString()));
            return only.OrderBy(x => x).ToList();
        }

        public void WriteInventory(string path)
        {
            var sb = new StringBuilder();
            foreach (IHardware hw in AllHardware())
            {
                hw.Update();
                foreach (ISensor s in hw.Sensors)
                    sb.AppendLine(string.Join(" | ", hw.HardwareType, hw.Name, s.SensorType, s.Name, s.Identifier,
                                              s.Value.HasValue ? s.Value.Value.ToString() : ""));
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ---- mapping -------------------------------------------------------
        readonly Dictionary<string, Regex> regexCache = new Dictionary<string, Regex>();
        double? Lhm(params string[] patterns)
        {
            List<string> keys = values.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            foreach (string p in patterns)
            {
                Regex r;
                if (!regexCache.TryGetValue(p, out r)) { r = new Regex("^(?:" + p + ")$"); regexCache[p] = r; }
                foreach (string k in keys)
                    if (r.IsMatch(k)) { UsedKeys.Add(k); return values[k]; }
            }
            return null;
        }

        static double? DiskFreeGb(string letter)
        {
            try
            {
                DriveInfo d = new DriveInfo(letter);
                return d.IsReady ? d.AvailableFreeSpace / 1073741824.0 : (double?)null;
            }
            catch (Exception) { return null; }
        }

        const string Gpu = "/gpu-(amd|nvidia|intel)/0/";

        double? Resolve(string id)
        {
            Match m;
            switch (id)
            {
                case "[AIDA:Value.SMEMUTI]": return Lhm("/ram/load/0");
                case "[AIDA:Value.DGPU1]": return Lhm(Gpu + "load/0");          // shown as "GPU %": GPU core load
                case "[AIDA:Value.TCPU]": return Lhm("/amdcpu/0/temperature/2", @"/amdcpu/0/temperature/\d+", @"/intelcpu/0/temperature/\d+");
                case "[AIDA:Value.TGPU1]": return Lhm(Gpu + "temperature/0");
                case "[AIDA:Value.FCPU]": return Lhm("/lpc/[^/]+/0/fan/[01]", "/lpc/[^/]+/fan/[01]");
                case "[AIDA:Value.FGPU1]": return Lhm(Gpu + "fan/0");
            }
            if ((m = Regex.Match(id, @"^\[AIDA:Value\.THDD(\d+)\]$")).Success)   // AIDA counts drives from 1
                return Lhm("/(hdd|nvme|ssd|storage)/" + (int.Parse(m.Groups[1].Value) - 1) + "/temperature/0");
            if ((m = Regex.Match(id, @"^\[AIDA:Value\.SDRV([A-Z])FREESPC\]$")).Success)
                return DiskFreeGb(m.Groups[1].Value + ":\\");
            if ((m = Regex.Match(id, @"^\[OHMD:(.+)\]$")).Success)
            {
                string path = m.Groups[1].Value;
                return path == "/amdcpu/0/load/0" ? Lhm(path, "/intelcpu/0/load/0") : Lhm(Regex.Escape(path));
            }
            return null;
        }

        public Dictionary<string, double?> Read(IEnumerable<string> ids)
        {
            Poll();
            var result = new Dictionary<string, double?>();
            foreach (string id in ids)
            {
                double? v = Resolve(id);
                bool isTemp = id.Contains("Value.T") || id.Contains("/temperature/");
                result[id] = (v.HasValue && v.Value == 0.0 && isTemp) ? null : v;   // 0.0 = unreadable without driver
            }
            return result;
        }
    }
}
