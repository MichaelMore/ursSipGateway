using NLog;
using SharpPcap;
using PacketDotNet;
using WorkerThread;
using NLog.Fluent;
using Project.AppSetting;
using Project.Models;
using static System.Net.WebRequestMethods;
using Project.ProjectCtrl;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Project.Lib;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Utilities.Net;
using System.Reflection;
using Project.Enums;
using NLog.Filters;
using Newtonsoft.Json;
using System.Diagnostics;

// ****************************************************************************************
//
// 說明: 處理監聽的封包 ... => 如果監聽 200 支分機，這樣會太多 Thread
// 後續這個部分應該要獨立開一個 Worker Service 來做
//
// ****************************************************************************************

namespace ThreadWorker
{
    public class MonitorPacketThread: IWorker
    {
        public class RtpControlEx {
            private object _bufferLock = new object();
            private int _bufferSize = 0;
            private int _jitterSize = 0;
            public int _bufferLength = 0;
            private byte[] _buffer  = null;
            private ushort OriginalSeq = 0; // 用來記錄 Rtp.Header.Sequence 是否重複?

            public UInt16 Seq { set; get; } // 真正傳送的 Seq(新的 Rtp.Header 的 Sequence)
            public UInt32 Timestamp { set; get; } // 真正傳送的 Timestamp(新的 Rtp.Header 的 Timestamp)
            public ENUM_IPDir IpDir { internal set; get; }            
            public byte[] Jitter { internal set; get; } = null;
            public bool JitterFull { internal set; get; } = false;


            // Constructer
            public RtpControlEx(ENUM_IPDir ipDir, int jitterSize, int bufferSize) {
                this.Seq = 1;
                this.Timestamp = 1;
                this.IpDir = ipDir;
                this._bufferSize = bufferSize;
                this._jitterSize = jitterSize;
                _buffer = new byte[this._bufferSize];
                Jitter = new byte[this._jitterSize];
            }

            public bool AddBuffer(PacketInfoModel packetInfo) {
                lock (_bufferLock) {
                    if (packetInfo.Rtp.Header.SeqNum != OriginalSeq) { // 過濾重複的封包                    
                        OriginalSeq = packetInfo.Rtp.Header.SeqNum;
                        Array.Copy(packetInfo.Rtp.AudioBytes, 0, _buffer, _bufferLength, packetInfo.Rtp.AudioBytes.Length);
                        _bufferLength = _bufferLength + packetInfo.Rtp.AudioBytes.Length;
                    }
                    JitterFull = _bufferLength >= _jitterSize;
                    return JitterFull; // Jitter buffer 是否已滿?
                }                
            }

            // return: 
            //      true: jitter 已滿，要撥放
            //      false: jitter 未滿
            public bool GetJitter() {
                var ret = false;
                lock (_bufferLock) {
                    if (_bufferLength >= _jitterSize) {
                        Array.Copy(_buffer, 0, Jitter, 0, _jitterSize);
                        _bufferLength = _bufferLength - _jitterSize;
                        ret = true;
                    }
                    JitterFull = _bufferLength >= _jitterSize; // 扔然要計算，也許還是 full
                    return ret;
                }                
            }

        }

        // protect, private
        protected volatile bool _shouldStop = false;
        protected volatile bool _shouldPause;

        private Logger _nLog;
        private string _tag;
        //private static object _queueLock = new object();

        //private Thread _myThread;                
        private Thread _sendThread;
        private Thread _recvThread;

        // live monitor
        private static object _liveMonLock = new object();
        private LiveMonitorModel _liveMonitor = new LiveMonitorModel(GlobalVar.AppSettings.LiveMonGateway.RenewIntervalSec);

        //private Socket _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);        
        //private AutoResetEvent _autoResetEvent = new AutoResetEvent(false);

        private ManualResetEvent recvWaitSending = new ManualResetEvent(false);
        private ManualResetEvent sendWaitSending = new ManualResetEvent(false);
        private RtpControlEx SendRtpCtrl = new RtpControlEx(ENUM_IPDir.Source, 8000*2, 8000 * 3);
        private RtpControlEx RecvRtpCtrl = new RtpControlEx(ENUM_IPDir.Source, 8000*2, 8000 * 3);

        // public        
        public WorkerState State { get; private set; }
        public AppSettings_Monitor_Device MonDevice { get; private set; }        

        // constructor
        public MonitorPacketThread(AppSettings_Monitor_Device monDev) {
            MonDevice = new AppSettings_Monitor_Device() {                
                IpAddr = monDev.IpAddr,
                MacAddr = monDev.MacAddr,
                Extn = monDev.Extn,
            };

            _tag = $"MonitorPkt_{MonDevice.Extn}";
            _nLog = LogManager.GetLogger(_tag);
        }

        //public void StartThread() {
        //    _myThread = new Thread(this.DoWork) {
        //        IsBackground = true,
        //        Name = _tag
        //    };
        //    State = WorkerState.Starting;
        //    _myThread.Start();
        //}

        //public void StopThread() {
        //    RequestStop(); // stopping ...
        //    _nLog.Info($"{_tag} is waiting to stop(join) ...");
        //    _myThread.Join();
        //    State = WorkerState.Stopped; // stopped !!!
        //}

        public void StartThread() {
            _sendThread = new Thread(this.Send) {
                IsBackground = true,
                Name = _tag + "_send"
            };            
            State = WorkerState.Starting;
            _sendThread.Start();

            _recvThread = new Thread(this.Recv) {
                IsBackground = true,
                Name = _tag + "_recv"
            };
            State = WorkerState.Starting;
            _recvThread.Start();
        }

        public void StopThread() {
            RequestStop(); // stopping ...
            _nLog.Info($"{_tag} is waiting to stop(join) ...");
            _sendThread.Join();
            _recvThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }



        //public void AddMonitorRtp(MonitorRtpModel monRtp) {
        //    lock (_queueLock) {
        //        _monitorRtpQueue.Enqueue(monRtp);
        //    }
        //}

        //public void AddMonitorRtp(MonitorRtpModel monRtp) {
        //    if (!_liveMonitor.IsOpened)
        //        return;
        //    lock (_queueLock) {
        //        if (monRtp.IpDir == ENUM_IPDir.Source) {
        //            if (monRtp.Rtp.Header.SeqNum != sendSeq) { // 過濾重複的封包                    
        //                sendSeq = monRtp.Rtp.Header.SeqNum;
        //                Array.Copy(monRtp.Rtp.AudioBytes, 0, sendBuffer, sendBufferLength, monRtp.Rtp.AudioBytes.Length);
        //                sendBufferLength = sendBufferLength + monRtp.Rtp.AudioBytes.Length;                        
        //            }
        //        }
        //        else if (monRtp.IpDir == ENUM_IPDir.Dest) {
        //            if (monRtp.Rtp.Header.SeqNum != recvSeq) { // 過濾重複的封包                    
        //                recvSeq = monRtp.Rtp.Header.SeqNum;
        //                Array.Copy(monRtp.Rtp.AudioBytes, 0, recvBuffer, recvBufferLength, monRtp.Rtp.AudioBytes.Length);
        //                recvBufferLength = recvBufferLength + monRtp.Rtp.AudioBytes.Length;                        
        //            }
        //        }
        //    }
        //}

        public bool StartLiveMonitor(LoggerCommandModel model, out string msg) {
            msg = "";
            var ret = false;
            lock (_liveMonLock) {
                ret = _liveMonitor.StartMonitor(model, out msg);
            }
            return ret;            
        }

        public void StopLiveMonitor() {
            lock (_liveMonLock) {
                _liveMonitor.StopMonitor();
            }
            return;
        }

        public bool CheckRenewLiveMonitor() {
            var ret = false;
            lock (_liveMonLock) {
                ret = _liveMonitor.CheckRenew();
            }
            return ret;
        }

        public bool GetLiveMonitorStatus() {
            var ret = false;    
            lock (_liveMonLock) {
                ret = _liveMonitor.IsOpened;
            }
            return ret;
        }

        public void AddMonitorRtp(ENUM_IPDir ipDir, PacketInfoModel packetInfo) {
            if (!_liveMonitor.IsOpened)
                return;
            
            if (ipDir == ENUM_IPDir.Source) {
                if (SendRtpCtrl.AddBuffer(packetInfo)) {
                    sendWaitSending.Set();
                }
            }
            else if (ipDir == ENUM_IPDir.Dest) {
                if (RecvRtpCtrl.AddBuffer(packetInfo)) {
                    recvWaitSending.Set();
                }
            }            
        }

        public virtual void Send(object anObject) {
            _nLog.Info("");
            _nLog.Info($"********** MonitorPacket:Send {MonDevice.Extn} is now starting ... **********");
            State = WorkerState.Running;
            while (!_shouldStop) {                
                sendWaitSending.WaitOne();
                while (SendRtpCtrl.JitterFull) {
                    var orgBufLen = SendRtpCtrl._bufferLength;
                    if (SendRtpCtrl.GetJitter()) {                        
                        SendRTP(_liveMonitor._endTx, SendRtpCtrl, ENUM_PayloadType.PT_PCMU);
                        _nLog.Info($"send SEND: {orgBufLen}/{SendRtpCtrl._bufferLength}");
                    }
                }
                sendWaitSending.Reset();
            }// end while
            _nLog.Info($"========== MonitorPacket:Send {MonDevice.Extn} terminated. ==========");
            State = WorkerState.Stopped;
        }

        public virtual void Recv(object anObject) {
            _nLog.Info("");
            _nLog.Info($"********** MonitorPacket:Recv {MonDevice.Extn} is now starting ... **********");
            State = WorkerState.Running;
            while (!_shouldStop) {
                recvWaitSending.WaitOne();
                while (RecvRtpCtrl.JitterFull) {
                    var orgBufLen = RecvRtpCtrl._bufferLength;
                    if (RecvRtpCtrl.GetJitter()) {
                        SendRTP(_liveMonitor._endRx, RecvRtpCtrl, ENUM_PayloadType.PT_PCMU);
                        _nLog.Info($"send RECV: {orgBufLen}/{RecvRtpCtrl._bufferLength}");
                    }
                }
                sendWaitSending.Reset();
            }// end while
            _nLog.Info($"========== MonitorPacket:Recv {MonDevice.Extn} terminated. ==========");
            State = WorkerState.Stopped;
        }

        public virtual void DoWork(object anObject) {

        }

        //public virtual void DoWork(object anObject) {
        //    _nLog.Info("");
        //    _nLog.Info($"********** MonitorPacket {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr} is now starting ... **********");
            
        //    var sendJitterFull = false;
        //    var recvJitterFull = false;
        //    State = WorkerState.Running;            
        //    while (!_shouldStop) {
        //        //_autoResetEvent.WaitOne(1, true);                                
        //        WaitSendRTP.WaitOne();

        //        sendJitterFull = SendRtpCtrl.GetJitter();
        //        recvJitterFull = RecvRtpCtrl.GetJitter();
                
        //        if (sendJitterFull) {                    
        //            SendRTP(_liveMonitor._endTx, SendRtpCtrl, ENUM_PayloadType.PT_PCMU);                    
        //        }
        //        if (recvJitterFull) {                    
        //            SendRTP(_liveMonitor._endRx, RecvRtpCtrl, ENUM_PayloadType.PT_PCMU);
                    
        //        }
        //    }// end while
        //    _nLog.Info($"========== MonitorPacket {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr}] terminated. ==========");
        //    State = WorkerState.Stopped;
        //}

        private async Task SendRTP(IPEndPoint endPoint, RtpControlEx rtpCtrl, ENUM_PayloadType pt) {
            await Task.Delay(TimeSpan.FromMilliseconds(1));
            if (endPoint == null)
                return;
            try {
                var dwDataLen = rtpCtrl.Jitter.Length;
                var sendCount = dwDataLen / 160;
                if (dwDataLen % 160 != 0) // 是否有餘數，有餘數要多送一次
                    sendCount++;

                for (var i = 0; i < sendCount; i++) {
                    var startPos = i * 160;
                    var copyLen = (startPos + 160) <= dwDataLen ? 160 : dwDataLen - startPos;

                    // 每次傳送 1 個 frame, 12 + 160
                    byte[] rtp = new byte[copyLen + 12];
                    var rtpHeader = PrepareRTPHeader(ref rtpCtrl, pt);
                    rtpHeader.CopyTo(rtp, 0); // copy header(12)
                    Array.Copy(rtpCtrl.Jitter, i * 160, rtp, 12, copyLen); // copy payload
                    _liveMonitor.UdpSocket.SendTo(rtp, endPoint);
                    //_sock.SendTo(rtp, endPoint);
                }
            }
            catch(Exception ex) {
            }            
        }

        private byte[] PrepareRTPHeader(ref RtpControlEx rtpCtrl, ENUM_PayloadType pt) {
            int samplePerSec = 8000;    // 8000 Hz
            int rtpFrameRate = 20;      // 
            int rtpFrameByte = samplePerSec / (1000 / rtpFrameRate);

            // 產生封包的間隔時間，若以 20ms 產生一包，則須產生 50 次，8000Hz/50 = 160(時間增量)
            int rtpFrameInterval = samplePerSec / (1000 / rtpFrameRate);

            var rtpHdr = new byte[12];
            // Byte 0 = 0x80
            rtpHdr[0] = 0x80; // ver=2(10), p=0, x=0(00), cc=0000, 2進位(10000000 = 0x80)

            // Byte 1 = 0x00(使用 G.711, PCMU)           
            rtpHdr[1] = (byte)pt;

            // RTP Sequence
            rtpHdr[2] = (byte)(rtpCtrl.Seq / 256);
            rtpHdr[3] = (byte)(rtpCtrl.Seq % 256);

            if (rtpCtrl.Seq >= UInt16.MaxValue)
                rtpCtrl.Seq = 1;
            else
                rtpCtrl.Seq++;

            //Big Endian
            var timeStamp = BitConverter.GetBytes(rtpCtrl.Timestamp);
            rtpHdr[4] = timeStamp[3];
            rtpHdr[5] = timeStamp[2];
            rtpHdr[6] = timeStamp[1];
            rtpHdr[7] = timeStamp[0];
            if (rtpCtrl.Timestamp >= uint.MaxValue - 1)
                rtpCtrl.Timestamp = 1;
            else
                rtpCtrl.Timestamp = (uint)(rtpCtrl.Timestamp + rtpFrameInterval);
            // SSRC
            rtpHdr[8] = 0x00;
            rtpHdr[9] = 0x90;
            rtpHdr[10] = 0x00;
            rtpHdr[11] = (byte)((int)rtpCtrl.IpDir);  // 1 or 2
            return rtpHdr;
        }

        public void RequestStop() {                        
            _nLog.Info($"{_tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }

    }

}
