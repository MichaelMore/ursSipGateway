using Newtonsoft.Json;
using Project.Enums;
using Project.Lib;
using Project.ProjectCtrl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Project.Models {    

    public class CallRtpModel {
        public string BaseFolder { get; set; } = string.Empty;
        public string ExtNo { get; set; } = string.Empty;
        public string CallID { get; set; } = string.Empty;
        public DateTime StartTalkTime { get; set; }

        public ENUM_CallDirection CallDir { get; set; } = ENUM_CallDirection.Unknown; 
        public string CallerID { get; set; } = string.Empty;
        public string CalledID { get; set; } = string.Empty;

        public string SendRawFileName { get; set; } = "";
        public string RecvRawFileName { get; set; } = "";                
        public ulong SendRtpSeq { get; internal set; } = 0;
        public ulong RecvRtpSeq { get; internal set; } = 0;

        // AudioRawModel 用來寫入 raw 檔案，buffer 滿了再寫
        public AudioRawModel SendRaw { get; internal set; }
        public AudioRawModel RecvRaw { get; internal set; }

        // 用來記錄前 6 個 RTP Seq，並且插入的 rtp ，其序號不能與前 6 個重複。
        // 不一定要前 6 個，5 個也可以，要看 RTP 封包傳送順序的狀況。
        private FixedSizedQueue<ulong> SendSeqQueue = new FixedSizedQueue<ulong>(6);
        private FixedSizedQueue<ulong> RecvSeqQueue = new FixedSizedQueue<ulong>(6);

        private string sendSsrcInfoFileName { get; set; } = "";
        private string recvSsrcInfoFileName { get; set; } = "";

        // SSRCModel 是用來記錄每一段的 SSRC 的封包數、封包到達時間...，後續主要用來計算 silence 插入的依據
        public Dictionary<string, SsrcControlModel> DictSendSSRC { get; internal set; } = new Dictionary<string, SsrcControlModel>(); // 紀錄所有 "Send" 封包的 SSRC，SSRC 有多段
        public Dictionary<string, SsrcControlModel> DictRecvSSRC { get; internal set; }= new Dictionary<string, SsrcControlModel>(); // 紀錄所有 "Recv" 封包的 SSRC，SSRC 有多段

        private int _samplingRates = 8000; // 聲音取樣頻率，例如: G.711 每秒 8000 Bytes
        private int _bytesPerFrame = 160; // 聲音取樣頻率，例如: G.711 每秒 8000 Bytes
        
        // 作廢
        //private bool _beHeld = false; // 紀錄是否剛剛被保留

        public List<HoldModel> HoldList = new List<HoldModel>(); // 儲存被Hold或主動 Hold 的紀錄        

        /// <summary>
        /// 建立 CallRtpModel
        /// </summary>
        /// <param name="baseFolder">錄音檔、Temp檔的所在路徑</param>
        /// <param name="callID">SIP 中的 CallID</param>
        /// <param name="extNo"></param>
        /// <param name="startTalkTime"></param>
        /// <param name="maxBytesToWrite"></param>        
        public CallRtpModel(string baseFolder, RecRtpModel recRtp, int samplingRates, int bytesPerFrame,  int maxBytesToWrite) {
            BaseFolder = baseFolder;
            ExtNo = recRtp.ExtNo;
            CallID = recRtp.CallID;            
            StartTalkTime = recRtp.StartTalkTime;
            CallerID = recRtp.CallerID;
            CalledID = recRtp.CalledID;
            CallDir= recRtp.CallDir;
            lib_misc.ForceCreateFolder(baseFolder); // 建立 temp\20221012 的日期目錄            
            var dir = recRtp.CallDir == ENUM_CallDirection.Outbound ? "OB" : "IB";

            var callerID = string.IsNullOrEmpty(CallerID) ? "null" : CallerID;
            var calledID = string.IsNullOrEmpty(CalledID) ? "null" : CalledID;

            var baseFileName = $"{ExtNo}_{dir}-{callerID}-{calledID}_{StartTalkTime.ToString("yyyyMMdd-HHmmss")}_{CallID}";

            SendRawFileName = Path.Combine(baseFolder, $"{baseFileName}_snd.raw");
            RecvRawFileName = Path.Combine(baseFolder, $"{baseFileName}_rcv.raw");

            sendSsrcInfoFileName = Path.Combine(baseFolder, $"{baseFileName}_snd.ssrc");
            recvSsrcInfoFileName = Path.Combine(baseFolder, $"{baseFileName}_rcv.ssrc");            

            SendRaw = new AudioRawModel(SendRawFileName, maxBytesToWrite);
            RecvRaw = new AudioRawModel(RecvRawFileName, maxBytesToWrite);
            _samplingRates = samplingRates;
            _bytesPerFrame = bytesPerFrame;
            
        }

        #region 重複封包的成因
        // 重複封包的成因: 可以查一下關於 Pcap/Wildshark => https://wiki.wireshark.org/DuplicatePackets
        // 這是一個很常見的網路封包問題，理由直接看網址...
        // 有些網路上會有針對 pcap 檔案如何移除重複封包的方法(pcap 檔案是 wildshark 本身錄下封包的檔名)，
        // 但在這裡，直接用 RTP 的 Seq 來判斷即可。        
        #endregion

        // return true/false 目前沒用到
        public bool WritePacket(ENUM_IPDir pktDir, RtpModel rtp, out string errMsg) {            
            errMsg = "";
            var ret = false;
            var ssrcStr = $"0x{Convert.ToString(rtp.Header.SSRC, 16).ToUpper()}";
            if (pktDir == ENUM_IPDir.Send) {                
                if (!SendSeqQueue.Qu.Contains(rtp.Header.SeqNum)) { // 過濾重複的封包                                        

                    //移除最舊的，並塞入新的 seq
                    SendSeqQueue.Enqueue(rtp.Header.SeqNum);

                    if (DictSendSSRC.TryGetValue(ssrcStr, out SsrcControlModel sendSSRC)) { // ssrc 已存在
                        sendSSRC.AddPacket(rtp);
                    }
                    else { // ssrc 不存在                        
                        SendSeqQueue.Clear();

                        sendSSRC = new SsrcControlModel(ssrcStr, pktDir, _samplingRates, _bytesPerFrame); // 建立另一個新的
                        DictSendSSRC.Add(ssrcStr, sendSSRC);
                        sendSSRC.AddPacket(rtp);
                    }
                    
                    return SendRaw.WritePacket(rtp.Header.SeqNum, rtp.AudioBytes, out errMsg);                    
                }
            }
            else if (pktDir == ENUM_IPDir.Recv) {                
                if (!RecvSeqQueue.Qu.Contains(rtp.Header.SeqNum)) { // 過濾重複的封包                    
                    
                    //移除最舊的，並塞入新的 seq
                    RecvSeqQueue.Enqueue(rtp.Header.SeqNum);

                    if (DictRecvSSRC.TryGetValue(ssrcStr, out SsrcControlModel recvSSRC)) { // ssrc 已存在
                        recvSSRC.AddPacket(rtp);
                    }
                    else { // ssrc 不存在

                        #region 原來是想在這裡標註是否為 MOH，但發現封包到達時間不一定，所以此做法會有問題，故作廢
                        //// 判斷這個 SSRC 是否為保留音樂? 只有 recv 需要判斷
                        //var beheld = _beHeld;
                        //if (beheld) {
                        //    _beHeld = false; // 已經知道是 MOH 了，重新設為 false，避免下一個 SSRC 被影響
                        //}
                        #endregion

                        RecvSeqQueue.Clear();

                        recvSSRC = new SsrcControlModel(ssrcStr, pktDir, _samplingRates, _bytesPerFrame); // 建立另一個新的
                        DictRecvSSRC.Add(ssrcStr, recvSSRC);                        
                        recvSSRC.AddPacket(rtp);
                    }

                    return RecvRaw.WritePacket(rtp.Header.SeqNum, rtp.AudioBytes, out errMsg);                    
                }
            }
            return false;
        }

        public bool Close(ENUM_IPDir pktDir, out string errMsg) {            
            if (pktDir == ENUM_IPDir.Send) {                
                WriteSSRCInfo(pktDir);                
                return SendRaw.Close(out errMsg); // 會強制送出在 buffer 中的 data
            }
            else {                
                WriteSSRCInfo(pktDir);                
                return RecvRaw.Close(out errMsg); // 會強制送出在 buffer 中的 data
            }
        }

       
        /// <summary>
        /// 這個是把每一段的 SSRC 的內容寫檔，純粹是為了 debug
        /// </summary>
        /// <param name="pktDir"></param>
        private void WriteSSRCInfo(ENUM_IPDir pktDir) {
            if (!GlobalVar.AppSettings.Logging.SSRCInfo)
                return;

            if (pktDir == ENUM_IPDir.Send) {
                if (DictSendSSRC.Count == 0)
                    return;
                try {
                    using (var fs = new FileStream(sendSsrcInfoFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)) {
                        fs.Seek(0, SeekOrigin.End);
                        var sendList = DictSendSSRC.Select(p => p.Value).ToList();
                        var dataStr = JsonConvert.SerializeObject(sendList, Formatting.Indented);
                        var dataByte = new UTF8Encoding(true).GetBytes(dataStr);
                        fs.Write(dataByte, 0, dataByte.Length);
                    }
                }
                catch(Exception ex) {
                }                
            }
            else if (pktDir == ENUM_IPDir.Recv) {
                if (DictRecvSSRC.Count == 0)
                    return;
                try {
                    using (var fs = new FileStream(recvSsrcInfoFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)) {
                        fs.Seek(0, SeekOrigin.End);
                        var recvList = DictRecvSSRC.Select(p => p.Value).ToList();
                        var dataStr = JsonConvert.SerializeObject(recvList, Formatting.Indented);
                        var dataByte = new UTF8Encoding(true).GetBytes(dataStr);
                        fs.Write(dataByte, 0, dataByte.Length);
                    }
                }
                catch (Exception ex) {
                }                
            }
        }        

        // 紀錄被 Hold 的(封包)開始時間
        public void StartBeheld(DateTime packetTime) {            
            var held = new HoldModel() {
                Dir = ENUM_IPDir.Recv,
                StartPacketTime = packetTime                
            };
            HoldList.Add(held);
        }

        // 紀錄按下 Hold 的(封包)開始時間
        public void StartPressHeld(DateTime packetTime) {
            var held = new HoldModel() {
                Dir = ENUM_IPDir.Send, 
                StartPacketTime = packetTime
            };
            HoldList.Add(held);
        }

        // 紀錄停止 Hold 的時間(包含主動即被動)
        public bool StopHeld(DateTime packetTime, out decimal holdTimeMs, out string err) {
            err = "";
            holdTimeMs = 0;
            var held = HoldList.LastOrDefault();
            if (held == null) { // 沒有任何 hold 
                err = "hold item not found";
                return false;
            }
            if (held.EndPacketTime.HasValue) { // 已有結束時間
                var start = held.StartPacketTime.HasValue ? held.StartPacketTime.Value.ToTimeStr(":", 3) : "null";
                var end = held.EndPacketTime.HasValue ? held.EndPacketTime.Value.ToTimeStr(":", 3) : "null";
                err = $"the last hold has already EndPacketTime({start}~{end})";
                return false;
            }

            var diffms = (packetTime - held.StartPacketTime.Value).TotalMilliseconds;
            if ( diffms <= 100 ) { // 小於0 或 時間太短，應該不是合法的結束HOLD
                err = $"hold結束時間太短(diffms={diffms})";
                return false;
            }

            held.EndPacketTime = packetTime;
            holdTimeMs = (decimal)Math.Round((held.EndPacketTime.Value - held.StartPacketTime.Value).TotalSeconds, 2);
            held.SetHoldTime();
            return true;            
        }
    }
}
