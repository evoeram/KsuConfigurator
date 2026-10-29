using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace KsuConfigurator
{
    // Работа с KCY_UM.ini: секция [FStrings], ключи "0", "1", ... — список заготовок
    // строки формата. Чтение/запись идут через Win32 profile API, как в оригинале
    // (GetPrivateProfileStringA / WritePrivateProfileStringA), поэтому поведение и
    // кодировка (ANSI = cp1251 на русской Windows) совпадают один в один.
    internal static class KsuIni
    {
        public const string Section = "FStrings";

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern uint GetPrivateProfileString(string section, string key, string def,
            StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WritePrivateProfileString(string section, string key, string val, string filePath);

        public static List<string> ReadPresets(string iniPath)
        {
            List<string> list = new List<string>();
            for (int i = 0; ; i++)
            {
                StringBuilder sb = new StringBuilder(1024);
                uint n = GetPrivateProfileString(Section, i.ToString(), null, sb, sb.Capacity, iniPath);
                if (n == 0) break;
                list.Add(sb.ToString());
            }
            return list;
        }

        public static int NextFreeIndex(string iniPath)
        {
            return ReadPresets(iniPath).Count;
        }

        public static bool AppendPreset(string iniPath, string value)
        {
            int index = NextFreeIndex(iniPath);
            return WritePrivateProfileString(Section, index.ToString(), value, iniPath);
        }
    }
}
