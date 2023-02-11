using Project.Database;
using Project.Helper;
using Project.AppSetting;
using Project.ProjectCtrl;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;
using Project.Lib;
using System.Net.Sockets;
using System.Net;
using WorkerThread;
using Project.Models;
using ThreadWorker;

namespace Project.WorkerService {
    class CommandWorker : BaseWorker {

        private UdpListener _udpServer = null;
        private IPEndPoint _serverEndPoint = null;
        private Socket _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        public override string className => GetType().Name;

        public CommandWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger(className);
        }

        private bool Init() {
            nlog.Info($"\t try to bind Command(UDP) listening ...(port={GlobalVar.AppSettings.CommandPort})");
            var ret = true;
            _serverEndPoint = lib_misc.GetIpEndPoint(IPAddress.Any, GlobalVar.AppSettings.CommandPort, out string err);
            if (_serverEndPoint == null) {
                nlog.Info($"\t\t IPEndPoint error: {err}");
                ret = false;
            }
            else {
                try {
                    _udpServer = new UdpListener(_serverEndPoint);
                }
                catch (Exception ex) {
                    nlog.Info($"\t\t Command(UDP) listener raised an exception: {ex.Message}");
                    ret = false;
                }
            }
            if (ret)
                nlog.Info($"\t Command(UDP) server bind ok({_serverEndPoint})");

            return ret;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute * 1000, stoppingToken);

            nlog.Info($"{className} ExecuteAsync starting ...");
            if (!Init()) {
                nlog.Info("********** process init failed. Job terminated. **********");                
                return;
            }

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            DateTime checkTime = DateTime.Now;            

            while (!stoppingToken.IsCancellationRequested) {                

                // 等待時間是否已到
                if (!TimeIsUp(GlobalVar.AppSettings.WorkerOption.ProcessIntervalSec, ref checkTime)) {
                    await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);
                    continue;
                }
                nlog.Info($"");
                nlog.Info($"");
                try {
                    #region Do your work
                    await DoJob();
                    #endregion
                }
                catch (Exception ex) {
                    nlog.Info($"執行工作發生錯誤：{ex.Message}");
                }                
                await Task.Delay(100, stoppingToken);
            }
        }

        private async Task DoJob() {
            try {
                var received = await _udpServer.Receive();
                nlog.Info("\r\n\r\n");
                nlog.Info($"received data from {received.Sender}, length={received.DataLen}");
                if (string.IsNullOrEmpty(received.Message.Trim()))
                    return;

                var model = GetCommandModel(received.Message);
                if (model == null)
                    return;

                nlog.Info($"===> Get command:\r\n{JsonConvert.SerializeObject(model, Formatting.Indented)}");
                try {
                    ProcessCommand(model);
                }
                catch (Exception ex) {
                    nlog.Info($"ProcessCommand raised exception: {ex.Message}");
                }
            }
            catch (Exception ex) {
                nlog.Info($"process udp packet raise an exception: {ex.Message}");                
            }
        }


        private LoggerCommandModel GetCommandModel(string jsonStr) {
            LoggerCommandModel model = null;
            try {
                model = JsonConvert.DeserializeObject<LoggerCommandModel>(jsonStr);
            }
            catch (Exception ex) {
                model = null;
                nlog.Info($"\t LoggerCommandModel parsing failed: {ex.Message}");
            }
            return model;
        }

        // 1. LoggerCommandModel中，目前只用到 command + extNo 而已
        // 2. 用分機找到對應的 LiveMonitorModel，對該 LiveMonitorModel 進行 StartMonitor/StopMonitor
        // 3. 重複下 StartMonitor 是必要的，因為要在時間內 Renew
        // 4. 如果逾時不 renew，ScanThread 中，每隔一段時間會呼叫 LiveMonitorModel.CheckRenew() 來自動讓 IsOpened 變為 false
        private void ProcessCommand(LoggerCommandModel model) {
            if (string.IsNullOrEmpty(model.ExtNo))
                return;
            // 檢查帳號密碼，先暫時略過...

            // 先暫時只能有 1 個監聽，如果第 2 個人進來，要擋掉
            if (GlobalVar.DictLiveMonitor.TryGetValue(model.ExtNo, out LiveMonitorModel mon)) {
                if (model.Command.ToUpper() == "StartMonitor".ToUpper()) {
                    nlog.Info($"設定分機({model.ExtNo}) 開始監聽...");
                    var ret = mon.StartMonitor(model, out string msg);
                    if (mon.IsOpened) {
                        mon.SendPlayRtp.Reset(); // 設定 bye = false, bufferLen = 0
                        mon.RecvPlayRtp.Reset();
                        nlog.Info($"\t 開始監聽 => extNo={model.ExtNo}, send.Bye={mon.SendPlayRtp.Bye}, recvBye={mon.RecvPlayRtp.Bye}");
                    }
                    nlog.Info($"\t 分機({model.ExtNo}) 開始監聽..., ret={ret}, msg={msg} ");
                }
                else if (model.Command.ToUpper() == "StopMonitor".ToUpper()) {
                    mon.StopMonitor();
                    mon.SendPlayRtp.Reset(); // 設定 bye = false, bufferLen = 0
                    mon.RecvPlayRtp.Reset();
                    nlog.Info($"設定分機({model.ExtNo}) 停止監聽...(ret={mon.IsOpened})");
                }
            }
            else {
                // 回應錯誤...目前不需要
            }
        }
    }
}
