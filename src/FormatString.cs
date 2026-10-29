using System;
using System.Collections.Generic;

namespace KsuConfigurator
{
    // Проверка «строки формата вывода» по DSL из KCY_UM_HELP.RTF (раздел «О параметрах
    // считывателя») и по реальным строкам из KCY_UM.ini.
    //
    // Управляющие последовательности:
    //   \t \r \n \\ %%          — табуляция / Enter / Enter / "\" / "%"
    //   %R                      — сброс накопления контрольной суммы
    //   %[a][b][c][d][e][f]g    — данные: a — начальная позиция (цифра), b — число байт минус 1
    //                             (цифра), c — способ вывода f|g|h|l (нет = hex), d — реверс b|c|d,
    //                             e — лидирующие нули '.', f — расчёт CRC/КС ',',
    //                             g — что выводим: I|T|U|L|K|C|V
    internal static class FormatString
    {
        public const int MaxBytes = KsuProtocol.FormatLength;

        private const string DataLetters = "ITLUKCVW";
        private const string ModeLetters = "fghl";
        private const string ReverseLetters = "bcd";

        private const string Error = "ошибка: ";
        private const string Warn = "предупреждение: ";

        public static int ByteLength(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return KsuProtocol.Cp1251.GetByteCount(text);
        }

        public static List<string> Validate(string text)
        {
            List<string> messages = new List<string>();
            if (text == null) return messages;

            int bytes = ByteLength(text);
            if (bytes > MaxBytes)
                messages.Add(Error + string.Format("длина {0} байт в cp1251, максимум {1}", bytes, MaxBytes));

            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == '\\')
                {
                    if (i + 1 >= text.Length)
                    {
                        messages.Add(Error + string.Format("позиция {0}: висящий '\\'", i));
                        break;
                    }
                    char e = text[i + 1];
                    if (e != 't' && e != 'r' && e != 'n' && e != '\\')
                        messages.Add(Warn + string.Format("позиция {0}: '\\{1}' устройством не распознаётся, пройдёт как текст", i, e));
                    i += 2;
                    continue;
                }
                if (ch != '%') { i++; continue; }

                i++;
                if (i >= text.Length)
                {
                    messages.Add(Error + string.Format("позиция {0}: висящий '%'", i - 1));
                    break;
                }
                if (text[i] == '%') { i++; continue; }
                if (text[i] == 'R') { i++; continue; }
                if (text[i] == '\\')
                {
                    messages.Add(Error + string.Format("позиция {0}: '%\\' — не последовательность DSL", i - 1));
                    i++;
                    continue;
                }

                int dataStart = i;
                int digits = 0;
                while (i < text.Length && char.IsDigit(text[i])) { i++; digits++; }
                if (i < text.Length && ModeLetters.IndexOf(text[i]) >= 0) i++;
                if (i < text.Length && ReverseLetters.IndexOf(text[i]) >= 0) i++;
                if (i < text.Length && text[i] == '.') i++;
                if (i < text.Length && text[i] == ',') i++;

                if (i >= text.Length)
                {
                    messages.Add(Error + string.Format("позиция {0}: незавершённая '{1}'",
                        dataStart - 1, text.Substring(dataStart - 1)));
                    break;
                }
                char g = text[i];
                string token = text.Substring(dataStart - 1, i - dataStart + 2);
                if (digits > 3)
                    messages.Add(Warn + string.Format("позиция {0}: '{1}' — слишком длинное числовое поле (a/b по одному цифре, %nnn для V)",
                        dataStart - 1, token));
                if (DataLetters.IndexOf(char.ToUpperInvariant(g)) < 0)
                    messages.Add(Error + string.Format("позиция {0}: неизвестный выводимый символ в '{1}'",
                        dataStart - 1, token));
                else if (g == 'W')
                    messages.Add(Warn + string.Format("позиция {0}: '%W' есть в KCY_UM.ini, но не описан в справке",
                        dataStart - 1));
                i++;
            }
            return messages;
        }

        public static List<string> Describe(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;

            int i = 0;
            int literalStart = 0;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    FlushLiteral(list, text, literalStart, i);
                    string e = i + 1 < text.Length ? text[i + 1].ToString() : "";
                    list.Add(string.Format("\\{0} -> {1}", e, EscapeMeaning(e)));
                    i += 2;
                    literalStart = i;
                    continue;
                }
                if (text[i] != '%') { i++; continue; }

                FlushLiteral(list, text, literalStart, i);
                int j = i + 1;
                if (j >= text.Length) { list.Add("висящий '%'"); break; }
                if (text[j] == '%' || text[j] == 'R')
                {
                    list.Add(text.Substring(i, 2) + " -> " +
                        (text[j] == '%' ? "символ '%'" : "сброс контрольной суммы"));
                    i = j + 1;
                    literalStart = i;
                    continue;
                }

                int k = j;
                while (k < text.Length && char.IsDigit(text[k])) k++;
                if (k < text.Length && ModeLetters.IndexOf(text[k]) >= 0) k++;
                if (k < text.Length && ReverseLetters.IndexOf(text[k]) >= 0) k++;
                if (k < text.Length && text[k] == '.') k++;
                if (k < text.Length && text[k] == ',') k++;
                if (k >= text.Length)
                {
                    list.Add("незавершённая последовательность: " + text.Substring(i));
                    break;
                }
                list.Add(text.Substring(i, k - i + 1) + " -> " + DataMeaning(text, j, k));
                i = k + 1;
                literalStart = i;
            }
            FlushLiteral(list, text, literalStart, text.Length);
            return list;
        }

        private static void FlushLiteral(List<string> list, string text, int from, int to)
        {
            if (to > from) list.Add("текст: \"" + text.Substring(from, to - from) + "\"");
        }

        private static string EscapeMeaning(string e)
        {
            switch (e)
            {
                case "t": return "табуляция";
                case "r": return "Enter / возврат каретки";
                case "n": return "Enter / перевод строки";
                case "\\": return "символ '\\'";
                default: return "печатается как есть";
            }
        }

        private static string DataMeaning(string text, int from, int gIndex)
        {
            string source;
            switch (char.ToUpperInvariant(text[gIndex]))
            {
                case 'I': source = "серийник считывателя"; break;
                case 'T': source = "тип карты (1 цифра)"; break;
                case 'U': source = "UID карты"; break;
                case 'L': source = "длина кода карты"; break;
                case 'K': source = "контрольная сумма после последней %R"; break;
                case 'C': source = "CRC8 после последней %R"; break;
                case 'V': source = "вставка произвольного байта"; break;
                case 'W': source = "данные Wiegand (нет в справке, есть в KCY_UM.ini)"; break;
                default: source = "неизвестный источник"; break;
            }

            string mode = "hex";
            List<string> extra = new List<string>();
            for (int p = from; p < gIndex; p++)
            {
                char c = char.ToLowerInvariant(text[p]);
                if (c == 'f') mode = "десятичное число";
                else if (c == 'g') mode = "строка с нуль-терминатором";
                else if (c == 'h') mode = "строка заданной длины";
                else if (c == 'l') mode = "не выводится (только для расчёта CRC)";
                else if (c == 'b') extra.Add("бит-реверс каждого байта");
                else if (c == 'c') extra.Add("обратный порядок байтов");
                else if (c == 'd') extra.Add("реверс байтов и битов");
                else if (c == '.') extra.Add("лидирующие нули как в справке");
                else if (c == ',') extra.Add("расчёт CRC/КС");
            }
            if (extra.Count > 0)
                return source + ", вывод: " + mode + "; " + string.Join("; ", extra.ToArray());
            return source + ", вывод: " + mode;
        }
    }
}
