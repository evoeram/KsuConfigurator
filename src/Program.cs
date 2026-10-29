using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace KsuConfigurator
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.Exit(RunCaptured("selftest.log", new Func<int>(() => SelfTest.Run())));
                }
                if (string.Equals(args[i], "--dump", StringComparison.OrdinalIgnoreCase))
                {
                    // Только чтение: HidD_GetFeature ничего не пишет во flash считывателя.
                    Environment.Exit(RunCaptured("dump.log", new Func<int>(() => { DeviceDump.Run(); return 0; })));
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        // exe собирается как winexe (без консоли), поэтому вывод дублируется в файл.
        private static int RunCaptured(string logName, Func<int> body)
        {
            System.Text.StringBuilder capture = new System.Text.StringBuilder();
            System.IO.TextWriter previous = Console.Out;
            Console.SetOut(new System.IO.StringWriter(capture));
            int code;
            try
            {
                code = body();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ИСКЛЮЧЕНИЕ: " + ex);
                code = 2;
            }
            Console.SetOut(previous);
            string text = capture.ToString();
            Console.Write(text);
            System.IO.File.WriteAllText(System.IO.Path.Combine(Application.StartupPath, logName),
                text, System.Text.Encoding.UTF8);
            return code;
        }
    }

    // KsuConfigurator.exe --selftest
    // Проверки, не требующие считывателя: раскладка отчёта, round-trip, cp1251, валидатор DSL,
    // и что HidP_GetCaps по имени действительно возвращает размеры отчётов на этом Windows.
    internal static class SelfTest
    {
        private static int _failed;

        public static int Run()
        {
            CheckReportLayout();
            CheckRoundTrip();
            CheckCp1251();
            CheckValidator();
            CheckRealInI();
            CheckGetCapsByName();
            Console.WriteLine(_failed == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string what)
        {
            Console.WriteLine((condition ? "  ok   " : "  FAIL ") + what);
            if (!condition) _failed++;
        }

        private static void CheckReportLayout()
        {
            Console.WriteLine("[1] раскладка feature-отчёта 0x03");
            KsuParams p = new KsuParams();
            p.DeviceId = "0A12";
            p.AutoRepeat = 7;
            p.WakeInterval = 5;
            p.Flags = KsuFlags.ForbidKeyboard | KsuFlags.PowerSave;
            p.Format = "%7W%U";
            byte[] r = p.ToReport();

            Check(r.Length == 0x80, "длина отчёта 0x80");
            Check(r[0] == 0x03, "байт 0 = ID 0x03");
            Check(r[3] == (byte)'0' && r[4] == (byte)'A' && r[5] == (byte)'1' && r[6] == (byte)'2', "DevID в 0x03..0x06");
            Check(r[7] == 0, "остаток DevID обнулён");
            Check(r[0x0B] == 7, "автоповтор в 0x0B");
            Check(r[0x0C] == (5 | 0x20 | 0x80), "байт 0x0C = побудка|флаги (0x" + (5 | 0x20 | 0x80).ToString("X2") + ")");
            Check(r[0x10] == (byte)'%' && r[0x11] == (byte)'7', "формат начинается с 0x10");
            Check(r[0x7F] == 0, "хвост поля формата обнулён");
        }

        private static void CheckRoundTrip()
        {
            Console.WriteLine("[2] round-trip Pack -> UnPack");
            KsuParams p = new KsuParams();
            p.DeviceId = "Prox-01";
            p.AutoRepeat = 250;
            p.WakeInterval = 15;
            p.Flags = KsuFlags.ForbidRs232 | KsuFlags.ForbidWake;
            p.Format = "%R%001l,V%l,U%000l,V%C00%U01";
            KsuParams q = KsuParams.FromReport(p.ToReport());
            Check(q.DeviceId == p.DeviceId, "серийник: " + q.DeviceId);
            Check(q.AutoRepeat == p.AutoRepeat, "автоповтор: " + q.AutoRepeat);
            Check(q.WakeInterval == p.WakeInterval, "побудка: " + q.WakeInterval);
            Check(q.Flags == p.Flags, "флаги: 0x" + ((int)q.Flags).ToString("X2"));
            Check(q.Format == p.Format, "формат: " + q.Format);
            Check(q.VersionText == "0.0", "версия читается из 0x01/0x02");
        }

        private static void CheckCp1251()
        {
            Console.WriteLine("[3] cp1251 в строковых полях");
            KsuParams p = new KsuParams();
            p.Format = "Идентификатор считывателя \"%gI\"";
            byte[] r = p.ToReport();
            Check(r[0x10] == 0xC8 && r[0x11] == 0xE4, "кириллица идёт байтами cp1251 (" + r[0x10].ToString("X2") + " " + r[0x11].ToString("X2") + ")");
            Check(KsuParams.FromReport(r).Format == p.Format, "кириллица восстанавливается");
            string long64 = new string('а', 120);
            Check(FormatString.ByteLength(long64) == 120 && FormatString.ByteLength(long64) > KsuProtocol.FormatLength,
                "120 букв 'а' = 120 байт > 112");
        }

        private static void CheckValidator()
        {
            Console.WriteLine("[4] валидатор строки формата");
            string[] good =
            {
                "%7W%U",
                "DevID \"%gI\"; Card Type \"%f.T\"; Card UID Len %f.L bytes; Card UID \"%U\";",
                "%R%001l,V%l,U%000l,V%C00%U01",
                "%R%001l,V%02l,U%000l,V%000l,V%000l,V%C000000%02U01   // DS1990, 3 bytes",
                "%20U %01f.U",
                "%lW%03U\t\t\t// Legos?",
                "текст\\t\\r\\n%%\\\\",
            };
            foreach (string s in good)
            {
                List<string> messages = FormatString.Validate(s);
                int errors = messages.TrueCount("ошибка");
                Check(errors == 0, "принимается: " + Cut(s));
                foreach (string m in messages) Console.WriteLine("         " + m);
            }

            Check(FormatString.Validate("%Q").TrueCount("ошибка") == 1, "отклоняется: %Q (неизвестный выводимый символ)");
            Check(FormatString.Validate("abc%").TrueCount("ошибка") == 1, "отклоняется: висящий '%'");
            Check(FormatString.Validate(new string('%', 1) + new string('U', 200)).TrueCount("ошибка") >= 1,
                "отклоняется: слишком длинная строка");
        }

        private static void CheckRealInI()
        {
            Console.WriteLine("[5] реальный KCY_UM.ini рядом с exe");
            string ini = System.IO.Path.Combine(Application.StartupPath, "KCY_UM.ini");
            if (!System.IO.File.Exists(ini)) { Console.WriteLine("  (нет файла — пропущено)"); return; }
            List<string> presets = KsuIni.ReadPresets(ini);
            Console.WriteLine("  заготовок: " + presets.Count);
            foreach (string s in presets)
            {
                int errors = FormatString.Validate(s).TrueCount("ошибка");
                Console.WriteLine("  [" + (errors == 0 ? "ok " : "err") + "] " + Cut(s));
            }
        }

        private static void CheckGetCapsByName()
        {
            Console.WriteLine("[6] HID-стек этого Windows (поиск по имени, не по ординалу)");
            Guid hidGuid;
            NativeHid.HidD_GetHidGuid(out hidGuid);
            Check(hidGuid != Guid.Empty, "HidD_GetHidGuid -> " + hidGuid);

            List<HidDevice> devices = HidDevice.Enumerate();
            Console.WriteLine("  HID-интерфейсов: " + devices.Count);
            int withCaps = 0;
            foreach (HidDevice d in devices)
            {
                if (d.InputReportByteLength > 0) withCaps++;
                if (d.IsKsu) Console.WriteLine("  найден КСУ: " + d.Description);
                d.Dispose();
            }
            Check(devices.Count == 0 || withCaps > 0,
                "HidP_GetCaps по имени заполняет размеры отчётов (" + withCaps + " из " + devices.Count + ")");
        }

        private static string Cut(string s)
        {
            s = s.Replace("\n", "\\n").Replace("\r", "\\r");
            return s.Length <= 78 ? s : s.Substring(0, 78) + "...";
        }
    }

    // KsuConfigurator.exe --dump — посмотреть, чем система отвечает на HID-запросы и что
    // сейчас записано в параметрах считывателя (только чтение).
    internal static class DeviceDump
    {
        public static void Run()
        {
            List<HidDevice> devices = HidDevice.Enumerate();
            Console.WriteLine("HID-интерфейсов: " + devices.Count);
            foreach (HidDevice d in devices)
                Console.WriteLine("  " + d.Description);

            foreach (HidDevice d in devices)
            {
                if (!d.IsKsu) continue;
                Console.WriteLine();
                Console.WriteLine("=== " + d.Path);
                byte[] report = new byte[KsuProtocol.ReportLength];
                report[0] = KsuProtocol.ReportId;
                string error;
                if (!d.GetFeature(report, out error))
                {
                    Console.WriteLine("HidD_GetFeature не прошла: " + error);
                    continue;
                }
                Console.WriteLine("feature 0x03 (" + KsuProtocol.ReportLength + " байт):");
                Console.WriteLine(HidDevice.ToHex(report));
                KsuParams p = KsuParams.FromReport(report);
                Console.WriteLine("серийник: \"" + p.DeviceId + "\"");
                Console.WriteLine("автоповтор: " + p.AutoRepeat + " (шаг 0,1 с)");
                Console.WriteLine("интервал побудки: " + p.WakeInterval + " (шаг 0,25 с)");
                Console.WriteLine("флаги: 0x" + ((int)p.Flags).ToString("X2") +
                    "  RS232=" + ((p.Flags & KsuFlags.ForbidRs232) != 0) +
                    " клавиатура=" + ((p.Flags & KsuFlags.ForbidKeyboard) != 0) +
                    " побудка=" + ((p.Flags & KsuFlags.ForbidWake) != 0) +
                    " экономия=" + ((p.Flags & KsuFlags.PowerSave) != 0));
                Console.WriteLine("версия: " + p.VersionText);
                Console.WriteLine("формат: " + p.Format);
                foreach (string m in FormatString.Validate(p.Format)) Console.WriteLine("    " + m);
            }
            foreach (HidDevice d in devices) d.Dispose();
        }
    }

    internal static class ListExtensions
    {
        public static int TrueCount(this List<string> list, string startsWith)
        {
            int n = 0;
            foreach (string s in list) if (s.StartsWith(startsWith)) n++;
            return n;
        }
    }
}
