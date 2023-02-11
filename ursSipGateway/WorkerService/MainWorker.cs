using Project.Database;
using Project.Helper;
using Project.Models;
using Project.ProjectCtrl;
using NLog;
using ThreadWorker;
using WorkerThread;
using Project.Lib;
using Project.Enums;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using Project.AppSetting;

namespace Project.WorkerService {
    class MainWorker : BaseWorker {

        public override string className => GetType().Name;
        protected DemoDb db => new DemoDb();

        public MainWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger("Startup");
        }
        

        //TODO: 在 API 提供增加/停止分機錄音的功能，方便變更系統設定，而不需要將錄音核心重啟!!!

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            if (!CheckSystemSettings(out string errMsg)) {
                nlog.Error($"\t {errMsg}");
                GlobalVar.SendAPI_WriteSystemLog(ENUM_LogType.Fatal, errMsg);
                return;
            }
            CheckMonitorDevice(); // 移除 IP/MAC 空白或重複

            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute * 1000, stoppingToken);
            nlog.Info($"{GlobalVar.ProjectName} MainWorker.ExecuteAsync starting ...");            

            #region 啟動每一個分機的監聽 LiveMonitorModel
            nlog.Info($"總共有 {GlobalVar.AppSettings.Monitor.Device.Count} 個分機開始監聽 ...");
            var bytePerFrame = GlobalVar.AppSettings.Monitor.AudioBytesPerFrame;
            foreach (var dev in GlobalVar.AppSettings.Monitor.Device) {
                var liveMon = new LiveMonitorModel(ENUM_PayloadType.PT_PCMU, bytePerFrame, 8000);
                GlobalVar.DictLiveMonitor.Add(dev.Extn, liveMon);
            }
            #endregion

            //啟動 DispatchPacketThread
            var dispatchPacket = new DispatchPacketThread();
            dispatchPacket.StartThread();

            #region 啟動每一個分機的封包解析 ParsePacketThread + MakeFileThread
            nlog.Info($"總共有 {GlobalVar.AppSettings.Monitor.Device.Count} 個分機開始錄音 ...");
            
            var deviceCount = 0;
            foreach (var dev in GlobalVar.AppSettings.Monitor.Device) {
                // 控制錄音授權的數量
                if (deviceCount >= GlobalVar.LicenseModel.SynipPort)
                    break;
                deviceCount++;
                #region 啟動 ParsePacketThread
                var parsePacket = new ParsePacketThread(dev);
                // parsePacket 加入 Dictionary 中， key = dev.Extn
                GlobalVar.DictParseThread.Add(dev.Extn, parsePacket);
                parsePacket.StartThread();
                
                while (true) { // 此處要等待所有的 ParseThread 全部啟動完以後才可以往下 ...
                    if (parsePacket.State == WorkerState.Running)
                        break;                 
                    Thread.Sleep(1);
                }
                #endregion

                #region 啟動 MakeFileThread
                var makeFile = new MakeFileThread(dev);
                // makeFile 加入 Dictionary 中， key = dev.Extn
                GlobalVar.DictMakeFileThread.Add(dev.Extn, makeFile);
                makeFile.StartThread();

                while (true) {// 此處要等待所有的 ParseThread 全部啟動完以後才可以往下 ...
                    if (makeFile.State == WorkerState.Running)
                        break;
                    Thread.Sleep(1);
                }
                #endregion
            }
            #endregion

            nlog.Info($"*** 共 {deviceCount} 分機開始錄音 ... ***");            

            #region 啟動監控網路卡的 Thread 來讀取封包: ReadPacketThread，*** 支援多網卡 ***
            nlog.Info($"總共監控 {GlobalVar.MonitorPcapIndex.Count} 個網路介面 ...");
            List<ReadPacketThread> listReadThread = new();
            foreach (var index in GlobalVar.MonitorPcapIndex) {
                var dev = GlobalVar.MonitorPcapDevice[index];
                var pcapModel = new PcapDeviceModel(dev);
                nlog.Info($"\t index={index}: name={dev.Name}, mac={dev.MacAddress}, name={pcapModel.GetFriendlyName()}, ipAddr={pcapModel.GetIPV4()}, description={dev.Description}");

                var readPacket = new ReadPacketThread(index);
                listReadThread.Add(readPacket);
                readPacket.StartThread();
            }
            #endregion            

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            var time_ProcessPacket = DateTime.Now;
            var time_TalkingNotResponse = DateTime.Now;
            var time_HoldNotResponse = DateTime.Now;
            var time_LongWaitingDialog = DateTime.Now;

            var demoStr = GlobalVar.LicenseModel.DemoExpired.HasValue ? $"Demo版本:{GlobalVar.LicenseModel.DemoExpired.Value.ToString("yyyy-MM-dd")}" : "正式版";
            GlobalVar.SendAPI_WriteSystemLog(ENUM_LogType.Info, $"系統啟動({demoStr})...共 {deviceCount} 分機開始錄音");

            while (!stoppingToken.IsCancellationRequested) {
                #region 列印處理封包數
                if (TimeIsUp(GlobalVar.AppSettings.WorkerOption.ProcessIntervalSec, ref time_ProcessPacket)) {                                        
                    foreach (var readPacket in listReadThread) {
                        nlog.Info($"{readPacket.Tag} 已經處理 {readPacket.TotalPacket:n0} 封包");
                    }
                    nlog.Info($"Global.DispatchPacketQueue.Count = {GlobalVar.GetDispatchPacketQueueCount():n0}");
                    nlog.Info($"");
                }
                #endregion

                #region 處理逾期封包回應的 Dialog
                if (TimeIsUp(45, ref time_TalkingNotResponse)) {
                    nlog.Info($"檢查並終止未回應的 Talking Dialog ...");
                    var ret = dispatchPacket.ForceCloseTalkingNotResponse();
                    nlog.Info($"\t 發現 {ret} 個 Dialog 逾期封包回應");
                    nlog.Info($"");
                }
                #endregion

                #region 處理逾期封包回應的 Dialog
                if (TimeIsUp(120, ref time_HoldNotResponse)) {
                    nlog.Info($"檢查並終止未回應的 Hold Dialog ...");
                    var ret = dispatchPacket.ForceCloseHoldNotResponse();
                    nlog.Info($"\t 發現 {ret} 個 Dialog 逾期封包回應");
                    nlog.Info($"");
                }
                #endregion

                #region 處理逾期 Waiting 的 Dialog(10 分鐘檢查一次)
                if (TimeIsUp(600, ref time_LongWaitingDialog)) {
                    nlog.Info($"檢查 init/waiting 太久的 Dialog ...");
                    var ret = dispatchPacket.ClearLongWaintingDialog();
                    nlog.Info($"\t 發現 {ret} 個 Dialog init/waiting 逾期");
                    nlog.Info($"");
                }
                #endregion
                await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);                
            }

            // 通知 ReadThread 結束
            //foreach (var thd in listReadThread) {
            //    thd.RequestStop();
            //}

            //// 通知 ParseThread 結束
            //foreach (var thd in listParseThread) {
            //    thd.RequestStop();
            //}
            // 此處也必須等待所有的 Tnread 都離開。
        }

        private bool CheckSystemSettings(out string checkErr) {
            checkErr = "";
            if (!CheckLicense(out string licErr)) {
                checkErr = $"{licErr}, 服務停止!";                                
                CreateLicenceKey();
                return false;
            }

            if (GlobalVar.LicenseModel.SynipPort == 0) {
                checkErr = $"無錄音授權, 服務停止!";                
                CreateLicenceKey();
                return false;
            }

            #region 檢查 appsettings，設定有錯誤則 ExecuteAsync return，服務不繼續，應該會停止
            if (!CheckAndCreateRecFolder(out string err)) {
                checkErr = $"{err}, 服務停止!";                
                return false;
            }

            if (lib_misc.IsNullOrEmpty(GlobalVar.AppSettings.Monitor.Device)) {
                checkErr = $"監控設備(appsettings.Monitor.Device)設定錯誤, 服務停止!";             
                return false;
            }
            var sipProto = new List<string>() { "tcp", "udp" };
            if (!sipProto.Contains(GlobalVar.AppSettings.Monitor.SipProtocol.ToLower())) {
                checkErr = $"監控設備(appsettings.Monitor.SipProtocol)設定錯誤(tcp/udp), 服務停止!";                
                return false;
            }
            var monType = new List<string>() { "ip", "mac" };
            if (!monType.Contains(GlobalVar.AppSettings.Monitor.MonType.ToLower())) {
                checkErr = $"監控設備(appsettings.Monitor.MonType)設定錯誤(ip/mac), 服務停止!";                
                return false;
            }
            if (GlobalVar.MonitorPcapIndex.Count <= 0) {
                checkErr = $"AppSettings.NetworkInterface 設定錯誤，從目前系統中對應不到要監控的網路介面，服務停止!";                
                return false;
            }

            #region 矯正 AppSettings 中 MAC 的格式, 從 PCAP 封包取得的 MAC Address = 6C5E3B87C0BD，而且大寫
            foreach (var mon in GlobalVar.AppSettings.Monitor.Device) {
                if (!string.IsNullOrEmpty(mon.MacAddr)) {
                    mon.MacAddr = mon.MacAddr.Replace("-", "").Replace(":", "").ToUpper(); // 去除 - : 並大寫
                }
            }
            #endregion

            return true;
            #endregion
        }

        private bool CheckMonitorDevice() {
            var ret = true;            
            GlobalVar.AppSettings.Monitor.MonitorType = GlobalVar.AppSettings.Monitor.MonType.ToLower() == "ip"
                        ? ENUM_SIP_MonitorType.IP
                        : ENUM_SIP_MonitorType.MAC;
            var orgMonCount = GlobalVar.AppSettings.Monitor.Device.Count;
            nlog.Info($"檢查監控錄音分機設定 ..., monType={GlobalVar.AppSettings.Monitor.MonitorType}, 監控數量={orgMonCount}");
            // 檢查 分機設定 是否重複
            var extnList = GlobalVar.AppSettings.Monitor.Device.Select(x => x.Extn).Distinct().ToList(); // 取不重複的分機清單
            for (var i = GlobalVar.AppSettings.Monitor.Device.Count - 1; i >= 0; i--) {
                var dev = GlobalVar.AppSettings.Monitor.Device[i];

                if (string.IsNullOrEmpty(dev.Extn.Trim())) // 移除空白
                    GlobalVar.AppSettings.Monitor.Device.Remove(dev);
                else if (GlobalVar.AppSettings.Monitor.Device.Count(x => x.Extn == dev.Extn) > 1) // 移除重複
                    GlobalVar.AppSettings.Monitor.Device.Remove(dev);
            }

            // 移除 IP 空白或重複
            if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.IP) {
                var ipaddrList = GlobalVar.AppSettings.Monitor.Device.Select(x => x.IpAddr).Distinct().ToList(); // 取不重複的 IpAddr 清單
                for (var i = GlobalVar.AppSettings.Monitor.Device.Count - 1; i >= 0; i--) {
                    var dev = GlobalVar.AppSettings.Monitor.Device[i];

                    if (string.IsNullOrEmpty(dev.IpAddr.Trim())) // 移除空白
                        GlobalVar.AppSettings.Monitor.Device.Remove(dev);
                    else if (GlobalVar.AppSettings.Monitor.Device.Count(x => x.IpAddr == dev.IpAddr) > 1) // 移除重複
                        GlobalVar.AppSettings.Monitor.Device.Remove(dev);
                }
            }
            // 移除 MAC 空白或重複
            else if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.MAC) {
                var macAddrList = GlobalVar.AppSettings.Monitor.Device.Select(x => x.MacAddr).Distinct().ToList(); // 取不重複的 MacAddr 清單
                for (var i = GlobalVar.AppSettings.Monitor.Device.Count - 1; i >= 0; i--) {
                    var dev = GlobalVar.AppSettings.Monitor.Device[i];

                    if (string.IsNullOrEmpty(dev.MacAddr.Trim())) // 移除空白
                        GlobalVar.AppSettings.Monitor.Device.Remove(dev);
                    else if (GlobalVar.AppSettings.Monitor.Device.Count(x => x.MacAddr == dev.MacAddr) > 1) // 移除重複
                        GlobalVar.AppSettings.Monitor.Device.Remove(dev);
                }
            }

            var actualMonCount = GlobalVar.AppSettings.Monitor.Device.Count;
            if (actualMonCount == orgMonCount)
                nlog.Info($"\t 監控錄音分機設定過濾 OK, 實際監控數量={actualMonCount}");
            else {
                var err = $"預計監控({orgMonCount})與實際監控({actualMonCount})數量有差異，請檢查監控設定!";
                GlobalVar.SendAPI_WriteSystemLog(ENUM_LogType.Error, err);
                nlog.Error($"\t ***{err}");
            }
            return ret;
        }

        private bool CheckAndCreateRecFolder(out string errMsg) {
            errMsg = "";

            if (!Directory.Exists(GlobalVar.RecTempPath)) {
                var err = lib_misc.ForceCreateFolder(GlobalVar.RecTempPath);
                if (err != "") {
                    errMsg = $"錄音路徑(temp)不存在或無法建立({err})";
                    return false;
                }
            }

            if (!Directory.Exists(GlobalVar.RecDataPath)) {
                var err = lib_misc.ForceCreateFolder(GlobalVar.RecDataPath);
                if (err != "") {
                    errMsg = $"錄音路徑(data)不存在或無法建立({err})";
                    return false;
                }
            }
            return true;
        }

        private bool CheckLicense(out string err) {
            err = "";            
            var ret = false;
            if (string.IsNullOrEmpty(GlobalVar.AppSettings.LicenseFile)) {
                err = "no license file assigned";
                return false;
            }
            else if (!System.IO.File.Exists(GlobalVar.AppSettings.LicenseFile)) {
                err = "license file not found";
                return false;
            }
            
            var obj = lib_license.DecodeLicenseFile(GlobalVar.AppSettings.LicenseFile, out int licVer, out string licErr);
            if (obj == null) {
                err = $"license error: {licErr}";
                return false;
            }

            GlobalVar.LicenseModel = obj as LicRegisterEx2Model;
            if (!GlobalVar.CheckLicense(out err)) {
                return false;
            }

            nlog.Info($"license detected:");
            nlog.Info($"\t LicSeq = {GlobalVar.LicenseModel.LicSeq}");
            nlog.Info($"\t LicVer = {GlobalVar.LicenseModel.LicVer}");
            nlog.Info($"\t CustID = {GlobalVar.LicenseModel.CustID}");
            nlog.Info($"\t CustName = {GlobalVar.LicenseModel.CustName}");
            nlog.Info($"\t IP Port 授權 = {GlobalVar.LicenseModel.SynipPort}"); // 跟三匯的 SynIp Port 共用
            nlog.Info($"\t Monitor監控授權 = {GlobalVar.LicenseModel.MonitorCount}");
            nlog.Info($"\t SystemFunction = {GlobalVar.LicenseModel.SystemFunction}");
            nlog.Info($"\t WebFunction = {GlobalVar.LicenseModel.WebFunction}");
            nlog.Info($"\t InstallDateTime = {GlobalVar.LicenseModel.InstallDateTime}");
            nlog.Info($"\t InstallSWName = {GlobalVar.LicenseModel.InstallSWName}");
            nlog.Info($"\t InstallSWVersion = {GlobalVar.LicenseModel.InstallSWVersion}");
            if (GlobalVar.LicenseModel.DemoExpired.HasValue) {
                nlog.Info($"\t Demo版本，到期日 = {GlobalVar.LicenseModel.DemoExpired.Value.ToString("yyyy-MM-dd")}");
            }
            
            return true;
        }

        private void CreateLicenceKey() {
            var key = lib_license.GetLicenceKey(out var err);
            if (string.IsNullOrEmpty(key)) {
                nlog.Error($"failed to create license key: {err}");
                return;
            }

            lib_misc.ForceCreateFolder(GlobalVar.AppSettings.Recording.RecDataPath);
            var fileName = Path.Combine(GlobalVar.AppSettings.Recording.RecDataPath, "ursSipLogger.key");
            try {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(fileName)) {
                    file.WriteLine(key); 
                }
            }
            catch (Exception ex) {
                nlog.Error($"*** 無法產生 license key 檔案({fileName}): {ex.Message}");
            }
            return;

        }
    }
}
