using NLog;
using WorkerThread;
using Project.Models;
using Project.ProjectCtrl;
using Project.Enums;
using Newtonsoft.Json;
using Project.Lib;
using ursSipParser.Models;
using Project.Helpers;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;


namespace ThreadWorker
{
    public class DispatchPacketThread: IWorker
    {
        // protect
        protected volatile bool _shouldStop;
        protected volatile bool _shouldPause;

        // private
        private string Tag;
        private Thread _myThread;

        private NLog.Logger _sipDetailLog;
        private NLog.Logger _sipCmdLog;
        private NLog.Logger _dialogLog;
        private NLog.Logger _rtpLog;        
        
        private SegmentPacket _segmentPacket = new SegmentPacket();
        private ulong _packetIndex = 0;
        private int _audioBytescPerFrame = 0;

        // public
        public WorkerState State { get; internal set; }               

        public DispatchPacketThread() {            
            Tag = "DispatchPacket";

            _sipDetailLog = LogManager.GetLogger($"{Tag}-SIP-Detail");
            _dialogLog = LogManager.GetLogger($"{Tag}-SIP-Dialog");
            _rtpLog = LogManager.GetLogger($"{Tag}-RTP");
            _sipCmdLog = LogManager.GetLogger($"{Tag}-SIP-Command");
            _audioBytescPerFrame = GlobalVar.AppSettings.Monitor.AudioBytesPerFrame;
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
            _sipDetailLog.Info($"{Tag} is waiting to stop(join) ...");
            _myThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }

        public virtual void DoWork(object anObject) {
            _sipDetailLog.Info("");
            _sipDetailLog.Info($"********** DispacketPacket is now starting ... **********");
            State = WorkerState.Running;
            while (!_shouldStop) {                
                GlobalVar.WaitPacketComing.WaitOne(); // <= 要注意這裡是否會造成監聽緩慢

                // 關於 GetDispatchPacket:
                //      1. 如果Queue沒有封包，WaitPacketComing 會 reset，這裡的 while loop 會卡在 WaitOne()
                //      2. 如果Queue有封包，WaitPacketComing 會 set，WaitOne() 會跳出、往下跑
                var packetInfo = GlobalVar.GetDispatchPacket(); 
                if (packetInfo != null) {
                    ProcessPacketInfo(packetInfo);
                }
            }
            _sipDetailLog.Info($"========== DispacketPacket terminated. ==========");
            State = WorkerState.Stopped;
        }
        
        private void ProcessPacketInfo(PacketInfoEx packetInfo) {
            // sip command by TCP(看 appsettings.json 設定)
            if (packetInfo.IPType == ENUM_IPType.TCP) { // TCP 封包               
                if (GlobalVar.AppSettings.Monitor.SipProtocol.ToLower() == "tcp") {                    
                    if (packetInfo.CheckIfSipPort()) {                        
                        ProcessSipCommand(ref packetInfo);  //從封包中過濾，產生 SipSdpModel 放入 SipSdpList 中
                    }                    
                }
                return;
            }

            // sip command by UDP(看 appsettings.json 設定)...> 也會有封包分割問題
            // 但如果 udp 不是 sip command，而是 rtp 時(只有 160 或 320 bytes)，理論上不用處理 Frame 分割的問題，因為一般封包超過576才會被分割
            // 參考: https://stackoverflow.com/questions/17938623/tcp-and-udp-segmentation
            if (packetInfo.IPType == ENUM_IPType.UDP) {                                
                if (packetInfo.CheckIfSipPort()) {                    
                    ProcessSipCommand(ref packetInfo); //從封包中過濾，產生 SipSdpModel 放入 SipSdpList 中                    
                }
                else { //處理 RTP          
                    ProcessRtp(ref packetInfo);
                }
            }
        }

        // 0. 處理 SIP Command
        private ENUM_SIPCommand ProcessSipCommand(ref PacketInfoEx packetInfo) {
            // 封包序號，為了解決封包被切割的問題(被切割包必定連續)
            if (_packetIndex >= ulong.MaxValue - 1)
                _packetIndex = 1;
            else
                _packetIndex++;
            // 取得 SIP 資訊
            packetInfo.SetSip();

            #region 處理 tcp/udp 封包因為長度超過 576 被切成兩個 Segment 的問題            
            if (packetInfo.SipCmd == ENUM_SIPCommand.S_Incomplete) {
                _segmentPacket.SetPacket(_packetIndex, packetInfo.PayloadData); // 放入到 _segmentPacket 裡面
                return ENUM_SIPCommand.S_Incomplete;
            }
            else if (_segmentPacket.IsTheSecondSegmentPacket(_packetIndex)) {
                packetInfo.InsertFirstSegment(_segmentPacket.PayloadData);
                _segmentPacket.Reset();
            }
            #endregion

            // 取得具有 SDP 資訊的 SIPCommand
            packetInfo.GetSipCommand();

            #region print out log
            // debug ............................................            
            var s = packetInfo.GetSIPLog();
            if (!string.IsNullOrEmpty(s))
                _sipCmdLog.Info(s);
            // ..................................................

            // 紀錄 SIP Command log
            if (GlobalVar.AppSettings.Logging.SipCommand) {
                _sipDetailLog.Info(packetInfo.GetSIPDetailLog());
            }
            #endregion

            // 要 >=2 (Invite=2, 200OK=3, Bye=4, Ack=5) 以上才會處理
            if (packetInfo.SipCmd <= ENUM_SIPCommand.S_Completed)
                return packetInfo.SipCmd; // 不是 SIPCommand

            packetInfo.SetSdp(); // 因為封包是SDP，所以取得 CallID, SessionID, RemotePartyID, RtpPort 並填入 packetInfo.SDP                
            // 紀錄 SDP log
            if (GlobalVar.AppSettings.Logging.SdpCommand) {
                _dialogLog.Info(packetInfo.GetSDPLog());
            }

            // 取得這的封包對應到監控清單:
            // 因為是 SIP Command，一定是 SipServer 送過來，或送給 SipServer，所以只會有其中一種狀況(SipSvr->設備 or 設備->SipSvr)
            // 不會存在 srcIP 與 dstIP 都是監控分機的狀況
            var monDev = packetInfo.GetMonitorDevice(out ENUM_IPDir ipDir);
            if (monDev == null) {
                _dialogLog.Info("****** 系統錯誤: 此封包無法對應到監控清單中分機 ******");
                return packetInfo.SipCmd;
            }

            #region 處理 SIP Command            

            if (packetInfo.SipCmd == ENUM_SIPCommand.S_Invite) {
                ProcessSipCommand_Invite(monDev.Extn, packetInfo, ipDir);
            }
            else if (packetInfo.SipCmd == ENUM_SIPCommand.S_200ok) {
                ProcessSipCommand_200Ok(monDev.Extn, packetInfo, ipDir);
            }
            else if (packetInfo.SipCmd == ENUM_SIPCommand.S_Ack) {
                ProcessSipCommand_Ack(monDev.Extn, packetInfo, ipDir);
            }
            else if (packetInfo.SipCmd == ENUM_SIPCommand.S_Bye) {
                ProcessSipCommand_Bye(packetInfo);
            }
            #endregion

            WriteSipDialogLog();
            return packetInfo.SipCmd;
        }

        private void ProcessRtp(ref PacketInfoEx pktInfo) {
            // RTP Header = 12 bytes，後面接著才是語音 RawData
            if (pktInfo.Udp.PayloadData.Length <= 12) {
                return;
            }

            // 產生 RTP 物件，含 RTP Header                 
            pktInfo.SetRtp();            
            if (pktInfo.Rtp == null || pktInfo.Rtp.AudioBytes.Length != _audioBytescPerFrame)
                return;

            // 注意:
            // 1. 此時的封包只有 srcIp/srcMac, dstIp/dstMac 及 RTP Header/RawData ...，但是: _sipDialogList 是根據 SDP 產生的，運作上以 IP 為主，MAC 就不用再管。
            // 2. 問題: 有沒有可能通信中，rtpPort 變更，若有，則會有某些 rtp 漏錄，所以可以思考，是不是可以不要判斷 rtpPort，只要 IP 一樣，就錄 rtp(當然要判斷它是不是rtp)...，這個後續再說。            

            // 處理 SrcIp(發話端)            
            var dialog = GlobalVar.SipDialogCtrl.GetDialog(pktInfo.SrcIp, pktInfo.SrcPort, ENUM_SIP_Dialog_Status.Talking);
            if (dialog != null) {
                dialog.TotalPkt++;
                dialog.LastPacketTime = DateTime.Now; //紀錄最後一次取得封包的時間
                // 錄音檔
                FindParserToAddRecRtp(dialog, pktInfo, ENUM_IPDir.Send, ENUM_RTP_RecFlag.Recording);

                // 監聽，這個播放OK                                
                PlayRTP_Send(dialog.ExtNo, pktInfo);

                // 寫 log
                WriteRtpLog(pktInfo);
            }

            // 處理 DstIp(受話端)            
            dialog = GlobalVar.SipDialogCtrl.GetDialog(pktInfo.DstIp, pktInfo.DstPort, ENUM_SIP_Dialog_Status.Talking);
            if (dialog != null) {
                dialog.TotalPkt++;
                dialog.LastPacketTime = DateTime.Now; //紀錄最後一次取得封包的時間
                // 錄音檔
                FindParserToAddRecRtp(dialog, pktInfo, ENUM_IPDir.Recv, ENUM_RTP_RecFlag.Recording);

                // 監聽，這個播放OK                                
                PlayRTP_Recv(dialog.ExtNo, pktInfo);

                // 寫 log
                WriteRtpLog(pktInfo);
            }
        }

        private IPEndPoint GetIpEndPoint(IPAddress ipAddress, int port, out string err) {
            err = "";
            IPEndPoint endPoint;
            try {
                endPoint = new IPEndPoint(ipAddress, port);
            }
            catch (Exception ex) {
                err = ex.Message;
                endPoint = null;
            }
            return endPoint;
        }
        
        /* ========= 測試用 ffmpeg 播放 =========
        private async Task PlayRTP_Send(string extNo, PacketInfoEx packetInfo) {
            if (GlobalVar.DictLiveMonitor.TryGetValue(extNo, out LiveMonitorModel mon)) {
                if (!mon.IsOpened)
                    return;

                var ffmpegPoint = GetIpEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 49001, out string txErr);
                if (mon.SendPlayRtp.Bye) {// 如果已經結束通話，直接送出 rtp
                    await mon.SendPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, mon.TxEndPoint);
                    mon.SendPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, ffmpegPoint);
                }
                else {
                    if (mon.SendPlayRtp.AddBuffer(packetInfo)) {
                        if (mon.SendPlayRtp.GetJitter()) {
                            mon.SendPlayRtp.PlayRTP(mon.TxEndPoint);
                            mon.SendPlayRtp.PlayRTP(ffmpegPoint);
                        }
                    }
                }
            }
        }

        private async Task PlayRTP_Recv(string extNo, PacketInfoEx packetInfo) {
            if (GlobalVar.DictLiveMonitor.TryGetValue(extNo, out LiveMonitorModel mon)) {
                if (!mon.IsOpened)
                    return;
                var ffmpegPoint = GetIpEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 59001, out string txErr);
                if (mon.RecvPlayRtp.Bye) { // 如果已經結束通話，直接送出 rtp
                    await mon.RecvPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, mon.RxEndPoint);
                    mon.RecvPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, ffmpegPoint);
                }
                else {
                    if (mon.RecvPlayRtp.AddBuffer(packetInfo)) {
                        if (mon.RecvPlayRtp.GetJitter()) {
                            mon.RecvPlayRtp.PlayRTP(mon.RxEndPoint);
                            mon.RecvPlayRtp.PlayRTP(ffmpegPoint);
                        }
                    }
                }
            }
        }
        */


        // 送給 VLC 播放很 OK
        private async Task PlayRTP_Send(string extNo, PacketInfoEx packetInfo) {
            if (GlobalVar.DictLiveMonitor.TryGetValue(extNo, out LiveMonitorModel mon)) {
                if (!mon.IsOpened)
                    return;
                if (mon.SendPlayRtp.Bye) // 如果已經結束通話，直接送出 rtp
                    await mon.SendPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, mon.TxEndPoint);
                else {
                    if (mon.SendPlayRtp.AddBuffer(packetInfo)) {
                        if (mon.SendPlayRtp.GetJitter()) {
                            await mon.SendPlayRtp.PlayRTP(mon.TxEndPoint);
                        }
                    }
                }
            }
        }

        // 送給 VLC 播放很 OK
        private async Task PlayRTP_Recv(string extNo, PacketInfoEx packetInfo) {
            if (GlobalVar.DictLiveMonitor.TryGetValue(extNo, out LiveMonitorModel mon)) {
                if (!mon.IsOpened)
                    return;
                if (mon.RecvPlayRtp.Bye) // 如果已經結束通話，直接送出 rtp
                    await mon.RecvPlayRtp.PlayRTPNow(packetInfo.Rtp.AudioBytes, mon.RxEndPoint);
                else {
                    if (mon.RecvPlayRtp.AddBuffer(packetInfo)) {
                        if (mon.RecvPlayRtp.GetJitter()) {
                            await mon.RecvPlayRtp.PlayRTP(mon.RxEndPoint);
                        }
                    }
                }
            }
        }

        // 針對長時間都沒有 RTP 封包的 Dialog，要進行強制結束錄音。        
        public int ForceCloseTalkingNotResponse() {
            var ret = 0;
            var rtpMaxWaitingSec = GlobalVar.AppSettings.Recording.RtpNoResponseTimeoutSec;
            if (rtpMaxWaitingSec < 45)
                rtpMaxWaitingSec = 45;

            _dialogLog.Info($"### checking Talking-Not-Responding dialog ...");
            var notRespList = GlobalVar.SipDialogCtrl.GetTalkingNoResponseList(rtpMaxWaitingSec);            
            if (notRespList != null && notRespList.Count > 0) {
                _dialogLog.Info($"\t 發現 {notRespList.Count}個 dialogs 封包逾時 ...");
                ret = notRespList.Count;                
                foreach (var dialog in notRespList) {
                    _dialogLog.Info($"\t\t >>> 強制 dialog 結束通話...=> \r\n{JsonConvert.SerializeObject(dialog, Formatting.Indented)}");                    
                    FindParserToAddRecRtp(dialog, null, ENUM_IPDir.Unknown, ENUM_RTP_RecFlag.StopRec);
                    _dialogLog.Info($"\t\t\t 強制移除 dialog");
                    GlobalVar.SipDialogCtrl.RemoveDialog(dialog);
                }
            }
            return ret;
        }

        // 針對 (主動)HOLD 以後長時間都沒有回應的 Dialog，要進行強制結束錄音。        
        public int ForceCloseHoldNotResponse() {
            var ret = 0;
            var holdMaxWaitingSec = GlobalVar.AppSettings.Recording.HoldNoResponseTimeoutSec;
            if (holdMaxWaitingSec < 300)
                holdMaxWaitingSec = 300;
            _dialogLog.Info($"### checking Hold-Not-Responding dialog ...");
            var notRespList = GlobalVar.SipDialogCtrl.GetHoldNoResponseList(holdMaxWaitingSec); // 1 hr
            if (notRespList != null && notRespList.Count > 0) {
                _dialogLog.Info($"\t 發現 {notRespList.Count}個 dialogs 封包逾時 ...");
                ret = notRespList.Count;
                foreach (var dialog in notRespList) {
                    _dialogLog.Info($"\t\t >>> 強制 dialog 結束通話...=> \r\n{JsonConvert.SerializeObject(dialog, Formatting.Indented)}");
                    FindParserToAddRecRtp(dialog, null, ENUM_IPDir.Unknown, ENUM_RTP_RecFlag.StopRec);
                    _dialogLog.Info($"\t\t\t 強制移除 dialog");
                    GlobalVar.SipDialogCtrl.RemoveDialog(dialog);
                }
            }
            return ret;
        }

        // 針對 (主動)HOLD 以後長時間都沒有回應的 Dialog，要進行強制結束錄音。        
        public int ClearLongWaintingDialog() {
            var ret = 0;            
            _dialogLog.Info($"### checking long-waiing dialog ...");
            var notRespList = GlobalVar.SipDialogCtrl.GetLongWaitingList(120); // 2 hr
            if (notRespList != null && notRespList.Count > 0) {
                _dialogLog.Info($"\t 發現 {notRespList.Count}個 逾時的 init/waiting dialogs  ...");
                ret = notRespList.Count;
                foreach (var dialog in notRespList) {                    
                    _dialogLog.Info($"\t\t\t 強制移除 dialog: callID={dialog.CallID}, fromTag={dialog.FromTag}, toTag={dialog.ToTag}");
                    GlobalVar.SipDialogCtrl.RemoveDialog(dialog);
                }
            }
            return ret;
        }

        // 透過 sdpModel.ExtNo 到 dictParseThread 找到對應的 ParsePacketThread(parser)，並新增一個 RecRtp 物件
        // recFlag:  用來說明這一包是一般RTP 或是(開始、結束錄音)的訊令
        // ipDir: 指出此封包是屬於監控設備的來源(Src)或目的(Dst)
        private async Task FindParserToAddRecRtp(SipDialogModel dialog, PacketInfoEx packetInfo, ENUM_IPDir ipDir, ENUM_RTP_RecFlag recFlag) {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
            // 用分機快速找到 ParsePacketThread
            if (GlobalVar.DictParseThread.TryGetValue(dialog.ExtNo, out ParsePacketThread parser)) {             
                var recRtp = new RecRtpModel() {
                    DialogID = dialog.ID,
                    PktIndex = 0,
                    IpDir = ipDir,
                    ExtNo = dialog.ExtNo,
                    CallID = dialog.CallID,
                    SessionID = dialog.SessionID,
                    StartTalkTime = dialog.StartTalkTime.Value,                    
                    StopTalkTime = recFlag == ENUM_RTP_RecFlag.StopRec ? DateTime.Now : null,
                    Flag = recFlag,                    
                    Rtp = null,
                };
                if (packetInfo != null ) {
                    recRtp.PktCaptureTime = packetInfo.CaptureTime;
                }

                if (recFlag == ENUM_RTP_RecFlag.StartRec) {
                    // 不論是主叫或被叫，CallerID=FromExt, CalledID=ToExt
                    recRtp.CallerID = dialog.FromExt; 
                    recRtp.CalledID = dialog.ToExt;            
                    recRtp.CallDir = dialog.Invite ? ENUM_CallDirection.Outbound : ENUM_CallDirection.Inbound;
                    _dialogLog.Info($"FindParserToAddRecRtp 開始錄音({recRtp.ExtNo})=> callerID={recRtp.CallerID}, calledID={recRtp.CalledID}, dialogID={recRtp.DialogID}");
                    // 傳送 API
                    var lineStatus = dialog.Invite ? ENUM_LineStatus.Outbound : ENUM_LineStatus.Inbound;
                    GlobalVar.SendAPI_WriteChannelStatus(recRtp, dialog, lineStatus);

                    // 監聽開始 ...
                    if (GlobalVar.DictLiveMonitor.TryGetValue(dialog.ExtNo, out LiveMonitorModel mon)) {
                        if (mon.IsOpened) {
                            mon.SendPlayRtp.Reset();
                            mon.RecvPlayRtp.Reset();
                            _dialogLog.Info($"FindParserToAddRecRtp 開始監聽({recRtp.ExtNo})=> send.Bye={mon.SendPlayRtp.Bye}, recvBye={mon.RecvPlayRtp.Bye}");
                        }
                    }
                }
                else if (recFlag == ENUM_RTP_RecFlag.Recording) {
                    // 是錄音中才有封包                                        
                    recRtp.PktIndex = dialog.TotalPkt; // 標註此封包的順序                                        
                    recRtp.Rtp = new RtpModel(packetInfo.PayloadData, packetInfo.CaptureTime);
                }
                else if (recFlag == ENUM_RTP_RecFlag.StopRec) {                    
                    _dialogLog.Info($"FindParserToAddRecRtp: 結束錄音({recRtp.ExtNo})=> totalPkt={dialog.TotalPkt}, dialogID={recRtp.DialogID}");
                    recRtp.PktIndex = dialog.TotalPkt; // 結束錄音時，帶入封包總數                    
                    recRtp.CallDir = dialog.Invite ? ENUM_CallDirection.Outbound : ENUM_CallDirection.Inbound;
                    if (GlobalVar.DictLiveMonitor.TryGetValue(dialog.ExtNo, out LiveMonitorModel mon)) {                        
                        if (mon.IsOpened) {
                            mon.SendPlayRtp.StopMonitoring(mon.TxEndPoint);
                            mon.RecvPlayRtp.StopMonitoring(mon.RxEndPoint);
                            _dialogLog.Info($"FindParserToAddRecRtp: 結束監聽({recRtp.ExtNo})=> send.Bye={mon.SendPlayRtp.Bye}, recvBye={mon.RecvPlayRtp.Bye}");
                        }
                    }
                    // 傳送 API
                    GlobalVar.SendAPI_WriteChannelStatus(recRtp, dialog,  ENUM_LineStatus.Idle);                    
                }
                else if (recFlag == ENUM_RTP_RecFlag.StartPressToHold) { // 開始主動 Hold                                  
                    // do nothig
                }
                else if (recFlag == ENUM_RTP_RecFlag.StartBeHeld) { // 開始被動 Hold                    
                    // do nothig
                }
                else if (recFlag == ENUM_RTP_RecFlag.StopHeld) { // 結束 Hold
                    // do nothig
                }
                parser.AddRecRtp(recRtp);
            }
        }        
        

        // 1. 處理 SIP Command: Invite ---
        // --- 主叫端: 主動送出 Invite 給 Server (srcIP 是自己)，此時有 callID，fromTag，但 toTag 是空值，rtpPort 是對外通信的 port
        private SipDialogModel ProcessSipCommand_Invite(string extNo, PacketInfoEx packetInfo, ENUM_IPDir ipDir) {
            var sdpInfo = "";
            if (packetInfo.Sdp != null)
                sdpInfo = packetInfo.Sdp.GetInfo();
            _dialogLog.Info($"ProcessSipCommand_Invite: {sdpInfo}");
            var symbol = $"===> {ipDir.ToDescription()}({extNo})";

            SipDialogModel dialog = null;            
            if (ipDir == ENUM_IPDir.Send) {
                // 處理第 1 次 Invite，此時有 fromTag，但無 toTag
                if (packetInfo.Sip.FromTag != "" && packetInfo.Sip.ToTag == "") {
                    _dialogLog.Info($"{symbol}: 分機主動撥出(Invite) ...");
                    dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID);
                    if (dialog == null) {                        
                        GlobalVar.SipDialogCtrl.CreateAndInsertDialog(extNo, true, packetInfo.SrcIp, packetInfo.SrcMac, packetInfo);
                        _dialogLog.Info($"\t callID 不存在，新增 dialog 物件({JsonConvert.SerializeObject(dialog)})");                        
                    }
                    else {
                        _dialogLog.Error($"\t 注意***，Call-ID 已經存在，屬於 In-Dialog 的處理模式!");                        
                    }
                    return dialog;
                }            
                // 處理 In-Dialog
                dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, packetInfo.Sip.ToTag, ENUM_SIP_Dialog_Status.Talking); 
                if (dialog != null) {
                    _dialogLog.Info($"{symbol}: Invite 的 In-Dialog 處理 =>");
                    if (packetInfo.Sdp != null && packetInfo.Sdp.SendOnly) { // 開始 Press Hold(主動Hold)
                        _dialogLog.Info($"{symbol}: Invite-PressHold: 加入 \"開始Hold(主動)\" 的 RecRtp ...");
                        FindParserToAddRecRtp(dialog, packetInfo, ipDir, ENUM_RTP_RecFlag.StartPressToHold);
                        dialog.PressToHold();                        
                    }
                    else if (packetInfo.Sdp != null && packetInfo.Sdp.SendRecv) { // Press Hold(主動Hold) 抓回，
                        _dialogLog.Info($"{symbol}: Invite-StopHeld: 加入 \"取消Hold(主動)\" 的 RecRtp ...");
                        FindParserToAddRecRtp(dialog, packetInfo, ipDir, ENUM_RTP_RecFlag.StopHeld);
                        dialog.CancelHold();                        
                    }
                }
            }                        
            return dialog;
        }

        // 2. 處理 SIP Command: 200OK
        // --- 有兩種狀況:
        //      1. 主叫端: 主叫端 Invite 後, server 主動回 200OK
        //      2. 被叫端: 被呼叫時，主動回 200OK 給 server
        private SipDialogModel ProcessSipCommand_200Ok(string extNo, PacketInfoEx packetInfo, ENUM_IPDir ipDir) {
            var sdpInfo = "";
            if (packetInfo.Sdp != null)
                sdpInfo = packetInfo.Sdp.GetInfo();
            _dialogLog.Info($"ProcessSipCommand_200Ok: {sdpInfo}");
            var symbol = $"===> {ipDir.ToDescription()}({extNo})";
            
            SipDialogModel dialog = null;

            // 1. 先判斷這個 200OK 是否為主叫端分機送出 Invite 後，由 server 回應的 200OK? 重點是此時的 Dialog，toTag = "" 且 Status = waiting                
            dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, "", ENUM_SIP_Dialog_Status.Waiting); 
            if (dialog != null) {
                if (ipDir == ENUM_IPDir.Recv) {
                    GlobalVar.SipDialogCtrl.SetStartTalking(ref dialog, packetInfo.Sip.ToTag); // 此處會指定 DialogID
                    _dialogLog.Info($"{symbol}: 主叫端 Invite 後，Server 回傳的 200Ok，開始通話，找到 dialog 物件=>{JsonConvert.SerializeObject(dialog)}");
                    FindParserToAddRecRtp(dialog, null, ENUM_IPDir.Unknown, ENUM_RTP_RecFlag.StartRec); // 傳送開始錄音訊號                
                }
                else {
                    _dialogLog.Error($"{symbol}: 這應該是主叫端 Invite 後，Server 後回傳的 200Ok，但是 ipDir 不是 ENUM_IPDir.Recv");
                }
                return dialog;
            }

            // 2. 上面一段已經判斷，如果不是主叫的 200OK，那就是被叫的 200OK(FromTag、ToTag都會有值，要建立一個新的 dialog)
            dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, packetInfo.Sip.ToTag); 
            if (dialog == null) {
                // 對應到被叫端，要先建立一個 dialog 物件；被叫端被呼叫時，主動回 200OK 給 server，所以自己是 Send
                if (ipDir == ENUM_IPDir.Send) {
                    GlobalVar.SipDialogCtrl.CreateAndInsertDialog(extNo, false, packetInfo.SrcIp, packetInfo.SrcMac, packetInfo);
                    _dialogLog.Info($"{symbol}: 被叫端在收到 server 的 invite 後，主動回覆 200OK 給 Server，新增 dialog 物件=>{JsonConvert.SerializeObject(dialog)}");
                    return dialog;
                }
                return null;
            }
            // 有找到 dialog，處理 200OK 的 in-dialog
            _dialogLog.Info($"{symbol}: 200OK 的 In-Dialog 處理 =>");
            if (ipDir == ENUM_IPDir.Send) {
                if (packetInfo.Sdp != null && packetInfo.Sdp.SendRecv) {
                    _dialogLog.Info($"{symbol}: 200OK-StopHeld: 加入 \"取消Hold(被動)\" 的 RecRtp ...");
                    FindParserToAddRecRtp(dialog, packetInfo, ipDir, ENUM_RTP_RecFlag.StopHeld);
                    dialog.CancelHold();
                }
            }
            else if (ipDir == ENUM_IPDir.Recv) {
                // do nothing                
            }
            return dialog;            
        }

        // 3. 處理 SIP Command: Ack
        // --- 被叫端，Server 回 ACK (自己是 dstIP)
        private SipDialogModel ProcessSipCommand_Ack(string extNo, PacketInfoEx packetInfo, ENUM_IPDir ipDir) {
            var sdpInfo = "";
            if (packetInfo.Sdp != null)
                sdpInfo = packetInfo.Sdp.GetInfo();
            _dialogLog.Info($"ProcessSipCommand_Ack: {sdpInfo}");            
            var symbol = $"===> {ipDir.ToDescription()}({extNo})";

            SipDialogModel dialog = null;
            #region 處理被叫端 200OK 之後的 Ack
            if (ipDir == ENUM_IPDir.Recv) {
                dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, packetInfo.Sip.ToTag, ENUM_SIP_Dialog_Status.Waiting); // 對應到被叫端，應該要找到之前建立的 dialog 物件
                if (dialog != null) {
                    GlobalVar.SipDialogCtrl.SetStartTalking(ref dialog);  //此處會指定 DialogID
                    _dialogLog.Info($"{symbol}: 被叫端送出 200OK 後， server 回覆的 ACK，開始通話，找到 dialog 物件=>{JsonConvert.SerializeObject(dialog)}");                 
                    FindParserToAddRecRtp(dialog, null, ENUM_IPDir.Unknown, ENUM_RTP_RecFlag.StartRec); // 開始錄音的訓令，不用分 Src 或 Dst
                    return dialog;
                }
            }
            #endregion
            
            dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, packetInfo.Sip.ToTag, ENUM_SIP_Dialog_Status.Talking);
            if (dialog != null) {
                _dialogLog.Info($"{symbol}: Ack 的 In-Dialog 處理 =>");
                if (packetInfo.Sdp != null && packetInfo.Sdp.SendOnly && ipDir == ENUM_IPDir.Recv) {
                    _dialogLog.Info($"{symbol}: Ack-StopHeld: 加入 \"開始Hold(被動)\" 的 RecRtp ...");
                    FindParserToAddRecRtp(dialog, packetInfo, ipDir, ENUM_RTP_RecFlag.StartBeHeld);
                    dialog.SetBeHold();
                }            
            }            
            return dialog; // return 不重要
        }

        // 4. 處理 SIP Command: Bye        
        private SipDialogModel ProcessSipCommand_Bye(PacketInfoEx packetInfo) {
            _dialogLog.Info($"ProcessSipCommand_Bye:");
            var dialog = GlobalVar.SipDialogCtrl.GetDialog(packetInfo.Sip.CallID, packetInfo.Sip.FromTag, packetInfo.Sip.ToTag); // 對應到被叫端，要先建立一個 dialog 物件
            if (dialog != null) {
                // 傳送結束錄音訊號，不需要分 Src/Dst，因為分機如果 SIP:Bye，則 Send/Recv 封包會同時停止。
                FindParserToAddRecRtp(dialog, null, ENUM_IPDir.Unknown, ENUM_RTP_RecFlag.StopRec);
                
                _dialogLog.Info($"===> 分機={dialog.ExtNo}，找到 dialog 物件=>{JsonConvert.SerializeObject(dialog)}\r\n=> 移除 dialog，結束通話");                
                GlobalVar.SipDialogCtrl.RemoveDialog(dialog);
                return dialog;
            }
            else {
                _dialogLog.Info($"===> BYE sip 找不到對應的 CallID + FromTag + ToTag");
                return null;
            }
        }

        public void RequestStop() {
            _sipDetailLog.Info($"{Tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }       

        //TODO: SipDialogModel 如果一直都沒有被消除，或一直都沒有 StartTalking ...，要自動刪除它


        #region 其他較為不重要的 function
        private async Task WriteRtpLog(PacketInfoEx pktInfo) {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
            #region 寫 RTP log
            if (GlobalVar.AppSettings.Logging.RtpHeader)
                _rtpLog.Info(pktInfo.GetRTPLog());

            // debug RTP Header (12)bytes                        
            if (GlobalVar.AppSettings.Logging.RtpHeaderHex)
                _rtpLog.Info(pktInfo.Rtp.GetHeaderHex());
            #endregion
        }

        // 會有太多 Dialog，所以不顯示了。
        private void WriteSipDialogLog() {
            var dialogList = GlobalVar.SipDialogCtrl.GetDialogList();
            var s = $"===> {dialogList.Count} 個 dialog: \r\n";
            var index = 0;
            foreach (var dialog in dialogList) {
                index++;
                s = s + $"\t@{index:D3}: {JsonConvert.SerializeObject(dialog)}\r\n";
            }
            _dialogLog.Info(s + "\r\n\r\n\r\n");
        }
        #endregion
    }

}
