using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace KsuConfigurator
{
    internal sealed class MainForm : Form
    {
        private ListView _devices;
        private TextBox _devId;
        private NumericUpDown _autoRepeat;
        private NumericUpDown _wakeInterval;
        private Label _version;
        private CheckBox _forbidRs232;
        private CheckBox _forbidKeyboard;
        private CheckBox _forbidWake;
        private CheckBox _powerSave;
        private ComboBox _presets;
        private TextBox _format;
        private Label _formatInfo;
        private TextBox _messages;
        private TextBox _report;
        private TextBox _log;
        private Button _search;
        private Button _read;
        private Button _write;
        private Button _monitor;

        private readonly List<HidDevice> _found = new List<HidDevice>();
        private Thread _monitorThread;
        private HidDevice _monitored;
        private volatile bool _monitoring;
        private string _iniPath;

        public MainForm()
        {
            BuildUi();
            _iniPath = System.IO.Path.Combine(Application.StartupPath, "KCY_UM.ini");
            LoadPresets();
            Log("Каркас конфигуратора. Перед первой записью прочитайте текущие параметры считывателя.");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SearchDevices();
        }

        private void BuildUi()
        {
            Text = "КСУ configurator (каркас) — Prox USB RFID";
            ClientSize = new Size(920, 832);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(936, 871);
            Font = new Font("Microsoft Sans Serif", 8.25f);

            _search = MakeButton("Найти считыватели", new Point(12, 12), 150);
            _search.Click += delegate { SearchDevices(); };
            Controls.Add(_search);

            Button reload = MakeButton("Перечитать INI", new Point(168, 12), 120);
            reload.Click += delegate { LoadPresets(); };
            Controls.Add(reload);

            _devices = new ListView();
            _devices.Location = new Point(12, 44);
            _devices.Size = new Size(896, 120);
            _devices.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _devices.View = View.Details;
            _devices.FullRowSelect = true;
            _devices.HideSelection = false;
            _devices.Columns.Add("Устройство", 420);
            _devices.Columns.Add("VID:PID", 80);
            _devices.Columns.Add("Серийник", 105);
            _devices.Columns.Add("Usage", 70);
            _devices.Columns.Add("in/out/feat", 100);
            _devices.Columns.Add("Признак КСУ", 90);
            _devices.SelectedIndexChanged += delegate { OnDeviceSelected(); };
            Controls.Add(_devices);

            GroupBox gbParams = new GroupBox();
            gbParams.Text = "Параметры считывателя";
            gbParams.Location = new Point(12, 172);
            gbParams.Size = new Size(896, 378);
            gbParams.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(gbParams);

            gbParams.Controls.Add(MakeLabel("Серийник (до 8 симв.):", new Point(16, 26)));
            _devId = MakeTextBox(new Point(16, 44), 150);
            _devId.MaxLength = 8;
            gbParams.Controls.Add(_devId);

            gbParams.Controls.Add(MakeLabel("Автоповтор 0,1 с (0 — выкл):", new Point(220, 26)));
            _autoRepeat = MakeNumeric(new Point(220, 44), 0, 255);
            gbParams.Controls.Add(_autoRepeat);

            gbParams.Controls.Add(MakeLabel("Интервал побудки 0,25 с:", new Point(400, 26)));
            _wakeInterval = MakeNumeric(new Point(400, 44), 0, 15);
            gbParams.Controls.Add(_wakeInterval);

            gbParams.Controls.Add(MakeLabel("Версия прошивки (только чтение):", new Point(580, 26)));
            _version = MakeLabel("—", new Point(580, 44));
            gbParams.Controls.Add(_version);

            _forbidRs232 = MakeCheck("Запретить вывод в RS232  (бит 0x10)", new Point(16, 78));
            _forbidKeyboard = MakeCheck("Запретить вывод на клавиатуру  (бит 0x20)", new Point(16, 100));
            _forbidWake = MakeCheck("Запретить будить компьютер из ждущего режима  (бит 0x40)", new Point(16, 122));
            _powerSave = MakeCheck("Экономия потребления — опрос карты раз в 1 с  (бит 0x80)", new Point(16, 144));
            gbParams.Controls.Add(_forbidRs232);
            gbParams.Controls.Add(_forbidKeyboard);
            gbParams.Controls.Add(_forbidWake);
            gbParams.Controls.Add(_powerSave);

            gbParams.Controls.Add(MakeLabel("Заготовки строки формата ([FStrings] из KCY_UM.ini):", new Point(16, 176)));
            _presets = new ComboBox();
            _presets.Location = new Point(16, 194);
            _presets.Size = new Size(680, 24);
            _presets.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Left;
            _presets.DropDownStyle = ComboBoxStyle.DropDownList;
            _presets.SelectedIndexChanged += delegate { ApplyPreset(); };
            gbParams.Controls.Add(_presets);

            Button addIni = MakeButton("Добавить в [FStrings]", new Point(710, 193), 170);
            addIni.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addIni.Click += delegate { AddToIni(); };
            gbParams.Controls.Add(addIni);

            gbParams.Controls.Add(MakeLabel("Строка формата вывода:", new Point(16, 226)));
            _format = MakeTextBox(new Point(16, 244), 864);
            _format.Size = new Size(864, 52);
            _format.Multiline = true;
            _format.ScrollBars = ScrollBars.Vertical;
            _format.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _format.Font = new Font("Consolas", 9f);
            _format.TextChanged += delegate { UpdateFormatInfo(); };
            gbParams.Controls.Add(_format);

            _formatInfo = MakeLabel("байт: 0 / 112", new Point(16, 304));
            gbParams.Controls.Add(_formatInfo);

            _messages = MakeTextBox(new Point(16, 324), 864);
            _messages.Size = new Size(864, 42);
            _messages.Multiline = true;
            _messages.ReadOnly = true;
            _messages.ScrollBars = ScrollBars.Vertical;
            _messages.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _messages.BackColor = SystemColors.Window;
            gbParams.Controls.Add(_messages);

            GroupBox gbReport = new GroupBox();
            gbReport.Text = "Feature-отчёт 0x03 (128 байт) — то, что реально уходит на считыватель";
            gbReport.Location = new Point(12, 558);
            gbReport.Size = new Size(896, 152);
            gbReport.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(gbReport);

            _report = MakeTextBox(new Point(16, 22), 864);
            _report.Size = new Size(864, 122);
            _report.Multiline = true;
            _report.WordWrap = false;
            _report.ReadOnly = true;
            _report.ScrollBars = ScrollBars.Vertical;
            _report.Font = new Font("Consolas", 9f);
            _report.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gbReport.Controls.Add(_report);

            _read = MakeButton("Прочитать из устройства", new Point(12, 720), 170);
            _read.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _read.Click += delegate { ReadDevice(); };
            Controls.Add(_read);

            _write = MakeButton("Записать в устройство", new Point(188, 720), 160);
            _write.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _write.Click += delegate { WriteDevice(); };
            Controls.Add(_write);

            _monitor = MakeButton("Слушать отчёты", new Point(354, 720), 140);
            _monitor.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _monitor.Click += delegate { ToggleMonitor(); };
            Controls.Add(_monitor);

            _log = MakeTextBox(new Point(500, 718), 408);
            _log.Size = new Size(408, 100);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_log);
        }

        private static Button MakeButton(string text, Point location, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = location;
            b.Size = new Size(width, 26);
            return b;
        }

        private static Label MakeLabel(string text, Point location)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = location;
            l.AutoSize = true;
            return l;
        }

        private static TextBox MakeTextBox(Point location, int width)
        {
            TextBox t = new TextBox();
            t.Location = location;
            t.Size = new Size(width, 24);
            return t;
        }

        private static NumericUpDown MakeNumeric(Point location, int min, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = location;
            n.Size = new Size(70, 24);
            n.Minimum = min;
            n.Maximum = max;
            return n;
        }

        private static CheckBox MakeCheck(string text, Point location)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Location = location;
            c.AutoSize = true;
            return c;
        }

        private HidDevice Selected
        {
            get
            {
                if (_devices.SelectedIndices.Count == 0) return null;
                int index = _devices.SelectedIndices[0];
                if (index < 0 || index >= _found.Count) return null;
                return _found[index];
            }
        }

        private void SearchDevices()
        {
            StopMonitor();
            foreach (HidDevice d in _found) d.Dispose();
            _found.Clear();
            _devices.Items.Clear();

            List<HidDevice> all = HidDevice.Enumerate();
            int ksu = 0;
            foreach (HidDevice d in all)
            {
                if (!d.IsKsu && !IsInteresting(d)) continue;
                _found.Add(d);
                ListViewItem item = new ListViewItem(d.IsKsu ? d.Product + " / " + d.Manufacturer : d.Product);
                item.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "{0:X4}:{1:X4}", d.Vid, d.Pid));
                item.SubItems.Add(d.Serial);
                item.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "{0:X2}/{1:X2}", d.UsagePage, d.Usage));
                item.SubItems.Add(string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}",
                    d.InputReportByteLength, d.OutputReportByteLength, d.FeatureReportByteLength));
                item.SubItems.Add(d.IsKsu ? "свой" : "кандидат");
                item.Tag = d;
                _devices.Items.Add(item);
                if (d.IsKsu) ksu++;
            }
            Log(string.Format("HID-устройств в системе: {0}, подходящих КСУ: {1}", all.Count, ksu));
            all.Clear();
            if (_devices.Items.Count > 0) _devices.Items[0].Selected = true;
        }

        // Считыватели старых выпусков могут отличаться строками производителя/продукта,
        // поэтому в список дополнительно попадают устройства с feature-отчётом 128 байт.
        private static bool IsInteresting(HidDevice d)
        {
            return d.FeatureReportByteLength == KsuProtocol.ReportLength;
        }

        private void OnDeviceSelected()
        {
            HidDevice d = Selected;
            if (d == null) return;
            if (_monitoring) StopMonitor();
            Log(d.Description);
            if (d.FeatureReportByteLength != 0 && d.FeatureReportByteLength != KsuProtocol.ReportLength)
                Log("Внимание: HIDP_CAPS.FeatureReportByteLength = " + d.FeatureReportByteLength +
                    ", оригинал жёстко использует " + KsuProtocol.ReportLength + ". Запись может не пройти.");
            PeekReport(d);
        }

        // HidD_GetFeature flash не перезаписывает, поэтому текущее содержимое отчёта
        // показываем сразу при выборе устройства — поля формы при этом не трогаем.
        private void PeekReport(HidDevice d)
        {
            if (d.FeatureReportByteLength != KsuProtocol.ReportLength) return;
            byte[] report = new byte[KsuProtocol.ReportLength];
            report[0] = KsuProtocol.ReportId;
            string error;
            if (!d.GetFeature(report, out error))
            {
                Log("HidD_GetFeature: " + error);
                return;
            }
            ShowReport(report);
            KsuParams p = KsuParams.FromReport(report);
            _version.Text = p.VersionText;
            Log("В устройстве сейчас: серийник=\"" + p.DeviceId + "\", автоповтор=" + p.AutoRepeat +
                ", побудка=" + p.WakeInterval + ", флаги=0x" + ((int)p.Flags).ToString("X2") +
                ", версия=" + p.VersionText);
        }

        private void LoadPresets()
        {
            List<string> presets = KsuIni.ReadPresets(_iniPath);
            _presets.Items.Clear();
            foreach (string p in presets) _presets.Items.Add(p);
            if (_presets.Items.Count > 0) _presets.SelectedIndex = 0;
            Log(string.Format("[FStrings] в {0}: {1} заготовок", _iniPath, presets.Count));
        }

        private void ApplyPreset()
        {
            if (_presets.SelectedItem == null) return;
            _format.Text = _presets.SelectedItem.ToString();
        }

        private void AddToIni()
        {
            string value = _format.Text;
            if (value.Length == 0)
            {
                Log("Строка формата пуста — добавлять нечего.");
                return;
            }
            if (KsuIni.AppendPreset(_iniPath, value))
            {
                Log("Добавлено в " + _iniPath + " как ключ " + (KsuIni.NextFreeIndex(_iniPath) - 1) + ".");
                LoadPresets();
            }
            else
            {
                Log("Не удалось записать " + _iniPath + " (права на папку?).");
            }
        }

        private void UpdateFormatInfo()
        {
            int bytes = FormatString.ByteLength(_format.Text);
            _formatInfo.Text = string.Format("байт cp1251: {0} / {1}", bytes, KsuProtocol.FormatLength);
            _formatInfo.ForeColor = bytes > KsuProtocol.FormatLength ? Color.Firebrick : SystemColors.ControlText;

            List<string> messages = FormatString.Validate(_format.Text);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append("\r\n");
                sb.Append(messages[i]);
            }
            _messages.Text = sb.ToString();
        }

        private List<string> CurrentErrors()
        {
            List<string> all = FormatString.Validate(_format.Text);
            List<string> errors = new List<string>();
            foreach (string m in all) if (m.StartsWith("ошибка")) errors.Add(m);
            return errors;
        }

        private KsuParams CollectParams()
        {
            KsuParams p = new KsuParams();
            p.DeviceId = _devId.Text;
            p.AutoRepeat = (int)_autoRepeat.Value;
            p.WakeInterval = (int)_wakeInterval.Value;
            KsuFlags flags = KsuFlags.None;
            if (_forbidRs232.Checked) flags |= KsuFlags.ForbidRs232;
            if (_forbidKeyboard.Checked) flags |= KsuFlags.ForbidKeyboard;
            if (_forbidWake.Checked) flags |= KsuFlags.ForbidWake;
            if (_powerSave.Checked) flags |= KsuFlags.PowerSave;
            p.Flags = flags;
            p.Format = _format.Text;
            return p;
        }

        private void ApplyParams(KsuParams p)
        {
            _devId.Text = p.DeviceId;
            _autoRepeat.Value = Clamp(_autoRepeat, p.AutoRepeat);
            _wakeInterval.Value = Clamp(_wakeInterval, p.WakeInterval);
            _forbidRs232.Checked = (p.Flags & KsuFlags.ForbidRs232) != 0;
            _forbidKeyboard.Checked = (p.Flags & KsuFlags.ForbidKeyboard) != 0;
            _forbidWake.Checked = (p.Flags & KsuFlags.ForbidWake) != 0;
            _powerSave.Checked = (p.Flags & KsuFlags.PowerSave) != 0;
            _format.Text = p.Format;
            _version.Text = p.VersionText;
        }

        private static int Clamp(NumericUpDown n, int value)
        {
            if (value < (int)n.Minimum) return (int)n.Minimum;
            if (value > (int)n.Maximum) return (int)n.Maximum;
            return value;
        }

        private void ShowReport(byte[] report)
        {
            _report.Text = HidDevice.ToHex(report);
        }

        private void ReadDevice()
        {
            HidDevice d = Selected;
            if (d == null) { Log("Сначала выберите устройство."); return; }
            // Незавершённый ReadFile в потоке прослушивания держит хэндл: HidD_GetFeature на нём
            // не выполнится до прихода отчёта, и окно выглядит зависшим.
            if (_monitoring)
            {
                if (!StopMonitor()) { Log("Слушание не остановилось — чтение отменено."); return; }
                Log("Слушание остановлено перед чтением.");
            }
            byte[] report = new byte[KsuProtocol.ReportLength];
            report[0] = KsuProtocol.ReportId;
            string error;
            if (!d.GetFeature(report, out error))
            {
                Log("Чтение не удалось — " + error);
                return;
            }
            ShowReport(report);
            KsuParams p = KsuParams.FromReport(report);
            ApplyParams(p);
            Log("Прочитано: серийник=\"" + p.DeviceId + "\", автоповтор=" + p.AutoRepeat +
                ", побудка=" + p.WakeInterval + ", флаги=0x" + ((int)p.Flags).ToString("X2") +
                ", версия=" + p.VersionText);
        }

        private void WriteDevice()
        {
            HidDevice d = Selected;
            if (d == null) { Log("Сначала выберите устройство."); return; }

            List<string> errors = CurrentErrors();
            if (errors.Count > 0)
            {
                Log("Запись заблокирована: " + string.Join(" | ", errors.ToArray()));
                return;
            }

            if (_monitoring)
            {
                if (!StopMonitor()) { Log("Слушание не остановилось — запись отменена."); return; }
                Log("Слушание остановлено перед записью.");
            }

            KsuParams p = CollectParams();
            byte[] report = p.ToReport();
            ShowReport(report);

            using (ConfirmForm confirm = new ConfirmForm(d, p, report))
            {
                if (confirm.ShowDialog(this) != DialogResult.OK)
                {
                    Log("Запись отменена.");
                    return;
                }
            }

            string error;
            if (!d.SetFeature(report, out error))
            {
                Log("HidD_SetFeature не прошла — " + error);
                return;
            }
            Log("Записано. Перечитываю для контроля.");
            ReadDevice();
        }

        private void ToggleMonitor()
        {
            if (_monitoring) { StopMonitor(); return; }
            HidDevice d = Selected;
            if (d == null) { Log("Сначала выберите устройство."); return; }
            try { d.Open(true); }
            catch (InvalidOperationException ex) { Log(ex.Message); return; }

            _monitored = d;
            _monitoring = true;
            _monitor.Text = "Остановить слушание";
            Log("Слушаю input-отчёты (raw hex)...");
            _monitorThread = new Thread(MonitorLoop);
            _monitorThread.IsBackground = true;
            _monitorThread.Start();
        }

        private void MonitorLoop()
        {
            HidDevice d = _monitored;
            while (_monitoring)
            {
                string error;
                byte[] report;
                if (d.ReadInputReport(out report, out error))
                {
                    AppendLog("input: " + HidDevice.ToHex(report).Replace('\n', ' '));
                }
                else
                {
                    if (error != null) AppendLog("читание прервано — " + error);
                    break;
                }
            }
        }

        private bool StopMonitor()
        {
            if (!_monitoring) return true;
            _monitoring = false;
            if (_monitored != null) _monitored.CancelPendingRead();
            bool stopped = true;
            if (_monitorThread != null)
            {
                if (!_monitorThread.Join(2000)) stopped = false;
                _monitorThread = null;
            }
            _monitored = null;
            if (_monitor.InvokeRequired) _monitor.BeginInvoke((Action)delegate { _monitor.Text = "Слушать отчёты"; });
            else _monitor.Text = "Слушать отчёты";
            return stopped;
        }

        private void Log(string message)
        {
            if (_log.InvokeRequired) { AppendLog(message); return; }
            if (_log.TextLength > 60000) _log.Clear();
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void AppendLog(string message)
        {
            if (_log.IsHandleCreated)
            {
                _log.BeginInvoke((Action)delegate { Log(message); });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopMonitor();
            foreach (HidDevice d in _found) d.Dispose();
            base.OnFormClosing(e);
        }
    }

    // Модальное подтверждение записи: параметры + hex-дамп отчёта.
    internal sealed class ConfirmForm : Form
    {
        public ConfirmForm(HidDevice device, KsuParams p, byte[] report)
        {
            Text = "Подтвердите запись в " + device.Serial;
            ClientSize = new Size(640, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            Label head = new Label();
            head.AutoSize = true;
            head.Location = new Point(16, 14);
            head.Text = "Параметры:";
            Controls.Add(head);

            TextBox details = new TextBox();
            details.Location = new Point(16, 34);
            details.Size = new Size(608, 88);
            details.Multiline = true;
            details.ReadOnly = true;
            details.Text = string.Format("серийник: \"{0}\"{4}автоповтор: {1} (шаг 0,1 с){4}интервал побудки: {2} (шаг 0,25 с){4}флаги: 0x{3:X2}{4}версия (только чтение): {5}",
                p.DeviceId, p.AutoRepeat, p.WakeInterval, (int)p.Flags, Environment.NewLine, p.VersionText);
            Controls.Add(details);

            TextBox hex = new TextBox();
            hex.Location = new Point(16, 130);
            hex.Size = new Size(608, 132);
            hex.Multiline = true;
            hex.ReadOnly = true;
            hex.ScrollBars = ScrollBars.Vertical;
            hex.Font = new Font("Consolas", 9f);
            hex.Text = "feature-отчёт ID 0x03, " + KsuProtocol.ReportLength + " байт:" + Environment.NewLine +
                HidDevice.ToHex(report);
            Controls.Add(hex);

            Label warn = new Label();
            warn.Location = new Point(16, 268);
            warn.Size = new Size(608, 30);
            warn.ForeColor = Color.Firebrick;
            warn.Text = "Параметры лежат во flash считывателя: ресурс — несколько десятков тысяч перезаписей.";
            Controls.Add(warn);

            Button ok = new Button();
            ok.Text = "Записать";
            ok.Size = new Size(120, 28);
            ok.Location = new Point(380, 302);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.Size = new Size(120, 28);
            cancel.Location = new Point(504, 302);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
