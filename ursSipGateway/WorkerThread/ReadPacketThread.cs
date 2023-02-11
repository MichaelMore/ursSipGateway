using NLog;
using SharpPcap;
using WorkerThread;
using Project.Models;
using Project.ProjectCtrl;
using Project.Enums;
using Newtonsoft.Json;
using Project.Lib;
using static System.Net.WebRequestMethods;
using System;
using System.Threading.Tasks.Dataflow;
using ursSipParser.Models;
using Project.Helpers;

namespace ThreadWorker
{
    public class ReadPacketThread: IWorker
    {
        // protect
        protected volatile bool _shouldStop;
        protected volatile bool _shouldPause;

        // private        
        private Thread _myThread;
        private NLog.Logger _nLog;
        
        private ILiveDevice _pcapDevice = null;                
        private PcapDeviceModel _pcapModel = null;                

        // public
        public WorkerState State { get; internal set; }               
        public ulong TotalPacket { get; internal set; } = 0;
        public string Tag { get; internal set; } = "";

        public int PcapDeviceIndex;

        public ReadPacketThread(int pcapDeviceIndex) {            
            PcapDeviceIndex = pcapDeviceIndex;
            var devices = CaptureDeviceList.Instance;
            _pcapDevice = devices[PcapDeviceIndex];
            _pcapModel = new PcapDeviceModel(_pcapDevice);

            Tag = $"ReadPacket_{_pcapModel.GetFriendlyName()}({_pcapModel.GetIPV4()})";
            Tag = lib_misc.MakeFilenameValidate(Tag, "_"); // 置換檔案的非法字元
            
            _nLog = LogManager.GetLogger($"{Tag}");
        }

        public void StartThread() {
            _myThread = new Thread(this.DoWork) {
                IsBackground = true,
                Name = Tag
            };
            State = WorkerState.Starting;
            _myThread.Start();
        }

        public void StopThread() {
            RequestStop(); // stopping ...

            if (_pcapDevice.Started)
                _pcapDevice.Close();

            _nLog.Info($"{Tag} is waiting to stop(join) ...");
            _myThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }

        public virtual void DoWork(object anObject) {
            _nLog.Info("");
            _nLog.Info($"********** PcapDevice[{PcapDeviceIndex}] is now opening ... **********");
            _nLog.Info($"\t Name = {_pcapDevice.Name}");
            _nLog.Info($"\t MacAddress = {_pcapModel.GetMac()}");
            _nLog.Info($"\t IP = {_pcapModel.GetIPV4()}");
            //Register our handler function to the 'packet arrival' event
            _pcapDevice.OnPacketArrival += new PacketArrivalEventHandler(device_OnPacketArrival);

            // Set ReadTimeoutMilliseconds
            int readTimeoutMilliseconds = GetReadPacketTimeoutMs();
            _nLog.Info($"\t ReadTimeoutMilliseconds = {readTimeoutMilliseconds}");

            // Set DeviceModes
            DeviceModes deviceMode = GlobalVar.AppSettings.Monitor.EnablePromiscuousModel ? DeviceModes.Promiscuous : DeviceModes.None;
            _nLog.Info($"\t DeviceModes = {deviceMode}");
            _pcapDevice.Open(deviceMode, readTimeoutMilliseconds);            

            var pcapFilter = GetPcapFilter();
            _nLog.Info($"\t Set Filter = {pcapFilter}");
            try {
                _pcapDevice.Filter = pcapFilter;
            }
            catch (Exception ex) {
                _nLog.Info($"\t Filter 設定錯誤: {ex.Message}");
                _nLog.Info($"程序中止");
                return;
            }

            _nLog.Info($"========== Start Capture ... ==========");
            _pcapDevice.Capture(); // <= 取封包的迴圈會卡在此處 ...，所以不需要 While Loop
            _nLog.Info($"========== End Capture ... ==========");
            _pcapDevice.Close();
        }

        // Callback: 處理封包
        private void device_OnPacketArrival(object sender, PacketCapture e) {
            var packetInfo = new PacketInfoEx(e);
            if (!packetInfo.CaptureSuccess)
                return;

            #region 檢查封包是否在監控清單中 && Payload 要有資料
            // 該設備的 IP或 MAC 不在監控清單中
            // *** 如果設定是用 mac 來監控設備，這裡也會過濾，但後續都是以 ip 進行，所以就不用再管 mac 了
            if (!packetInfo.CheckIpOrMacIsMonitoring()) 
                return;
            // 沒有 payload            
            if (packetInfo.PayloadData == null || packetInfo.PayloadData.Length < 12)
                return;
            #endregion

            GlobalVar.AddDispatchPacket(packetInfo);
            TotalPacket++;
            if (TotalPacket == ulong.MaxValue-1)
                TotalPacket = 0;
        }
        

        public void RequestStop() {
            _nLog.Info($"{Tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }

        
        /// <summary>
        /// 取得 AppSettings.Monitor.ReadPacketTimeoutMilliSec 
        /// </summary>
        /// <returns></returns>
        private int GetReadPacketTimeoutMs() {
            var ret = GlobalVar.AppSettings.Monitor.ReadPacketTimeoutMilliSec;
            if (GlobalVar.AppSettings.Monitor.ReadPacketTimeoutMilliSec < 1)
                ret = 1;
            else if (GlobalVar.AppSettings.Monitor.ReadPacketTimeoutMilliSec > 10)
                ret = 10;
            return ret;
        }

        /// <summary>
        /// 從 appsettings.json 取得 Pcap 過濾封包的指令
        /// sample: "ip and not broadcast and not multicast and not arp and (net 192.168.10.0/24 or net 10.102.7.0/24) and (tcp port 5060 or udp portrange 16384-65535)"
        /// </summary>
        /// <returns>過濾字串</returns>
        private string GetPcapFilter() {
            var basicFilter = "ip and not broadcast and not multicast and not arp"; // AppSettings.Monitor 沒設定時用 default
            if (!string.IsNullOrEmpty(GlobalVar.AppSettings.Monitor.BasicFilter))
                basicFilter = GlobalVar.AppSettings.Monitor.BasicFilter;

            var mainFilter = $"{basicFilter}";
            var netFilter = "";
            if (!lib_misc.IsNullOrEmpty(GlobalVar.AppSettings.Monitor.FilterIPRange)) {
                netFilter = String.Join(" or net ", GlobalVar.AppSettings.Monitor.FilterIPRange); // 用 or 連接 
                mainFilter = mainFilter + $" and (net {netFilter})";
            }

            var sipProto = GlobalVar.AppSettings.Monitor.SipProtocol.ToLower();
            var sipPort = GlobalVar.AppSettings.Monitor.SipPort;
            var rtpMinPort = GlobalVar.AppSettings.Monitor.RtpMinPort;
            var rtpMaxPort = GlobalVar.AppSettings.Monitor.RtpMaxPort;
            return $"{mainFilter} and ({sipProto} port {sipPort} or udp portrange {rtpMinPort}-{rtpMaxPort})";
        }
        
    }

}
