// LCDSysInfo 3.5 (USB 20A0:41ED) driver over libusb-win32 (libusb0.dll).
// Protocol reconstructed from GOverlay 1.4.3; only drawing and read commands.
// Flash/EEPROM/firmware commands (14, 15, 16, 28) are deliberately absent.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LcdSysInfoMini
{
    static class LibUsb0
    {
        const string Dll = "libusb0.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void usb_init();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_find_busses();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_find_devices();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr usb_get_busses();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr usb_open(IntPtr dev);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_close(IntPtr h);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_set_configuration(IntPtr h, int configuration);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int usb_control_msg(IntPtr h, int requestType, int request, int value, int index,
                                                 byte[] bytes, int size, int timeout);

        // libusb-win32 structures are packed; PATH_MAX + 1 = 512 on Windows.
        const int PathBytes = 512;
        static readonly int PtrSize = IntPtr.Size;
        static int BusDevicesOffset { get { return 2 * PtrSize + PathBytes; } }
        static int DevDescriptorOffset { get { return 2 * PtrSize + PathBytes + PtrSize; } }

        public static IntPtr FindDevice(int vid, int pid)
        {
            usb_init();
            usb_find_busses();
            usb_find_devices();
            for (IntPtr bus = usb_get_busses(); bus != IntPtr.Zero; bus = Marshal.ReadIntPtr(bus, 0))
            {
                for (IntPtr dev = Marshal.ReadIntPtr(bus, BusDevicesOffset); dev != IntPtr.Zero; dev = Marshal.ReadIntPtr(dev, 0))
                {
                    int v = Marshal.ReadInt16(dev, DevDescriptorOffset + 8) & 0xFFFF;
                    int p = Marshal.ReadInt16(dev, DevDescriptorOffset + 10) & 0xFFFF;
                    if (v == vid && p == pid) return dev;
                }
            }
            return IntPtr.Zero;
        }
    }

    sealed class FileEntry
    {
        public int Sector, Sectors, Size;
        public string Name, Type, Info, Md5;
        public bool Transparent { get { return Info.Contains("(T)"); } }
    }

    sealed class Lcd : IDisposable
    {
        public const int Vid = 0x20A0, Pid = 0x41ED, Width = 480, Height = 320;
        public const int TransparentBg = 12345;       // GOverlay "no background" for flash fonts
        const int IconKeyColor = 0xC217;              // transparency key used with command 193
        const int Timeout = 1000;
        const string Eot = "-------------------<EOT>-----------------";
        IntPtr handle;

        public static Lcd Open()
        {
            IntPtr dev = LibUsb0.FindDevice(Vid, Pid);
            if (dev == IntPtr.Zero) throw new IOException(Tr.T("LCDSysInfo 3.5 nicht gefunden (läuft noch GOverlay?)", "LCDSysInfo 3.5 not found (is GOverlay still running?)"));
            IntPtr h = LibUsb0.usb_open(dev);
            if (h == IntPtr.Zero) throw new IOException(Tr.T("Gerät konnte nicht geöffnet werden", "Could not open the device"));
            LibUsb0.usb_set_configuration(h, 1);      // ignore "already configured"
            Lcd lcd = new Lcd();
            lcd.handle = h;
            return lcd;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { LibUsb0.usb_close(handle); handle = IntPtr.Zero; }
        }

        void Out(int req, int value, int index, byte[] data)
        {
            if (data == null) data = new byte[0];
            int r = LibUsb0.usb_control_msg(handle, 0x40, req, value & 0xFFFF, index & 0xFFFF, data, data.Length, Timeout);
            if (r < 0) throw new IOException("USB-Fehler " + r + " bei Befehl " + req);
        }

        byte[] In(int req, int value, int index, int length)
        {
            byte[] buf = new byte[length];
            int r = LibUsb0.usb_control_msg(handle, 0xC0, req, value & 0xFFFF, index & 0xFFFF, buf, length, Timeout);
            if (r < 0) throw new IOException("USB-Fehler " + r + " bei Lesebefehl " + req);
            if (r < length) Array.Resize(ref buf, r);
            return buf;
        }

        static void Be16(List<byte> b, int v)
        {
            v = Math.Max(0, Math.Min(0xFFFF, v));
            b.Add((byte)(v >> 8)); b.Add((byte)(v & 0xFF));
        }

        public Dictionary<string, FileEntry> ReadFileTable()
        {
            StringBuilder sb = new StringBuilder();
            int maxBlocks = (16 - 1) * 4096 / 128 - 1;
            for (int block = 0; block < maxBlocks; block++)
            {
                sb.Append(Encoding.UTF8.GetString(In(12, block, 4, 128)));
                int tail = Math.Max(0, sb.Length - 300);
                if (sb.ToString(tail, sb.Length - tail).Contains(Eot)) break;
            }
            return ParseFileTable(sb.ToString());
        }

        public static Dictionary<string, FileEntry> ParseFileTable(string text)
        {
            text = text.Replace("\r\n", "").Replace("\0", "");
            int eot = text.IndexOf(Eot, StringComparison.Ordinal);
            if (eot >= 0) text = text.Substring(0, eot);
            Dictionary<string, FileEntry> entries = new Dictionary<string, FileEntry>();
            foreach (string record in text.Split(';'))
            {
                string[] f = record.Split('|');
                int sector, sectors, size;
                if (f.Length < 8 || f[1] == "UPDATES" || !int.TryParse(f[1], out sector)) continue;
                int.TryParse(f[3], out sectors);
                int.TryParse(f[7], out size);
                if (entries.ContainsKey(f[2])) continue;
                entries[f[2]] = new FileEntry { Sector = sector, Name = f[2], Sectors = sectors, Type = f[4], Info = f[5], Md5 = f[6], Size = size };
            }
            return entries;
        }

        public void Clear(int color) { Out(26, 0x3F, color, null); }

        public void Brightness(int level) { level = Math.Max(0, Math.Min(255, level)); Out(13, level, level, null); }

        public void Rect(int x1, int y1, int x2, int y2, int color, bool outline, int radius)
        {
            List<byte> d = new List<byte>(11);
            Be16(d, y1); Be16(d, x1); Be16(d, y2); Be16(d, x2); Be16(d, color);
            int kind;
            if (radius > 0) { d.Add((byte)Math.Min(255, radius)); kind = outline ? 9 : 10; }
            else kind = outline ? 2 : 3;
            Out(191, d.Count, kind, d.ToArray());     // value = data length, index = shape kind
        }

        public void TextFont(int x, int y, string s, FileEntry font, int color, int bg, int width)
        {
            byte[] body = Encoding.ASCII.GetBytes(s + "\r\n");
            List<byte> d = new List<byte>(13 + body.Length);
            Be16(d, x); Be16(d, y); Be16(d, Math.Min(x + width, Width)); Be16(d, 1); Be16(d, color); Be16(d, bg);
            d.AddRange(body);
            d.Add(0);
            Out(190, body.Length, font.Sector, d.ToArray());
        }

        public void Icon(int x, int y, FileEntry e)
        {
            List<byte> d = new List<byte>(4);
            Be16(d, y); Be16(d, x);
            if (e.Transparent) Out(193, e.Sector, IconKeyColor, d.ToArray());
            else Out(29, e.Sector, e.Sector, d.ToArray());
        }
    }
}
