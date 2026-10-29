using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KsuConfigurator
{
    // Результат поиска одного HID-интерфейса.
    internal sealed class HidDevice : IDisposable
    {
        public string Path;
        public string Manufacturer = string.Empty;
        public string Product = string.Empty;
        public string Serial = string.Empty;
        public int Vid;
        public int Pid;
        public int BcdDevice;
        public int Usage;
        public int UsagePage;
        public int InputReportByteLength;
        public int OutputReportByteLength;
        public int FeatureReportByteLength;
        public string ProbeError;

        private SafeFileHandle _handle;
        private bool _readWrite;

        // Признаки «своего» устройства взяты из оригинала: KCY_UM.exe сравнивает
        // строки производителя и продукта с "PROX (www.prox.ru)" и "RFID reader"
        // (.data 0x4A21D8 / 0x4A21EB, SearchClick -> func_4029F4).
        public const string ProxManufacturer = "PROX (www.prox.ru)";
        public const string ProxProduct = "RFID reader";

        public bool IsKsu
        {
            get
            {
                return string.Equals(Manufacturer, ProxManufacturer, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Product, ProxProduct, StringComparison.OrdinalIgnoreCase);
            }
        }

        public string Description
        {
            get
            {
                string base0 = string.Format("{0} {1}  vid:pid={2:X4}:{3:X4}  sn={4}  usage={5:X2}/{6:X2}  in/out/feat={7}/{8}/{9}",
                    Manufacturer, Product, Vid, Pid, Serial, UsagePage, Usage,
                    InputReportByteLength, OutputReportByteLength, FeatureReportByteLength);
                if (!string.IsNullOrEmpty(ProbeError)) base0 += "  [" + ProbeError + "]";
                return base0;
            }
        }

        public void Open(bool readWrite)
        {
            if (_handle != null && !_handle.IsInvalid)
            {
                if (!readWrite || _readWrite) return;
                // Хэндл уже открыт только на чтение — для HidD_SetFeature нужен доступ на запись.
                Dispose();
            }
            uint access = NativeHid.GENERIC_READ | (readWrite ? NativeHid.GENERIC_WRITE : 0u);
            _handle = NativeHid.CreateFile(Path, access, NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE,
                IntPtr.Zero, NativeHid.OPEN_EXISTING, 0, IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                string err = NativeHid.LastError("CreateFile(" + Path + ")");
                _handle.Dispose();
                _handle = null;
                throw new InvalidOperationException(err);
            }
            // ReadFile блокируется до появления отчёта; оригинал increasing число буферов (HidD_SetNumInputBuffers).
            NativeHid.HidD_SetNumInputBuffers(_handle, 8);
            _readWrite = readWrite;
        }

        public bool IsOpen { get { return _handle != null && !_handle.IsInvalid; } }

        public bool GetFeature(byte[] report, out string error)
        {
            error = null;
            try { Open(false); }
            catch (InvalidOperationException ex) { error = ex.Message; return false; }
            if (!NativeHid.HidD_GetFeature(_handle, report, report.Length))
            {
                error = NativeHid.LastError("HidD_GetFeature");
                return false;
            }
            return true;
        }

        public bool SetFeature(byte[] report, out string error)
        {
            error = null;
            try { Open(true); }
            catch (InvalidOperationException ex) { error = ex.Message; return false; }
            if (!NativeHid.HidD_SetFeature(_handle, report, report.Length))
            {
                error = NativeHid.LastError("HidD_SetFeature");
                return false;
            }
            return true;
        }

        // Один отчёт ввода (в KCY это сообщения Consumer Control / эмуляция клавиатуры).
        public bool ReadInputReport(out byte[] report, out string error)
        {
            report = null;
            error = null;
            int size = InputReportByteLength > 0 ? InputReportByteLength : 8;
            byte[] buf = new byte[size];
            int read;
            if (!NativeHid.ReadFile(_handle, buf, buf.Length, out read, IntPtr.Zero))
            {
                error = NativeHid.LastError("ReadFile");
                return false;
            }
            byte[] res = new byte[read];
            Array.Copy(buf, res, read);
            report = res;
            return true;
        }

        public void CancelPendingRead()
        {
            if (IsOpen) NativeHid.CancelIoEx(_handle, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_handle != null && !_handle.IsInvalid) _handle.Close();
            _handle = null;
            _readWrite = false;
        }

        public static List<HidDevice> Enumerate()
        {
            List<HidDevice> list = new List<HidDevice>();
            Guid hidGuid;
            NativeHid.HidD_GetHidGuid(out hidGuid);

            IntPtr set = NativeHid.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                NativeHid.DIGCF_PRESENT | NativeHid.DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) return list;

            try
            {
                for (int i = 0; ; i++)
                {
                    NativeHid.SP_DEVICE_INTERFACE_DATA ifd = new NativeHid.SP_DEVICE_INTERFACE_DATA();
                    ifd.cbSize = Marshal.SizeOf(typeof(NativeHid.SP_DEVICE_INTERFACE_DATA));
                    if (!NativeHid.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifd)) break;

                    string path = GetPath(set, ref ifd);
                    if (path == null) continue;

                    HidDevice d = new HidDevice();
                    d.Path = path;
                    Probe(d);
                    list.Add(d);
                }
            }
            finally
            {
                NativeHid.SetupDiDestroyDeviceInfoList(set);
            }
            return list;
        }

        // cbSize у SP_DEVICE_INTERFACE_DETAIL_DATA различается для x86 (6) и x64 (8).
        private static int DetailCbSize { get { return IntPtr.Size == 8 ? 8 : 6; } }

        private static string GetPath(IntPtr set, ref NativeHid.SP_DEVICE_INTERFACE_DATA ifd)
        {
            int required;
            NativeHid.SetupDiGetDeviceInterfaceDetail(set, ref ifd, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 0) return null;
            IntPtr buffer = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(buffer, DetailCbSize);
                int got;
                if (!NativeHid.SetupDiGetDeviceInterfaceDetail(set, ref ifd, buffer, required, out got, IntPtr.Zero))
                    return null;
                return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void Probe(HidDevice d)
        {
            // HidD_GetPreparsedData/HidD_Get*String требуют хэндл с правом чтения.
            SafeFileHandle h = NativeHid.CreateFile(d.Path, NativeHid.GENERIC_READ,
                NativeHid.FILE_SHARE_READ | NativeHid.FILE_SHARE_WRITE, IntPtr.Zero,
                NativeHid.OPEN_EXISTING, 0, IntPtr.Zero);
            try
            {
                if (h.IsInvalid)
                {
                    d.ProbeError = NativeHid.LastError("CreateFile");
                    return;
                }

                NativeHid.HIDD_ATTRIBUTES a = NativeHid.HIDD_ATTRIBUTES.Create();
                if (NativeHid.HidD_GetAttributes(h, ref a))
                {
                    d.Vid = a.VendorID;
                    d.Pid = a.ProductID;
                    d.BcdDevice = (a.VersionNumberHigh << 8) | a.VersionNumberLow;
                }
                d.Manufacturer = ReadString(h, NativeHid.HidD_GetManufacturerString);
                d.Product = ReadString(h, NativeHid.HidD_GetProductString);
                d.Serial = ReadString(h, NativeHid.HidD_GetSerialNumberString);

                IntPtr preparsed;
                if (NativeHid.HidD_GetPreparsedData(h, out preparsed))
                {
                    try
                    {
                        NativeHid.HIDP_CAPS caps;
                        int status = NativeHid.HidP_GetCaps(preparsed, out caps);
                        if (status == NativeHid.HIDP_STATUS_SUCCESS)
                        {
                            d.Usage = caps.Usage;
                            d.UsagePage = caps.UsagePage;
                            d.InputReportByteLength = caps.InputReportByteLength;
                            d.OutputReportByteLength = caps.OutputReportByteLength;
                            d.FeatureReportByteLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        NativeHid.HidD_FreePreparsedData(preparsed);
                    }
                }
            }
            finally
            {
                if (!h.IsInvalid) h.Dispose();
            }
        }

        private delegate bool GetStringDelegate(SafeFileHandle h, IntPtr buffer, int length);

        private static string ReadString(SafeFileHandle h, GetStringDelegate getter)
        {
            IntPtr buffer = Marshal.AllocHGlobal(NativeHid.MAX_MULTISYSTEM_BUFFER + 2);
            try
            {
                Marshal.WriteInt64(buffer, 0);
                if (!getter(h, buffer, NativeHid.MAX_MULTISYSTEM_BUFFER)) return string.Empty;
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public static string ToHex(byte[] data)
        {
            if (data == null) return string.Empty;
            StringBuilder sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0 && i % 16 == 0) sb.Append("\r\n");
                else if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
