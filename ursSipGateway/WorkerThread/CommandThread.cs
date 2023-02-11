using NLog;
using WorkerThread;
using Project.Models;
using Project.ProjectCtrl;
using Project.Enums;
using Newtonsoft.Json;
using Project.Lib;
using ursSipParser.Models;
using Project.Helpers;
using WebSocketSharp;
using SIPSorcery.SIP;
using NLog.Fluent;
using System.Net.Sockets;
using System.Net;
using static ThreadWorker.MonitorPacketThread;

namespace ThreadWorker
{
    public class CommandThread: IWorker
    {
        // protect
        protected volatile bool _shouldStop;
        protected volatile bool _shouldPause;

        // private
        private string Tag;
        private Thread _myThread;        
        private NLog.Logger _nLog;

        private UdpListener _udpServer = null;
        private IPEndPoint _serverEndPoint = null;
        private Socket _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // public
        public WorkerState State { get; internal set; }               

        public CommandThread() {            
            Tag = "Command";
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
            _nLog.Info($"{Tag} is waiting to stop(join) ...");
            _myThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }

        

        private bool Init() {
            _nLog.Info($"\t try to bind Command(UDP) listening ...(port={GlobalVar.AppSettings.CommandPort})");
            var ret = true;
            _serverEndPoint = lib_misc.GetIpEndPoint(IPAddress.Any, GlobalVar.AppSettings.CommandPort, out string err);
            if (_serverEndPoint == null) {
                _nLog.Info($"\t\t IPEndPoint error: {err}");
                ret = false;
            }
            else {
                try {
                    _udpServer = new UdpListener(_serverEndPoint);
                }
                catch (Exception ex) {
                    _nLog.Info($"\t\t Command(UDP) listener raised an exception: {ex.Message}");
                    ret = false;
                }
            }
            if (ret)
                _nLog.Info($"\t Command(UDP) server bind ok({_serverEndPoint})");

            return ret;
        }

        public virtual async void DoWork(object anObject) {
            _nLog.Info("");
            _nLog.Info($"********** {Tag} is now starting ... **********");
            // 系統初始化
            if (!Init()) {
                _nLog.Info("********** process init failed. Job terminated. **********");
                State = WorkerState.Stopped;
                return;
            }

            _nLog.Info($">>> UDP is listening ...");
            while (!_shouldStop) {
                Task.Delay(1).Wait();
                State = WorkerState.Starting;
                try {
                    var received = await _udpServer.Receive();
                    _nLog.Trace("\r\n\r\n");
                    _nLog.Trace($"received data from {received.Sender}, length={received.DataLen}");
                    if (string.IsNullOrEmpty(received.Message.Trim()))
                        continue;

                    var model = GetCommandModel(received.Message);
                    if (model == null)
                        continue;

                    _nLog.Trace($"===> Get command:\r\n{JsonConvert.SerializeObject(model, Formatting.Indented)}");
                    try {
                        ProcessCommand(model);
                    }
                    catch(Exception ex) {
                        _nLog.Trace($"ProcessCommand raised exception: {ex.Message}");
                    }
                }
                catch (Exception ex) {
                    _nLog.Trace($"process udp packet raise an exception: {ex.Message}");
                    await Task.Delay(200);
                }
                await Task.Delay(500);
            }

            _nLog.Info($"========== {Tag} terminated. ==========");
            State = WorkerState.Stopped;
        }

        private LoggerCommandModel GetCommandModel(string jsonStr) {
            LoggerCommandModel model = null;
            try {
                model = JsonConvert.DeserializeObject<LoggerCommandModel>(jsonStr);
            }
            catch (Exception ex) {
                model = null;
                _nLog.Trace($"\t LoggerCommandModel parsing failed: {ex.Message}");
            }
            return model;
        }

        // 1. LoggerCommandModel中，目前只用到 command + extNo 而已
        // 2. 用分機找到對應的 ParserThread，對該 ParserThread 的 LiveMonitor 進行 StartMonitor/StopMonitor
        // 3. 重複下 StartMonitor 是必要的，因為要在時間內 Renew
        // 4. 如果逾時不 renew，則在ParserThread 中，每隔一段時間會呼叫 LiveMonitor.CheckRenew() 來自動讓 IsOpened 變為 false
        private void ProcessCommand(LoggerCommandModel model) {
            if (string.IsNullOrEmpty(model.ExtNo))
                return;
            // 檢查帳號密碼，先暫時略過...

            // 先暫時只能有 1 個監聽，如果第 2 個人進來，要擋掉
            if (GlobalVar.DictMonitorThread.TryGetValue(model.ExtNo, out MonitorPacketThread monThread)) {
                if (model.Command.ToUpper() == "StartMonitor".ToUpper()) {
                    _nLog.Info($"設定分機({model.ExtNo}) 開始監聽...");
                    var ret = monThread.LiveMonitor.StartMonitor(model, out string msg);
                    _nLog.Info($"\t 分機({model.ExtNo}) 開始監聽..., ret={ret}, msg={msg} ");
                }
                else if (model.Command.ToUpper() == "StopMonitor".ToUpper()) {
                    monThread.LiveMonitor.StopMonitor();
                    _nLog.Info($"設定分機({model.ExtNo}) 停止監聽...(ret={monThread.LiveMonitor.IsOpened})");
                }
            }
            else {
                // 回應錯誤...
            }
        }

        public void RequestStop() {
            _nLog.Info($"{Tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }       

       
    }

}
