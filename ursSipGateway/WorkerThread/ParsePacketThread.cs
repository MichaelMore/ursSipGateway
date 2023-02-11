using NLog;
using SharpPcap;
using PacketDotNet;
using WorkerThread;
using Project.AppSetting;
using Project.Models;
using Project.ProjectCtrl;
using Project.Lib;
using System.Runtime.InteropServices;
using Project.Enums;
using System.Text;
using System.Net.Sockets;
using System.Net;
using Newtonsoft.Json;

// Audio Raw Data 轉 Wav:
// ffmpeg -f mulaw -ar 8000 -i spp_test_01.snd spp_A03.wav
//

namespace ThreadWorker
{
    public class ParsePacketThread: IWorker
    {
        // protect, private
        protected volatile bool _shouldStop;
        protected volatile bool _shouldPause;

        private Logger _nLog;
        private Logger _errorLog;
        private string _tag;
        private static object _queueLock = new object();        
        private Thread _myThread;
        private ManualResetEvent WaitRecRtpComing = new ManualResetEvent(false);

        // 一個分機會有幾個 CallID 同時錄音, 所以要進行管理
        public Dictionary<string, CallRtpModel> DictCallRtp { get; private set; } = new Dictionary<string, CallRtpModel>();

        // public        
        public Queue<RecRtpModel> RecRtpQueue = new Queue<RecRtpModel>(10000); // 10000 是初始化的大小，不是最高限制，超出會自動增加
        public WorkerState State { get; private set; }
        public AppSettings_Monitor_Device MonDevice { get; private set; }

        // constructor
        public ParsePacketThread(AppSettings_Monitor_Device monDev) {
            MonDevice = new AppSettings_Monitor_Device() {                
                IpAddr = monDev.IpAddr,
                MacAddr = monDev.MacAddr,
                Extn = monDev.Extn,
            };

            _tag = $"ParsePkt_{MonDevice.Extn}";
            _nLog = LogManager.GetLogger(_tag);
            _errorLog = LogManager.GetLogger($"{_tag}_Error");
        }        

        public void StartThread() {
            _myThread = new Thread(this.DoWork) {
                IsBackground = true,
                Name = _tag
            };
            State = WorkerState.Starting;
            _myThread.Start();
        }

        public void StopThread() {
            RequestStop(); // stopping ...
            _nLog.Info($"{_tag} is waiting to stop(join) ...");
            _myThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }

        public void AddRecRtp(RecRtpModel recRtp) {
            lock (_queueLock) {
                RecRtpQueue.Enqueue(recRtp);
                WaitRecRtpComing.Set(); // 有封包，要 set，thread 往下跑
            }
        }

        private RecRtpModel GetRecRtp() {
            RecRtpModel recRtp = null;
            lock (_queueLock) { 
                if (RecRtpQueue.Count > 0) {
                    recRtp = RecRtpQueue.Dequeue();
                }
                else {
                    WaitRecRtpComing.Reset(); 
                }
            }
            return recRtp;
        }

        //private void PrepareUdpSocket() {
        //    var host = GlobalVar.AppSettings.LiveMonGateway.Host;
        //    var port = GlobalVar.AppSettings.LiveMonGateway.Port;
        //    _nLog.Info($"建立 Forwarding UDP socket ...{port}@{host}");
        //    try {
        //        _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        //    }
        //    catch (Exception ex) {
        //        _nLog.Error($"\t failed to create UDP socket: {ex.Message}");
        //        _sock = null;
        //    }
        //    if (_sock != null) {                
        //        try {
        //            _gateway = new IPEndPoint(System.Net.IPAddress.Parse(host), port);
        //        }
        //        catch (Exception ex) {
        //            _nLog.Error($"\t failed to bind gateway UDP socket({port}@{host}): {ex.Message}");
        //            _gateway = null;
        //        }
        //    }
        //    if (_sock != null && _gateway != null) {
        //        _nLog.Info($"Forwarding UDP socket 建立成功");
        //    }
        //    else {
        //        _nLog.Error($"Forwarding UDP socket 建立失敗");

        //    }
        //}


        public virtual void DoWork(object anObject) {
            //var checkRenewTime = DateTime.Now;
            _nLog.Info("");
            _nLog.Info($"********** ParsePacket {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr} is now starting ... **********");                        
            while (!_shouldStop) {
                State = WorkerState.Running;
                //Task.Delay(1).Wait();
                //RecRtpModel recRtp = null;
                //lock (_queueLock) { // <== 一定要 lock
                //    if (RecRtpQueue.Count > 0) {
                //        recRtp = RecRtpQueue.Dequeue();
                //    }
                //}

                WaitRecRtpComing.WaitOne(); // <= 要注意這裡是否會造成監聽緩慢
                var recRtp = GetRecRtp();
                if (recRtp != null) {                    
                    ProcessRecRtp(recRtp);                    
                }
            }// end while
            _nLog.Info($"========== ParsePacket {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr}] terminated. ==========");
            State = WorkerState.Stopped;
        }


        /// <summary>
        /// 開始錄音時，建立一個新的 CallRtpModel 用來寫錄音檔 
        /// Dictionary Key = DialogID = CallID_StartTalkingHHmmss
        /// </summary>
        /// <param name="recRtp">RecRtpModel</param>        
        /// <returns>RecRtpModel</returns>        
        private CallRtpModel CreateCallRtpModel(RecRtpModel recRtp) {
            var maxBytesToWrite = GlobalVar.AppSettings.Monitor.AudioRawFreshSize; // 寫 raw 檔的 buffer 大小
            var samplingRates = GlobalVar.AppSettings.Monitor.AudioBytesPerSec; // 取樣頻率
            var bytesPerFrame = GlobalVar.AppSettings.Monitor.AudioBytesPerFrame; // 每一個 Frmae 的 Bytes

            var tempPath = Path.Combine(GlobalVar.RecTempPath, DateTime.Now.ToString("yyyyMMdd"));
            var callRtp = new CallRtpModel(tempPath, recRtp, samplingRates, bytesPerFrame, maxBytesToWrite);

            // ditCallRtp 的 Key = DialogID
            DictCallRtp.Add(recRtp.DialogID, callRtp);
            return callRtp;
        }


        /// <summary>
        /// 處理 RecRtpQueue 中的 RecRtpModel
        ///     1. 若是開始錄音: 建立 CallRtpModel 放入 dictCallRtp
        ///     2. 若是錄音中: 從 dictCallRtp 中找到對應的 CallRtp，寫入 RTP 封包
        ///     3. 若是結束錄音:
        ///         3.1 從 dictCallRtp 中找到對應的 CallRtp，Close CallRtp 物件(send+recv)
        ///         3.2 發出 MakeRecordingFile 命令
        ///             3.2.1 建立 RecFileModel
        ///             3.2.2 複製 CallRtp SSRCModel List(send+recv)
        ///             3.2.3 Add 到 RecFileQueue
        ///         3.3 移除 CallRtp 物件
        /// </summary>
        /// <param name="recRtp"></param>
        private void ProcessRecRtp(RecRtpModel recRtp) {
            var errMsg = "";
            
            var pktCaptureTimeStr = recRtp.PktCaptureTime.HasValue ? recRtp.PktCaptureTime.Value.ToTimeStr(":", 6) : "";

            CallRtpModel callRtp = null;
            // 1. 開始錄音：利用 DialogID 建立新的 CallRtp
            if (recRtp.Flag == ENUM_RTP_RecFlag.StartRec) {
                _nLog.Info($"### 開始錄音(建立 CallRtp)...=> 分機={recRtp.ExtNo}, pktTime={pktCaptureTimeStr}, dialogID={recRtp.DialogID}, startTalk={recRtp.StartTalkTime.ToString("yyyy/MM/dd HH:mm:ss")}");
                // 新的 dialog，定義 => callID + fromTag + toTag 的組合，但是 fromTag + toTag 可以互換                
                CreateCallRtpModel(recRtp);
            }
            // 2. 錄音中: 利用 DialogID 找對 CallRtp，寫入 RTP 封包(WritePacket)
            else if (recRtp.Flag == ENUM_RTP_RecFlag.Recording) {
                if (DictCallRtp.TryGetValue(recRtp.DialogID, out callRtp)) {                    
                    // 發送音
                    if (recRtp.IpDir == ENUM_IPDir.Send) {
                        if (callRtp.WritePacket(ENUM_IPDir.Send, recRtp.Rtp, out errMsg)) {                            
                            if (!string.IsNullOrEmpty(errMsg))
                                _errorLog.Error($"send.callRtp.WritePacket error: {errMsg}");
                            WriteRecRtpLog(recRtp, $"Send_RtpSeq={callRtp.SendRtpSeq}");
                        }
                    }

                    // 接收音
                    else if (recRtp.IpDir == ENUM_IPDir.Recv) {
                        if (callRtp.WritePacket(ENUM_IPDir.Recv, recRtp.Rtp, out errMsg)) {                            
                            if (!string.IsNullOrEmpty(errMsg))
                                _errorLog.Error($"recv.callRtp.WritePacket error: {errMsg}");                            
                            WriteRecRtpLog(recRtp, $"Recv_RtpSeq={callRtp.RecvRtpSeq}");
                        }
                    }
                }
            }

            // 3. 結束錄音：利用 DialogID 找對 CallRtp，
            //      3.1 Close CallRtp (send & recv)
            //      3.2 製作錄音檔 (MakeRecordingFile)
            //      3.3 移除 CallRtp            
            else if (recRtp.Flag == ENUM_RTP_RecFlag.StopRec) {
                if (DictCallRtp.TryGetValue(recRtp.DialogID, out callRtp)) {
                    _nLog.Info($">>> 結束錄音...=> 分機={recRtp.ExtNo}, pktTime={pktCaptureTimeStr}, dialogID={recRtp.DialogID} ...");

                    // 此處一但發現某分機結束通話(SIP: Bye)，必須同時關閉該分機的 Send + Recv 封包的寫入。
                    #region 關閉 Send，產生 ~send.ssrc 檔案 + 寫入 buffer 中最後的 rtp => send.raw 檔案
                    _nLog.Info($"\t @@@關閉分機={recRtp.ExtNo} 產生 snd 檔案, fileSize={callRtp.SendRaw.TotalSize}, 共寫入={callRtp.SendRaw.TotalPkt}封包");
                    var ssrcList = callRtp.DictSendSSRC.Select(p => p.Value).ToList();
                    foreach (var ssrc in ssrcList) {
                        _nLog.Info($"\t\t ={ssrc.SSRC}: firstCapture={ssrc.FirstCaptureTime.ToTimeStr(":", 3)}, totalPacket={ssrc.TotalPacket}, playTime={ssrc.TotalPacket * (ulong)ssrc.FrameMilliSec / 1000.00}");
                    }
                    callRtp.Close(ENUM_IPDir.Send, out errMsg);
                    if (!string.IsNullOrEmpty(errMsg))
                        _errorLog.Error($"send.callRtp.Close error: {errMsg}");
                    #endregion

                    #region 關閉 Recv，產生 ~recv.ssrc 檔案 + 寫入 buffer 中最後的 rtp => recv.raw 檔案
                    _nLog.Info($"\t 關閉分機={recRtp.ExtNo} 產生 rcv 檔案, fileSize={callRtp.RecvRaw.TotalSize}, 共寫入={callRtp.RecvRaw.TotalPkt}封包");
                    ssrcList = callRtp.DictRecvSSRC.Select(p => p.Value).ToList();
                    foreach (var ssrc in ssrcList) {
                        _nLog.Info($"\t\t ={ssrc.SSRC}: firstCapture={ssrc.FirstCaptureTime.ToTimeStr(":", 3)}, totalPacket={ssrc.TotalPacket}, playTime={ssrc.TotalPacket * (ulong)ssrc.FrameMilliSec / 1000.00}");
                    }
                    callRtp.Close(ENUM_IPDir.Recv, out errMsg);
                    if (!string.IsNullOrEmpty(errMsg))
                        _errorLog.Error($"recv.callRtp.Close error: {errMsg}");
                    #endregion

                    #region 送出製作錄音檔的指令
                    if (callRtp.DictSendSSRC.Count == 0 && callRtp.DictRecvSSRC.Count == 0) {
                        _nLog.Info($"\t *** 無任何 packet/ssrc,  Make recording file 指令忽略.");
                    }
                    else {
                        _nLog.Info($"\t 送出 Make recording file 指令 ... ");
                        MakeRecordingFile(callRtp, recRtp);
                    }
                    #endregion

                    _nLog.Info($"\t 移除 CallRtp 物件.\r\n");
                    DictCallRtp.Remove(recRtp.DialogID);
                }
            }
            // 開始按下 Hold
            else if (recRtp.Flag == ENUM_RTP_RecFlag.StartPressToHold) {
                if (DictCallRtp.TryGetValue(recRtp.DialogID, out callRtp)) {
                    _nLog.Info($">>> 開始主動按下Hold...=> 分機={recRtp.ExtNo}, pktTime={pktCaptureTimeStr}, dialogID={recRtp.DialogID} ...");
                    callRtp.StartPressHeld(recRtp.PktCaptureTime.Value);
                    _nLog.Info($">>> 當前 HoldList =>\r\n {JsonConvert.SerializeObject(callRtp.HoldList, Formatting.Indented)}");
                }
            }
            //開始進入被 Hold(MOH)
            else if (recRtp.Flag == ENUM_RTP_RecFlag.StartBeHeld) {
                if (DictCallRtp.TryGetValue(recRtp.DialogID, out callRtp)) {
                    _nLog.Info($">>> 開始被動Hold(MOH)...=> 分機={recRtp.ExtNo}, pktTime={pktCaptureTimeStr}, dialogID={recRtp.DialogID} ...");
                    callRtp.StartBeheld(recRtp.PktCaptureTime.Value);
                    _nLog.Info($">>> 當前 HoldList =>\r\n {JsonConvert.SerializeObject(callRtp.HoldList, Formatting.Indented)}");
                }
            }
            //結束 Hold 
            else if (recRtp.Flag == ENUM_RTP_RecFlag.StopHeld) {
                if (DictCallRtp.TryGetValue(recRtp.DialogID, out callRtp)) {
                    _nLog.Info($">>> 結束Hold...=> 分機={recRtp.ExtNo}, pktTime={pktCaptureTimeStr}, dialogID={recRtp.DialogID} ...");
                    if (callRtp.StopHeld(recRtp.PktCaptureTime.Value, out decimal holdTimeMs, out string err)) {
                        _nLog.Info($"\t 結束 Hold OK, holdDur={holdTimeMs}s");
                    }
                    else {
                        _nLog.Info($"\t 結束Hold錯誤: {err}");
                    }
                    _nLog.Info($">>> 當前 HoldList =>\r\n {JsonConvert.SerializeObject(callRtp.HoldList, Formatting.Indented)}");
                }
            }
        }
                

        /// <summary>
        /// 寫 RecRtp Log，量很大，沒事關閉
        /// </summary>
        /// <param name="recRtp"></param>
        /// <param name="rtpSeqLog"></param>
        /// <returns></returns>
        private async Task WriteRecRtpLog(RecRtpModel recRtp, string rtpSeqLog) {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
            if (GlobalVar.AppSettings.Logging.RecRtp) {
                var s = $"pktIndex={recRtp.PktIndex,4}, ext={recRtp.ExtNo}, {rtpSeqLog}";
                if (recRtp.Rtp == null)
                    s = $"{s}, *** RTP is null";
                else
                    s = $"{s}, {recRtp.Rtp.GetHeaderLog()}";
                _nLog.Info($"{s}, dialogID ={recRtp.DialogID}");
            }
        }

        /// <summary>
        /// 利用用 CallRtp 產生 RecFileModel，填入 RecFileQueue
        /// 注意: 重點是 RecFileModel 中的 SendSSRCList + RecSSRCList
        /// </summary>
        /// <param name="callRtp"></param>
        /// <param name="recRtp"></param>
        private void MakeRecordingFile(CallRtpModel callRtp, RecRtpModel recRtp) {
            if (!GlobalVar.CheckLicense(out string err)) {
                var errMsg = $"{callRtp.ExtNo}-{callRtp.CallID} MakeFile error: {err}";
                _nLog.Info($"\t\t {errMsg}");
                GlobalVar.SendAPI_WriteSystemLog(ENUM_LogType.Fatal, errMsg);
                return;
            }

            if (GlobalVar.DictMakeFileThread.TryGetValue(recRtp.ExtNo, out MakeFileThread makeFileThread)) {
                var recFile = new RecFileModel() {
                    ExtNo = callRtp.ExtNo,
                    CallDir= callRtp.CallDir,
                    CallID = callRtp.CallID,
                    RecStartTime = callRtp.StartTalkTime,
                    RecStopTime = recRtp.StopTalkTime.Value,
                    Duration = (int)(recRtp.StopTalkTime.Value - callRtp.StartTalkTime).TotalSeconds,
                    CallerID = callRtp.CallerID, 
                    CalledID = callRtp.CalledID, 
                    SendRawFileName = callRtp.SendRawFileName,
                    RecvRawFileName = callRtp.RecvRawFileName,                    
                };
                recFile.SendSSRCList.AddRange(callRtp.DictSendSSRC.Select(p => p.Value).ToList());
                recFile.RecvSSRCList.AddRange(callRtp.DictRecvSSRC.Select(p => p.Value).ToList());
                recFile.HoldList.AddRange(callRtp.HoldList);

                _nLog.Info($"\t\t 新增 MakeRecFile ..., ext={recFile.ExtNo}, callDir={callRtp.CallDir}, 錄音時間={recFile.RecStartTime.ToTimeStr()}, 通話時間={recFile.Duration}, CallerID={recFile.CallerID}, CalledID={recFile.CalledID} call-ID={recFile.CallID}");
                makeFileThread.AddRecFile(recFile);
            }
        }

        public void RequestStop() {                        
            _nLog.Info($"{_tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }

    }

}
