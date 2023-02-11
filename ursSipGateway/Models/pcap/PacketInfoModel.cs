using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Fluent;
using Org.BouncyCastle.Ocsp;
using PacketDotNet;
using Project.AppSetting;
using Project.Enums;
using Project.Lib;
using Project.ProjectCtrl;
using SharpPcap;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using StackExchange.Profiling.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using WebSocketSharp;

namespace Project.Models {

    public class SipInfoModel {
        public string RequestOrStatus { internal set; get; } = "";
        public string FromTag { internal set; get; } = "";
        public string FromExt { internal set; get; } = "";
        public string ToTag { internal set; get; } = "";
        public string ToExt { internal set; get; } = "";
        public string CallID { internal set; get; } = "";
        public string Branch { internal set; get; } = "";
        public string CSeq { internal set; get; } = "";
        public int? Content_Length { internal set; get; } = null;
        public string Content_Type { internal set; get; } = "";
        public string MessageHeader { internal set; get; } = "";
        public string MessageBody { internal set; get; } = "";
    }    
    
    public class SdpInfoModel {
        public string CallID { set; get; }
        public string SessionID { set; get; }
        public string RemotePartyID { set; get; }
        public int RtpPort { set; get; }
        public bool SendOnly { set; get; } = false;
    }

    public class PacketInfoModel {        
        public TcpPacket tcp { internal get; set; } = null; // 如果是 tcp 封包，就會填入 TcpPacket 物件
        public UdpPacket udp { internal get; set; } = null; // 如果是 udp 封包，就會填入 UdpPacket
        public DateTime CaptureTime { internal get; set; } = DateTime.MinValue;
        public ENUM_IPType IPType { internal set; get; }        

        public string PktTime { internal set; get; }
        public long PktLen { internal set; get; }
        
        public string SrcMac { internal set; get; }
        public string SrcIp { internal set; get; }
        public int SrcPort { internal set; get; }

        public string DstMac { internal set; get; }
        public string DstIp { internal set; get; }
        public int DstPort { internal set; get; }
        public byte[] PayloadData { internal set; get; }


        public ENUM_SIPCommand SipCmd { internal set; get; } = ENUM_SIPCommand.S_Unknown;        
        public string PayLoadStr { internal set; get; }

        // SDP Information
        public SdpInfoModel Sdp { internal set; get; } = null;
        public RtpModel Rtp { internal set; get; } = null;
        public bool IsSdp { internal set; get; } = false;
        public SipInfoModel Sip { internal set; get; } = new SipInfoModel();
        public bool CaptureSuccess { internal set; get; } = true;

        public PacketInfoModel(PacketCapture e) {
            try {
                CaptureTime = e.Header.Timeval.Date;
                PktTime = CaptureTime.ToString("HH:mm:ss.ffffff");
                PktLen = e.Data.Length; // 這個會跟 Wildsark 一樣，這樣比較好除錯
                var rawPacket = e.GetPacket();
                var packet = PacketDotNet.Packet.ParsePacket(rawPacket.LinkLayerType, rawPacket.Data);
                SrcMac = ((EthernetPacket)packet).SourceHardwareAddress.ToString();
                DstMac = ((EthernetPacket)packet).DestinationHardwareAddress.ToString();
                tcp = packet.Extract<PacketDotNet.TcpPacket>();
                if (tcp != null) {
                    SetTcp();
                }
                else {
                    udp = packet.Extract<PacketDotNet.UdpPacket>();
                    if (udp != null)
                        SetUdp();
                    else
                        return;
                }
            }
            catch(Exception ex) {
                CaptureSuccess = false;
            }
        }

        public void SetTcp() {
            IPType = ENUM_IPType.TCP;
            var ipPacket = (PacketDotNet.IPPacket)tcp.ParentPacket;
            SrcIp = ipPacket.SourceAddress.ToString();
            DstIp = ipPacket.DestinationAddress.ToString();
            SrcPort = tcp.SourcePort;
            DstPort = tcp.DestinationPort;
            PayLoadStr = Encoding.UTF8.GetString(tcp.PayloadData);

            PayloadData = new byte[tcp.PayloadData.Length];
            Array.Copy(tcp.PayloadData, 0, PayloadData, 0, tcp.PayloadData.Length);
        }

        public void SetUdp() {
            IPType = ENUM_IPType.UDP;
            var ipPacket = (PacketDotNet.IPPacket)udp.ParentPacket;
            SrcIp = ipPacket.SourceAddress.ToString();
            DstIp = ipPacket.DestinationAddress.ToString();
            SrcPort = udp.SourcePort;
            DstPort = udp.DestinationPort;
            PayLoadStr = Encoding.UTF8.GetString(udp.PayloadData);

            PayloadData = new byte[udp.PayloadData.Length];
            Array.Copy(udp.PayloadData, 0, PayloadData, 0, udp.PayloadData.Length);
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

        public void SetSip() {
            if (string.IsNullOrEmpty(PayLoadStr))
                return;            
            var lines = PayLoadStr.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(y => y.Trim())
                            .ToList();
            if (lines.Count == 0)
                return;
            
            Sip.RequestOrStatus = lines[0];
            foreach (string s in lines) {                
                if (s.Contains("From: ")) {
                    // 取 FromExt
                    if (s.Contains("<sip:")) {
                        Sip.FromExt = s.Split(new string[] { "<sip:" }, StringSplitOptions.RemoveEmptyEntries)[1].Split('@')[0].Trim();
                    }

                    // 取 FromTag
                    if (s.Contains(";tag=")) {
                        Sip.FromTag = s.Split(";tag=")[1].Trim();
                    }                    
                }

                if (s.Contains("To: ")) {
                    // 取 ToExt
                    if (s.Contains("<sip:")) {
                        Sip.ToExt = s.Split(new string[] { "<sip:" }, StringSplitOptions.RemoveEmptyEntries)[1].Split('@')[0].Trim();
                    }
                    // 取 ToTag
                    if (s.Contains(";tag=")) {
                        Sip.ToTag = s.Split(";tag=")[1].Trim();
                    }
                }

                if (s.Contains("Via: ") && s.Contains(";branch=")) {
                    Sip.Branch = s.Split(";branch=")[1].Trim();                    
                }
                if (s.Contains("Call-ID: ")) {
                    Sip.CallID = s.Split("Call-ID: ")[1].Trim();
                }                
                if (s.Contains("CSeq: ")) {
                    Sip.CSeq = s.Split("CSeq:")[1].Trim();
                }
                if (s.Contains("Content-Length: ")) {
                    var tmp = s.Split("Content-Length:")[1].Trim();
                    if (int.TryParse(tmp, out int len))
                        Sip.Content_Length = len;
                }
                if (s.Contains("Content-Type: ")) {
                    Sip.Content_Type = s.Split("Content-Type:")[1].Trim();
                }



                // get MessageHeader & MessageBody
                SetSipMessageHeaderAndBody();                     
                
                //// Sip.MediaExtra <= 沒甚麼用途，暫不提供
                //if (s.Contains("a=sendrecv")) {
                //    Sip.MediaExtra.Add("a=sendrecv");
                //}
                //if (s.Contains("a=recvonly")) {
                //    Sip.MediaExtra.Add("a=recvonly");
                //}
                //if (s.Contains("a=sendonly")) {
                //    Sip.MediaExtra.Add("a=sendonly");
                //}
                //if (s.Contains("a=inactive")) {
                //    Sip.MediaExtra.Add("a=inactive");
                //}
            }

            // 此處判斷 SipCmd 是否完整，有兩種可能:
            //      1. 沒有 Content-Length 出現，因為出現在下一包
            //      2. Content-Length 有值，但跟 MessageBody 長度不一致
            if (PayLoadStr.Contains("Via: ") && PayLoadStr.Contains("From: ") && PayLoadStr.Contains("To: ")) {
                // 此時尚不完全: MessageBody 長度不夠或根本沒有 Content-Length(因為在下一個封包)
                SipCmd = ENUM_SIPCommand.S_Incomplete;
                if (Sip.Content_Length.HasValue) {
                    if (Sip.Content_Length.Value == Sip.MessageBody.Length)
                        SipCmd = ENUM_SIPCommand.S_Completed; // 完整了
                }
            }
        }

        public void SetSipMessageHeaderAndBody() {
            if (string.IsNullOrEmpty(PayLoadStr))
                return;

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

        public void SetSdp() {
            Sdp = new SdpInfoModel() {
                CallID = lib_sip.GetCallID(PayLoadStr),
                SessionID = lib_sip.GetSessionID(PayLoadStr),
                RemotePartyID = lib_sip.GetRemotePartyID(PayLoadStr),
                RtpPort = lib_sip.GetRTPPort(PayLoadStr),
                SendOnly = PayLoadStr.Contains("a=sendonly")
            };
        }


        public void SetRtp() {
            Rtp = new RtpModel(udp.PayloadData, CaptureTime);
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
            else if (GlobalVar.AppSettings.Monitor.MonitorType ==  ENUM_SIP_MonitorType.MAC) {
                return CheckMacAddrIsMonitoring();
            }
            else {
                return false;
            }
        }


        // 檢查封包的 srcPort 或 dstPort 是否等於監控的 SIP Port
        public bool CheckIfSipPort() {
            var sipPort = GlobalVar.AppSettings.Monitor.SipPort;
            return (SrcPort == sipPort || DstPort == sipPort);
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
            var timeNow = DateTime.Now;
            var log = new StringBuilder();
            var sdp = IsSdp ? "/SDP" : "";
            log.AppendLine("");
            log.AppendLine($"{new string('=', 37)} {IPType.ToString()}{sdp} {new string('=', 37)}");
            log.AppendLine($"time=[{PktTime}] ip=[{SrcIp}]@{SrcPort} => [{DstIp}]@{DstPort}, mac=[{SrcMac}]=>[{DstMac}], len={PktLen}");
            log.AppendLine($"{new string('=', 80)}");
            log.AppendLine($"{PayLoadStr}");
            log.AppendLine($"{new string('-', 80)}");
            log.AppendLine($"花費時間 = {(DateTime.Now - timeNow).TotalMilliseconds} ms");
            log.AppendLine("");
            return log.ToString();
        }

        public string GetSIPLog() {
            var exclude = Sip.RequestOrStatus.Contains("NOTIFY") || Sip.RequestOrStatus.Contains("SUBSCRIBE") ||
                        Sip.CSeq.Contains("NOTIFY") || Sip.CSeq.Contains("SUBSCRIBE") || Sip.CSeq.Contains("REGISTER");
            if (exclude) {
                return "";
            }

            var log = new StringBuilder();
            log.AppendLine("");
            log.AppendLine($"{new string('=', 80)}");
            log.AppendLine($"{PktTime} | {SrcIp}:{SrcPort} => [{DstIp}:{DstPort}, len={PktLen}");
            log.AppendLine($"===> {Sip.RequestOrStatus}");
            log.AppendLine($"Call-ID={Sip.CallID}");
            log.AppendLine($"FromTag={Sip.FromTag}");
            log.AppendLine($"ToTag={Sip.ToTag}");
            log.AppendLine($"Branch={Sip.Branch}, CSeq={Sip.CSeq}");
            //if (Sip.MediaExtra.Count > 0) {
            //    log.AppendLine($"** MediaExtra={string.Join("; ", Sip.MediaExtra)}");
            //}
            log.AppendLine($"{new string('=', 80)}\r\n");
            return log.ToString();
        }

        // TODO: 把 lib_sip 的 function 全部整理到 PacketInfoModel 中

        public string GetSDPLog() {
            var timeNow = DateTime.Now;
            var log = new StringBuilder();
            log.AppendLine("");
            log.AppendLine($"{new string('=', 35)}( {SipCmd} ){new string('=', 90)}");
            log.AppendLine($"分機:{lib_sip.GetFromExtNo(PayLoadStr)} => {lib_sip.GetToExtNo(PayLoadStr)}");
            log.AppendLine($"time=[{PktTime}] ip=[{SrcIp}]:{SrcPort} => [{DstIp}]:{DstPort}, len={PktLen}");
            log.AppendLine($"ConnectedIP:{lib_sip.GetConnectIPAddr(PayLoadStr)}, RTP-Port:{lib_sip.GetRTPPort(PayLoadStr)}");            
            log.AppendLine($"{new string('-', 81)}");

            var lines = PayLoadStr.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim()).ToList();
            if (lines.Count > 0) {
                log.AppendLine(lines[0]); // 列印第 1 行 Request
                foreach (string s in lines) {
                    if (s.Contains("Via: "))
                        log.AppendLine(s);
                    else if (s.Contains("From: "))
                        log.AppendLine(s);
                    else if (s.Contains("To: "))
                        log.AppendLine(s);
                    else if (s.Contains("Call-ID: "))
                        log.AppendLine(s);
                    else if (s.Contains("Session-ID: "))
                        log.AppendLine(s);
                    else if (s.Contains("Remote-Party-ID: "))
                        log.AppendLine(s);
                    //
                    if (s.Contains("a=sendrecv")) {
                        log.AppendLine("*** a=sendrecv ***");
                    }
                    else if (s.Contains("a=inactive")) {
                        log.AppendLine("*** a=inactive ***");
                    }
                    else if (s.Contains("a=sendonly")) {
                        log.AppendLine("*** a=sendonly ***");
                    }
                    else if (s.Contains("a=recvonly")) {
                        log.AppendLine("*** a=recvonly ***");
                    }
                }
            }            
            log.AppendLine($"{new string('-', 80)}");
            log.AppendLine($"花費時間 = {(DateTime.Now - timeNow).TotalMilliseconds} ms");            
            return log.ToString();
        }

        public string GetRTPLog() {            
            var log = new StringBuilder();
            //var hdr = $"payloadType={Rtp.Header.PayloadType}, SSRC=0x{Convert.ToString(Rtp.Header.SSRC, 16).ToUpper()}, Seq={Rtp.Header.SeqNum}, Time={Rtp.Header.Timestamp}";            
            var hdr = Rtp.GetHeaderLog();
            log.Append($"RTP: time=[{PktTime}] ip=[{SrcIp}]:{SrcPort} => [{DstIp}]:{DstPort}, pkt.len={PktLen}, PalLoad.Len={udp.PayloadData.Length}=>{hdr}");            

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
