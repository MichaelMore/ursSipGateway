using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Fluent;
using PacketDotNet;
using Project.AppSetting;
using Project.Enums;
using Project.Lib;
using Project.ProjectCtrl;
using SharpPcap;
using StackExchange.Profiling.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


namespace Project.Models {
    public class SdpInfo {
        public string ConnectedIP { set; get; } = "";
        public int RtpPort { internal set; get; } = 0;
        public bool SendOnly { internal set; get; } = false;
        public bool SendRecv { internal set; get; } = false;
        public bool Inactive { internal set; get; } = false;

        public SdpInfo(string messageBody) {
            var lines = messageBody.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Select(y => y.Trim())
                                .ToList();
            if (lines == null || lines.Count == 0)
                return;
            foreach (string s in lines) {
                if (s.Contains("m=audio")) {
                    var tmp = s.Split(new string[] { "m=audio" }, StringSplitOptions.None)[1]
                                .Split(new string[] { "RTP/" }, StringSplitOptions.None)[0].Trim()
                                .Split('/')[0].Trim(); // 移除萬一 port 有包含斜線, ex: 21506/2
                    if (int.TryParse(tmp, out int port)) {
                        RtpPort = port;
                    }                 
                }
                if (s.Contains("c=IN")) {
                    ConnectedIP = s.Split(new string[] { "IP4 " }, StringSplitOptions.RemoveEmptyEntries)[1].Trim();
                }
                if (s.Contains("a=sendrecv")) {
                    SendRecv = true;
                }
                if (s.Contains("a=sendonly")) {
                    SendOnly = true;
                }
                if (s.Contains("a=inactive")) {
                    Inactive = true;
                }
            }
        }

        public string GetInfo() {
            var list = new List<string>();
            if (SendRecv)
                list.Add("a=sendrecv");
            if (SendOnly)
                list.Add("a=sendonly");
            if (Inactive)
                list.Add("a=inactive");
            return string.Join("; ", list);
        }
    }

    // 以下的欄位，都是原始的整串字串，並未解譯或切割、轉型
    public class SipHeader {
        public string RequestOrStatus { internal set; get; } = ""; // 命令行或狀態行
        public string Via { internal set; get; } = "";
        public string From { internal set; get; } = "";
        public string To { internal set; get; } = "";
        public string CallID { internal set; get; } = "";
        public string SessionID { internal set; get; } = "";
        public string RemotePartyID { internal set; get; } = "";
        public string CSeq { internal set; get; } = "";
        public string ContentLength { internal set; get; } = "";
        public string ContentType { internal set; get; } = "";
    }


    public class SipInfo {
        public SipHeader Header { internal set; get; } = new SipHeader(); // 存放 SIP ao3u
        public string FromTag { internal set; get; } = "";
        public string FromExt { internal set; get; } = "";
        public string ToTag { internal set; get; } = "";
        public string ToExt { internal set; get; } = "";
        public string CallID { internal set; get; } = "";
        public string SessionID { internal set; get; } = "";
        public string RemotePartyID { internal set; get; } = "";
        public string Branch { internal set; get; } = "";
        public string CSeq { internal set; get; } = "";
        public int? Content_Length { internal set; get; } = null;
        public string Content_Type { internal set; get; } = "";
        public string MessageHeader { internal set; get; } = ""; // SIP payload 兩個換行前面的內容，例如: "Via:", "From:"，"To:" ...等
        public string MessageBody { internal set; get; } = ""; // SIP payload 兩個換行後面的內容，例如: v=0，s=SIP Call, m-audio, ...等

        public SipInfo() { }

        public void GetFromExt(string s) {
            FromExt = "";
            if (s.Contains("<sip:") && s.Contains('@')) { // 有時候 => "From: <sip:10.102.10.111>"， <sip: 後面沒有 @
                FromExt = s.Split(new string[] { "<sip:" }, StringSplitOptions.RemoveEmptyEntries)[1].Split('@')[0].Trim();
            }                        
        }
        public void GetFromTag(string s) {            
            if (s.Contains(";tag=")) {
                FromTag = s.Split(";tag=")[1].Trim();
            }            
        }
        public void GetToExt(string s) {
            ToExt = "";
            if (s.Contains("<sip:") && s.Contains('@')) { // 有時候 => "To: <sip:10.102.10.111>"， <sip: 後面沒有 @
                ToExt = s.Split(new string[] { "<sip:" }, StringSplitOptions.RemoveEmptyEntries)[1].Split('@')[0].Trim();
            }            
        }
        public void GetToTag(string s) {            
            if (s.Contains(";tag=")) {
                ToTag = s.Split(";tag=")[1].Trim();
            }            
        }
        public void GetBranch(string s) {            
            if (s.Contains("Via: ") && s.Contains(";branch=")) {
                Branch = s.Split(";branch=")[1].Trim();
            }            
        }
        public void GetCallID(string s) {            
            CallID = s.Split("Call-ID: ")[1].Trim();            
        }
        public void GetRemotePartyID(string s) {
            RemotePartyID = s.Split("Remote-Party-ID:")[1].Trim();
        }
        public void GetSessionID(string s) {
            SessionID = s.Split("Session-ID: ")[1].Trim();
        }
        public void GetCSeq(string s) {            
            CSeq = s.Split("CSeq:")[1].Trim();            
        }
        public void GetContentLen(string s) {            
            var tmp = s.Split("Content-Length:")[1].Trim();
            if (int.TryParse(tmp, out int len))
                Content_Length = len;
            
        }
        public void GetContentType(string s) {
            Content_Type = s.Split("Content-Type:")[1].Trim();
        }
    }

    public class PacketInfoEx {
        #region 在建構子中解析完成，若有問題，CaptureSuccess = false
        public DateTime CaptureTime { internal get; set; } = DateTime.MinValue;
        public string PktTime { internal set; get; }
        public long PktLen { internal set; get; }
        public string SrcMac { internal set; get; }
        public string DstMac { internal set; get; }

        public TcpPacket Tcp { internal get; set; } = null; // 如果是 tcp 封包，就會填入 TcpPacket 物件
        public UdpPacket Udp { internal get; set; } = null; // 如果是 udp 封包，就會填入 UdpPacket
        public ENUM_IPType IPType { internal set; get; }


        public string SrcIp { internal set; get; }
        public int SrcPort { internal set; get; }
        public string DstIp { internal set; get; }
        public int DstPort { internal set; get; }
        public byte[] PayloadData { internal set; get; } = null;
        public string PayLoadStr { internal set; get; } = String.Empty;

        public bool CaptureSuccess { internal set; get; } = true;
        #endregion

        public ENUM_SIPCommand SipCmd { internal set; get; } = ENUM_SIPCommand.S_Unknown;

        public SipInfo Sip { internal set; get; } = new SipInfo();
        public SdpInfo Sdp { internal set; get; } = null;
        public RtpModel Rtp { internal set; get; } = null;
        public bool IsSdp { internal set; get; } = false;


        // 建構子: 解譯封包，取得TCP/UDP 相關資訊
        public PacketInfoEx(PacketCapture e) {
            try {
                CaptureTime = e.Header.Timeval.Date;
                PktTime = CaptureTime.ToString("HH:mm:ss.ffffff");
                PktLen = e.Data.Length; // 這個會跟 Wildsark 一樣，這樣比較好除錯
                var rawPacket = e.GetPacket();
                var packet = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
                SrcMac = ((EthernetPacket)packet).SourceHardwareAddress.ToString();
                DstMac = ((EthernetPacket)packet).DestinationHardwareAddress.ToString();
                Tcp = packet.Extract<PacketDotNet.TcpPacket>();
                if (Tcp != null) {
                    SetTcp();
                }
                else {
                    Udp = packet.Extract<PacketDotNet.UdpPacket>();
                    if (Udp != null)
                        SetUdp();
                    else {
                        CaptureSuccess = false;
                    }
                }
            }
            catch (Exception ex) {
                CaptureSuccess = false;
            }
        }

        public void SetTcp() {
            IPType = ENUM_IPType.TCP;
            var ipPacket = (PacketDotNet.IPPacket)Tcp.ParentPacket;
            SrcIp = ipPacket.SourceAddress.ToString();
            DstIp = ipPacket.DestinationAddress.ToString();
            SrcPort = Tcp.SourcePort;
            DstPort = Tcp.DestinationPort;
            if (Tcp.PayloadData != null && Tcp.PayloadData.Length > 0) {
                PayLoadStr = Encoding.UTF8.GetString(Tcp.PayloadData);
                PayloadData = new byte[Tcp.PayloadData.Length];
                Array.Copy(Tcp.PayloadData, 0, PayloadData, 0, Tcp.PayloadData.Length);
            }
        }

        public void SetUdp() {
            IPType = ENUM_IPType.UDP;
            var ipPacket = (PacketDotNet.IPPacket)Udp.ParentPacket;
            SrcIp = ipPacket.SourceAddress.ToString();
            DstIp = ipPacket.DestinationAddress.ToString();
            SrcPort = Udp.SourcePort;
            DstPort = Udp.DestinationPort;
            if (Udp.PayloadData != null && Udp.PayloadData.Length > 0) {
                PayLoadStr = Encoding.UTF8.GetString(Udp.PayloadData);
                PayloadData = new byte[Udp.PayloadData.Length];
                Array.Copy(Udp.PayloadData, 0, PayloadData, 0, Udp.PayloadData.Length);
            }
        }

        // 把上一個封包的 payload(參數: lastPayloadData) 塞入目前的 PayloadData 的前面，
        // 並且重新設定 PayloadStr, sip ....等
        public void InsertFirstSegment(byte[] lastPayloadData) {
            var tempByte = new byte[lastPayloadData.Length + PayloadData.Length]; // 宣告新的 byte array
            Array.Copy(lastPayloadData, 0, tempByte, 0, lastPayloadData.Length);  // copy 第 1 個 Segment(lastPayloadData)
            Array.Copy(PayloadData, 0, tempByte, lastPayloadData.Length, PayloadData.Length);  // copy 現在的(第 2 個) Segment
            PayloadData = tempByte;

            // 重新設定  Content-Length & Payload string
            PayLoadStr = Encoding.UTF8.GetString(PayloadData);
            SetSip();
        }

        public void GetMessageHeaderAndBody() {
            var lines = PayLoadStr.Split("\r\n\r\n");
            if (lines != null) {
                if (lines.Length >= 2) {
                    Sip.MessageHeader = lines[0];
                    Sip.MessageBody = lines[1];
                }
                else if (lines.Length == 1) {
                    Sip.MessageHeader = lines[0];
                }
            }
        }

        public void SetSip() {
            if (string.IsNullOrEmpty(PayLoadStr))
                return;
            // 取 SIP 的 Header + Body
            GetMessageHeaderAndBody();

            // 針對 Message Headder 再進一步解譯，填入 Sip.Header
            var lines = Sip.MessageHeader.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(y => y.Trim())
                            .ToList();
            if (lines.Count == 0)
                return;

            Sip.Header.RequestOrStatus = lines[0];
            foreach (string s in lines) {
                if (s.Contains("From: ")) {
                    Sip.Header.From = s;
                    Sip.GetFromExt(s);
                    Sip.GetFromTag(s);
                }
                if (s.Contains("To: ")) {
                    Sip.Header.To = s;
                    Sip.GetToExt(s);
                    Sip.GetToTag(s);
                }
                if (s.Contains("Via: ") && s.Contains(";branch=")) {
                    Sip.Header.Via = s;
                    Sip.GetBranch(s);
                }
                if (s.Contains("Call-ID: ")) {
                    Sip.Header.CallID = s;
                    Sip.GetCallID(s);
                }
                if (s.Contains("Session-ID: ")) {
                    Sip.Header.SessionID = s;
                    Sip.GetSessionID(s);
                }
                if (s.Contains("Remote-Party-ID: ")) {
                    Sip.Header.RemotePartyID = s;
                    Sip.GetRemotePartyID(s);
                }
                if (s.Contains("CSeq: ")) {
                    Sip.Header.CSeq = s;
                    Sip.GetCSeq(s);
                }
                if (s.Contains("Content-Length: ")) {
                    Sip.Header.ContentLength = s;
                    Sip.GetContentLen(s);
                }
                if (s.Contains("Content-Type: ")) {
                    Sip.Header.ContentType = s;
                    Sip.GetContentType(s);
                }
            }

            #region 解決 封包切割的說明
            // 判斷 SIP 命令是否完整?
            // 不完整的狀況有 2:
            //      1. 有 Content-Length: 可以用 Sip.Content_Length.Value && Sip.MessageBody.Length 比對，看命令是否完整? 
            //      2. Content-Length 還沒到!(因為在下一個封包)：此時算不完整。
            // 符合上述兩種狀況，都將 return S_Incomplete，將等待第 2 個封包的到來
            #endregion
            if (PayLoadStr.Contains("Via: ") && PayLoadStr.Contains("From: ") && PayLoadStr.Contains("To: ")) {
                SipCmd = ENUM_SIPCommand.S_Incomplete;
                if (Sip.Content_Length.HasValue) {
                    if (Sip.Content_Length.Value == Sip.MessageBody.Length)
                        SipCmd = ENUM_SIPCommand.S_Completed; // 完整了
                }
            }
        }

        public void SetSdp() {
            if (!string.IsNullOrEmpty(Sip.MessageBody)) {
                Sdp = new SdpInfo(Sip.MessageBody);
            }
        }

        public void SetRtp() {
            Rtp = new RtpModel(Udp.PayloadData, CaptureTime);
        }

        // 檢查封包的 srcIp 或 dstIp 有在監控的清單中
        private bool CheckIpAddrIsMonitoring() {
            return GlobalVar.AppSettings.Monitor.Device.Any(x => x.IpAddr == SrcIp || x.IpAddr == DstIp);
        }

        // 檢查封包的 srcMac 或 dstMac 有在監控的清單中
        private bool CheckMacAddrIsMonitoring() {
            return GlobalVar.AppSettings.Monitor.Device.Any(x => x.MacAddr == SrcMac || x.MacAddr == DstMac);
        }

        // 檢查設備是否有被監控，會依照 appsettings.json 設定自動判定 ip 或 mac
        public bool CheckIpOrMacIsMonitoring() {
            // 檢查是否在監控的 IP 設定中
            if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.IP) {
                return CheckIpAddrIsMonitoring();
            }

            //檢查是否在監控的 MAC 設定中
            else if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.MAC) {
                return CheckMacAddrIsMonitoring();
            }
            else {
                return false;
            }
        }


        // 檢查封包的 srcPort 或 dstPort 是否等於監控的 SIP Port，若是的話，封包才要處理
        public bool CheckIfSipPort() {
            return SrcPort == GlobalVar.AppSettings.Monitor.SipPort || DstPort == GlobalVar.AppSettings.Monitor.SipPort;
        }

        // 取得這個封包對應到監控清單(AppSettings.Monitor.Device)中的分機號碼，並順道回傳是 "發送端" 或 "接收端"        
        //
        // *************************************************************************************************************
        // 特別說明: 理論上是不會取到 發送端 + 接收端，因為不是 SipServer 送過來，不然就是送給 SipServer，所以只會有其中一種狀況
        // *************************************************************************************************************
        public AppSettings_Monitor_Device GetMonitorDevice(out ENUM_IPDir ipDir) {
            AppSettings_Monitor_Device dev = null;
            ipDir = ENUM_IPDir.Unknown;

            // 先看看發送端
            if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.IP) {
                dev = GlobalVar.AppSettings.Monitor.Device.Where(x => x.IpAddr == SrcIp).FirstOrDefault();
            }
            else if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.MAC) {
                dev = GlobalVar.AppSettings.Monitor.Device.Where(x => x.MacAddr == SrcMac).FirstOrDefault();
            }
            if (dev != null) {
                ipDir = ENUM_IPDir.Send;
                return dev;
            }

            // 再看看接收端
            if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.IP) {
                dev = GlobalVar.AppSettings.Monitor.Device.Where(x => x.IpAddr == DstIp).FirstOrDefault();
            }
            else if (GlobalVar.AppSettings.Monitor.MonitorType == ENUM_SIP_MonitorType.MAC) {
                dev = GlobalVar.AppSettings.Monitor.Device.Where(x => x.MacAddr == DstMac).FirstOrDefault();
            }
            if (dev != null) {
                ipDir = ENUM_IPDir.Recv;
                return dev;
            }

            return null;
        }

        public string GetSIPDetailLog() {
            var log = new StringBuilder();
            var sdp = IsSdp ? "/SDP" : "";
            log.AppendLine("");
            log.AppendLine($"{new string('=', 37)} {IPType.ToString()}{sdp} {new string('=', 37)}");
            log.AppendLine($"time=[{PktTime}] ip=[{SrcIp}]@{SrcPort} => [{DstIp}]@{DstPort}, mac=[{SrcMac}]=>[{DstMac}], len={PktLen}");
            log.AppendLine($"{new string('=', 80)}");
            log.AppendLine($"{PayLoadStr}");
            log.AppendLine($"{new string('-', 80)}\r\n");
            return log.ToString();
        }

        public string GetSIPLog() {
            if (Sip.Header.RequestOrStatus.Contains("NOTIFY") || Sip.Header.RequestOrStatus.Contains("SUBSCRIBE") ||
                        Sip.CSeq.Contains("NOTIFY") || Sip.CSeq.Contains("SUBSCRIBE") || Sip.CSeq.Contains("REGISTER"))
                return "";

            var log = new StringBuilder();
            log.AppendLine("");
            log.AppendLine($"{new string('=', 80)}");
            log.AppendLine($"{PktTime} | {SrcIp}:{SrcPort} => [{DstIp}:{DstPort}, len={PktLen}");
            log.AppendLine($"===> {Sip.Header.RequestOrStatus}");
            log.AppendLine($"Call-ID={Sip.CallID}");
            log.AppendLine($"FromTag={Sip.FromTag}");
            log.AppendLine($"ToTag={Sip.ToTag}");
            log.AppendLine($"Branch={Sip.Branch}, CSeq={Sip.CSeq}");
            log.AppendLine($"{new string('=', 80)}\r\n");
            return log.ToString();
        }

        public string GetSDPLog() {
            if (Sdp == null)
                return "";
            var log = new StringBuilder();
            log.AppendLine("");
            log.AppendLine($"{new string('=', 35)}( {SipCmd} ){new string('=', 90)}");
            log.AppendLine($"分機:{Sip.FromExt} => {Sip.ToExt}");
            log.AppendLine($"time=[{PktTime}] ip=[{SrcIp}]:{SrcPort} => [{DstIp}]:{DstPort}, len={PktLen}");
            log.AppendLine($"ConnectedIP:{Sdp.ConnectedIP}, RTP-Port:{Sdp.RtpPort}");
            log.AppendLine($"{new string('-', 81)}");
            log.AppendLine(Sip.Header.RequestOrStatus);
            log.AppendLine(Sip.Header.Via);
            log.AppendLine(Sip.Header.From);
            log.AppendLine(Sip.Header.To);
            log.AppendLine(Sip.Header.CallID);
            log.AppendLine(Sip.Header.SessionID);
            log.AppendLine(Sip.Header.RemotePartyID);
            log.AppendLine($"{new string('-', 80)}");
            return log.ToString();
        }

        public string GetRTPLog() {
            var log = new StringBuilder();
            var hdr = Rtp.GetHeaderLog();
            log.Append($"RTP: time=[{PktTime}] ip=[{SrcIp}]:{SrcPort} => [{DstIp}]:{DstPort}, pkt.len={PktLen}, PalLoad.Len={Udp.PayloadData.Length}=>{hdr}");
            return log.ToString();
        }

        // 取得對應的 SIP Command
        #region 攔截 SIP 的說明
        // ************************************************************************************
        // 目前只有 application/sdp 的才會抓(就是SDP)，所以很多 sip 命令都會忽略，後續看看有需要再改
        // ************************************************************************************
        // 攔截有 "application/sdp" 的 SIP 封包如下:
        //  1. 有 "application/sdp" 的 "INVITE sip:"
        //     => 主叫端: 主動送出 Invite  (自己是 srcIP，只有主動的 Invite 才會有 sdp)   
        //
        //  2. 有 "application/sdp" 的 "SIP/2.0 200 OK"
        //      有兩種狀況:
        //      2.1 被叫端: 被呼叫，主動回 200OK 給 Server (自己是 srcIP)
        //      2.2 主叫端: 因為之前有送出 Invite，Server 回覆 200OK (自己是 dstIP)
        //
        //  3. 有 "application/sdp" 的 "ACK sip:"
        //     => 被叫端: 等Server回 ACK (自己是 dstIP)
        //
        //  4. BYE sip:
        //     結束通話。(用 CallID 來辨識)         
        #endregion
        public ENUM_SIPCommand GetSipCommand() {
            if (PayLoadStr.Contains("INVITE sip:") && PayLoadStr.Contains("application/sdp") &&
                PayLoadStr.Contains("m=audio") && PayLoadStr.Contains("c=IN")) {
                SipCmd = ENUM_SIPCommand.S_Invite;
                IsSdp = true;
            }
            else if (PayLoadStr.Contains("SIP/2.0 200 OK") && PayLoadStr.Contains("application/sdp") &&
                PayLoadStr.Contains("m=audio") && PayLoadStr.Contains("c=IN")) {
                SipCmd = ENUM_SIPCommand.S_200ok;
                IsSdp = true;
            }
            else if (PayLoadStr.Contains("ACK sip:") && PayLoadStr.Contains("application/sdp") &&
                PayLoadStr.Contains("m=audio") && PayLoadStr.Contains("c=IN")) {
                SipCmd = ENUM_SIPCommand.S_Ack;
                IsSdp = true;
            }
            else if (PayLoadStr.Contains("SIP/2.0") && PayLoadStr.Contains("BYE sip:")) {
                SipCmd = ENUM_SIPCommand.S_Bye;
            }
            return SipCmd;
        }

    }

}
