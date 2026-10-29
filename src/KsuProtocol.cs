using System;
using System.Text;

namespace KsuConfigurator
{
    // Транспорт и раскладка feature-отчёта параметров КСУ.
    // Всё восстановлено по оригиналу: Pack = func_402DE0, UnPack = func_403044,
    // HidD_SetFeature = func_4020F0, HidD_GetFeature = func_402128 (KCY_UM.exe).
    internal static class KsuProtocol
    {
        public const byte ReportId = 0x03;          // mov byte ptr [esi], 3      @0x402E04
        public const int ReportLength = 0x80;       // memset(esi, 0, 0x80)        @0x402DF7

        public const int VersionLoOffset = 0x01;    // только чтение: Pack эти байты не заполняет
        public const int VersionHiOffset = 0x02;
        public const int DevIdOffset = 0x03;        // lstrcpyn(esi+3, Edit3, min(8, len)) @0x402E94
        public const int DevIdLength = 8;
        public const int AutoRepeatOffset = 0x0B;   // CSpinEdit1 -> [esi+0x0b]     @0x402EDF
        public const int FlagsOffset = 0x0C;        // CSpinEdit2 | чекбоксы<<4     @0x402EED
        public const int WakeIntervalMask = 0x0F;
        public const int FormatOffset = 0x10;       // lstrcpyn(esi+0x10, Edit, <=0x70) @0x402FF2
        public const int FormatLength = 0x70;       // 112 байт, сравнение cmp eax, 0x70 @0x402F96

        // Кодировка строковых полей устройства и KCY_UM.ini — cp1251.
        public static readonly Encoding Cp1251 = Encoding.GetEncoding(1251);
    }

    // Биты 4..7 байта 0x0C. Разборка RTTI-таблицы полей TForm1 (.data 0x4A2AA0) и код
    // Pack/UnPack дают однозначное соответствие имя контролы -> бит:
    //   CheckBox1 (+0x350) "Запретить вывод в RS232"                       -> 0x10
    //   CheckBox2 (+0x344) "Запретить вывод на клавиатуру"                 -> 0x20
    //   CheckBox3 (+0x348) "Запретить будить компьютер (из ждущего режима)" -> 0x40
    //   CheckBox4 (+0x34C) "Экономия потребления (за счёт частоты опроса)"  -> 0x80
    [Flags]
    internal enum KsuFlags
    {
        None = 0,
        ForbidRs232 = 0x10,
        ForbidKeyboard = 0x20,
        ForbidWake = 0x40,
        PowerSave = 0x80,
    }

    internal sealed class KsuParams
    {
        public string DeviceId = string.Empty;
        public int AutoRepeat;         // 0..255, шаг 0,1 с, 0 — выкл
        public int WakeInterval;       // 0..15, шаг 0,25 с
        public KsuFlags Flags = KsuFlags.None;
        public string Format = string.Empty;
        public int VersionLo;          // read-only
        public int VersionHi;          // read-only

        public string VersionText { get { return VersionHi + "." + VersionLo; } }

        public byte[] ToReport()
        {
            byte[] r = new byte[KsuProtocol.ReportLength];
            r[0] = KsuProtocol.ReportId;
            PutFixed(r, KsuProtocol.DevIdOffset, DeviceId, KsuProtocol.DevIdLength);
            r[KsuProtocol.AutoRepeatOffset] = (byte)AutoRepeat;
            r[KsuProtocol.FlagsOffset] = (byte)((WakeInterval & KsuProtocol.WakeIntervalMask) | (int)Flags);
            PutFixed(r, KsuProtocol.FormatOffset, Format, KsuProtocol.FormatLength);
            return r;
        }

        public static KsuParams FromReport(byte[] r)
        {
            if (r == null || r.Length < KsuProtocol.ReportLength)
                throw new ArgumentException("нужно " + KsuProtocol.ReportLength + " байт, получено " +
                    (r == null ? 0 : r.Length));
            KsuParams p = new KsuParams();
            p.DeviceId = ReadFixed(r, KsuProtocol.DevIdOffset, KsuProtocol.DevIdLength);
            p.AutoRepeat = r[KsuProtocol.AutoRepeatOffset];
            p.WakeInterval = r[KsuProtocol.FlagsOffset] & KsuProtocol.WakeIntervalMask;
            p.Flags = (KsuFlags)(r[KsuProtocol.FlagsOffset] & 0xF0);
            p.Format = ReadFixed(r, KsuProtocol.FormatOffset, KsuProtocol.FormatLength);
            p.VersionLo = r[KsuProtocol.VersionLoOffset];
            p.VersionHi = r[KsuProtocol.VersionHiOffset];
            return p;
        }

        private static void PutFixed(byte[] buf, int offset, string value, int length)
        {
            for (int i = offset; i < offset + length; i++) buf[i] = 0;
            if (string.IsNullOrEmpty(value)) return;
            byte[] bytes = KsuProtocol.Cp1251.GetBytes(value);
            int n = Math.Min(bytes.Length, length);
            Array.Copy(bytes, 0, buf, offset, n);
        }

        private static string ReadFixed(byte[] buf, int offset, int length)
        {
            int end = offset + length;
            int i = offset;
            while (i < end && buf[i] != 0) i++;
            return KsuProtocol.Cp1251.GetString(buf, offset, i - offset);
        }
    }
}
