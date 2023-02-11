using System.Reflection;
using System.Net.Sockets;
using System.Net;
using NLog;
using Project.Models;
using Project.AppSetting;
using SharpPcap;
using ThreadWorker;
using Project.Controller;
using Project.Lib;
using System.Text;
using Project.Enums;

namespace Project.ProjectCtrl {

    //專案資訊
    public static class GlobalVar {

        //專案名稱
        public const string ServiceName = "ursSipGateway";
        public const string ServiceName_Ch = "URS封包橋接服務";

        // 資料庫加密的 Key & iv
        public const string ProjectName = "ursSipGateway";
        public const string DBAesKey = "550102mktok" + "42751171@mitek.com.tw"; // 32 個英文或數字        
        public const string DBAesIV = "0955502123ASDzxc"; // 16 個英文或數字                        
        //        
        public static Logger nlog = LogManager.GetLogger("Startup");

        public static string LicenseFile { private set; get; } = "";
        public static string LocalIP { private set; get; } = "";
        public static AppSettings AppSettings { private set; get; }
        public static IConfiguration Configuration { private set; get; }

        public static CaptureDeviceList MonitorPcapDevice { private set; get; } = null;
        public static List<int> MonitorPcapIndex { private set; get; } = new List<int>();

        public static string RecDataPath { private set; get; } = "";
        public static string RecTempPath { private set; get; } = "";
        public static string FFMpegExeFileName { private set; get; } = "";
        public static string SoxExeFileName { private set; get; } = "";

        // TODO: DictLiveMonitor 裡面的物件存取，是否要 Lock？
        // Key = 分機號碼，負責 LiveMonitorModel 的管理 ...
        public static Dictionary<string, LiveMonitorModel> DictLiveMonitor = new Dictionary<string, LiveMonitorModel>();

        // Key = 分機號碼，負責 ParsePacketThread 的管理，不須 Lock，在 MainWorker 中事先建立每一分機的 ParsePacketThread 
        public static Dictionary<string, ParsePacketThread> DictParseThread = new Dictionary<string, ParsePacketThread>();

        // Key = 分機號碼，負責 MakeFileThread 的管理，不須 Lock，在 MainWorker 中事先建立每一分機的 MakeFileThread
        public static Dictionary<string, MakeFileThread> DictMakeFileThread = new Dictionary<string, MakeFileThread>();

        private static object _dispatchLock = new object();
        private static Queue<PacketInfoEx> DispatchPacketQueue = new Queue<PacketInfoEx>(100000);        

        public static SipDialogControll SipDialogCtrl = new SipDialogControll();

        public static ManualResetEvent WaitPacketComing = new ManualResetEvent(false);

        internal static LicRegisterEx2Model LicenseModel = null;

        public static ManualResetEvent WaitApiSending = new ManualResetEvent(false);
        private static object _apiLock = new object();
        private static Queue<object> apiQueue = new Queue<object>(500);

        // 建構子
        static GlobalVar() {
            // 取得 FFMpegExeFileName 位置
            var exePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            FFMpegExeFileName = Path.Combine(exePath, "3rdParty","ffmpeg.exe");
            SoxExeFileName = Path.Combine(exePath, "3rdParty", "sox", "sox.exe");

            // to get local ip address string                        
            try {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0)) {
                    socket.Connect("8.8.8.8", 65530);                    
                    IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                    LocalIP = (endPoint == null) ? "" : endPoint.Address.ToString();                                        
                }
            }
            catch (Exception ex) {                
                LocalIP = "";
            }

            // 取得 Pcap Device
            GetDeviceList();
        }     

        public static void SetConfiguration(IConfiguration config) {
            Configuration = config;
            AppSettings = new AppSettings();
            Configuration.GetSection("AppSettings").Bind(AppSettings);

            // 取得要監控的 Device
            GetMonitorDevice();

            // 建立錄音 Path            
            RecTempPath = AppSettings.Recording.RecTempPath;
            RecDataPath = AppSettings.Recording.RecDataPath;            
        }

        public static string CurrentVersion {
            get {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null) {
                    return $"{version.Major}.{version.Minor}.{version.Build} build {version.Revision}";
                }
                else {
                    return $"get version error";
                }                
            }
        }

        private static void GetDeviceList() {
            nlog.Info($"pcap core version: {Pcap.SharpPcapVersion}");
            nlog.Info($"Start to get Pcap device ...");
            // Retrieve the device list
            MonitorPcapDevice = CaptureDeviceList.Instance;
            if (MonitorPcapDevice == null) {
                nlog.Error("*****************************************************");
                nlog.Error("*** 無法取得本設備的網路介面(null)，錄音封包分析程式啟動失敗 ***");
                nlog.Error("*****************************************************");
                return;
            }

            if (MonitorPcapDevice.Count < 1) {
                nlog.Error("**************************************************");
                nlog.Error("*** 沒有發現任何網路介面，錄音封包分析程式啟動失敗 ***");
                nlog.Error("**************************************************");
                return;
            }

            var seq = 0;
            nlog.Info($"共發現 {MonitorPcapDevice.Count} 個網路介面 ...");            
        }

        private static void GetMonitorDevice() {
            if (MonitorPcapDevice == null)
                return;
            if (MonitorPcapDevice.Count <= 0)
                return;

            nlog.Info($"開始取得要監控的網路介面 ...");
            foreach (var str in GlobalVar.AppSettings.NetworkInterface) {
                var index = 0;
                foreach (var dev in MonitorPcapDevice) {
                    if (dev.Name == str) {
                        nlog.Info($"\t 第 {index} 個介面納入監控({str})");
                        MonitorPcapIndex.Add(index);
                        break;
                    }
                    index++;
                }
            }            
        }

        public static void AddDispatchPacket(PacketInfoEx packetInfo) {
            lock (_dispatchLock) {
                DispatchPacketQueue.Enqueue(packetInfo);
                WaitPacketComing.Set(); // 有封包，要 set，thread 往下跑
            }
        }

        public static PacketInfoEx GetDispatchPacket() {
            PacketInfoEx packetInfo = null;
            lock (_dispatchLock) { // <== 一定要 lock
                if (DispatchPacketQueue.Count > 0) {
                    packetInfo = DispatchPacketQueue.Dequeue();
                }
                else { // 沒有就 reset，thread 會卡住不動
                    WaitPacketComing.Reset();
                }
            }
            return packetInfo;
        }

        public static long GetDispatchPacketQueueCount() {
            long ret = 0;
            lock (_dispatchLock) { // <== 一定要 lock
                ret = DispatchPacketQueue.Count;
            }
            return ret;
        }

        public static void WriteNetworkInterfaceFile() {
            var seq = 0;
            var sb = new StringBuilder();
            sb.AppendLine($"共發現 {MonitorPcapDevice.Count} 個網路介面 ...");
            foreach (var dev in MonitorPcapDevice) {
                sb.AppendLine($"=============== device #{seq} =================================");
                sb.AppendLine($"\tName={dev.Name}");
                sb.AppendLine($"\tDesc={dev.Description}");
                sb.AppendLine($"\tMac={dev.MacAddress}");
                var pcapModel = new PcapDeviceModel(dev);
                sb.AppendLine($"\tFriendlyName={pcapModel.GetFriendlyName()}");
                sb.AppendLine($"\tIpAddr={pcapModel.GetIPV4()}");
                sb.AppendLine($"===============================================================");

                // 全部印出 Device
                sb.AppendLine(dev.ToString());
                seq++;
            }

            lib_misc.ForceCreateFolder(AppSettings.Recording.RecDataPath);
            var fileName = Path.Combine(AppSettings.Recording.RecDataPath, "NetworkInterface.txt");
            try {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(fileName)) {
                    file.WriteLine(sb.ToString()); // "sb" is the StringBuilder
                }
            }
            catch(Exception ex) {
                nlog.Error($"*** 無法產生網路卡介面檔案({fileName}): {ex.Message}");
            }            
            return;
        }

        public static void AddApiQueue(object obj) {
            lock (_apiLock) {
                apiQueue.Enqueue(obj);
                WaitApiSending.Set(); // 有API，要 set，thread 往下跑
            }
        }

        public static object GetApiQueue() {
            object obj = null;
            lock (_apiLock) { // <== 一定要 lock
                if (apiQueue.Count > 0) {
                    obj = apiQueue.Dequeue();
                }
                else { // 沒有就東西 reset，thread 會卡住不動
                    WaitApiSending.Reset();
                }
            }
            return obj;
        }

        public static async Task SendAPI_WriteRecData(RecFileModel recFile, string finalFileName) {
            // 傳送 API
            await Task.Delay(1);
            var chID = 0;
            if (!int.TryParse(recFile.ExtNo, out chID)) {
                chID = 0;
            }

            var recData = new RecDataModel() {
                LoggerSeq = GlobalVar.AppSettings.Recording.LoggerSeq,
                LoggerID = GlobalVar.AppSettings.Recording.LoggerID,
                LoggerName = GlobalVar.AppSettings.Recording.LoggerName,
                RecID = 1, // 固定=1，由 API 取拉票機自行處理
                RecDate = recFile.RecStartTime.ToString("yyyy-MM-dd"),
                RecFolder = Path.GetDirectoryName(finalFileName),
                RecFileName = Path.GetFileName(finalFileName),
                RecStartTime = recFile.RecStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                RecStopTime = recFile.RecStopTime.ToString("yyyy-MM-dd HH:mm:ss"),
                RecLen = recFile.Duration,
                InboundLen = recFile.CallDir == Project.Enums.ENUM_CallDirection.Inbound ? recFile.Duration : 0,
                OutboundLen = recFile.CallDir == Project.Enums.ENUM_CallDirection.Outbound ? recFile.Duration : 0,
                CallerID = recFile.CallerID,
                DTMF = recFile.CalledID,
                CallType = recFile.CallDir == Project.Enums.ENUM_CallDirection.Inbound ? 4 : 5,
                RecType = 4,
                //DNIS = "", // 
                ChType = 6,
                ChID = chID,
                // 以下 3 個，不須指定，API 會自動 join TB_Channel中的 ChannelName, AgentID, AgentName
                //ChName = "", 
                //AgentID = "",
                //AgentName = "",
                ExtNo = recFile.ExtNo,
                TriggerType = 6,
                MediaType = 1,
                CallerName = ""
            };
            AddApiQueue(recData);
        }

        public static async Task SendAPI_WriteSystemLog(ENUM_LogType logType, string msg) {
            await Task.Delay(1);
            // 傳送 API
            var sysLog = new SystemLogModel() {
                LoggerID = GlobalVar.AppSettings.Recording.LoggerID,
                LoggerName = GlobalVar.AppSettings.Recording.LoggerName,
                LogClass = "SipLogger",
                LogType = (int)logType,
                Msg = msg
            };
            AddApiQueue(sysLog);
        }

        public static async Task SendAPI_WriteChannelStatus(RecRtpModel recRtp, SipDialogModel dialog, ENUM_LineStatus lineStatus) {
            // 傳送 API
            await Task.Delay(1);
            var chStatus = new ChannelStatusModel() {
                CallerID = recRtp.CallerID,
                DTMF = recRtp.CalledID,
                ExtNo = dialog.ExtNo,
                LineStatus = (int)lineStatus,
                CallType = dialog.Invite ? 2 : 1, // 1: inbound, 2: outbound
                LoggerSeq = AppSettings.Recording.LoggerSeq,
                StartTime = dialog.StartTalkTime.Value
            };
            AddApiQueue(chStatus);
        }

        public static bool CheckLicense(out string err) {
            err = "";            
            if (LicenseModel == null || LicenseModel.SynipPort == 0) {
                err = "no license";
                return false;
            }
            if (GlobalVar.LicenseModel.DemoExpired.HasValue) {
                if ((DateTime.Now - GlobalVar.LicenseModel.DemoExpired.Value).TotalSeconds > 0) {
                    err = $"demo license expired: {GlobalVar.LicenseModel.DemoExpired.Value.ToString("yyyy-MM-dd")}";
                    return false;
                }
            }
            return true;
        }
        
    }
}
