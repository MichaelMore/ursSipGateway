using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    // **************************** SIP 攔截說明 *******************************************
    // 目前只有 application/sdp 的才會抓(就是SDP)，所以很多 sip 命令都會忽略，後續看看有需要再改
    // ************************************************************************************
    // 攔截有 "application/sdp" 的 SIP 封包如下:
    //  1. 有 "application/sdp" 的 "INVITE sip:"
    //     => 主叫端: 主動送出 Invite  (自己是 srcIP，只有主動的 Invite 才會有 sdp)   
    //     => 有攔截到，就產生(CreateSipDialog)物件: SipDialogModel，並塞入 _sipDialogList
    //     => 上面的 SipDialogModel，Status = waiting
    //     => 注意: 此 Invite 封包，只有 CallID + FromTag，沒有 ToTag，要等後續的 200OK 才有 ToTag(所以 Status = Waiting)
    //
    //  2. 有 "application/sdp" 的 "SIP/2.0 200 OK"
    //      有兩種狀況:
    //      2.1 被叫端: 被呼叫，主動回 200OK 給 Server (自己是 srcIP)
    //          => 有攔截到，就產生(CreateSipDialog)物件: SipDialogModel，並塞入 _sipDialogList
    //          => 此時的 200OK，CallID + FromTag + ToTag 都有，但 Status 依然是 Waiting，要等最後的 Server Ack
    //      2.2 主叫端: 因為之前有送出 Invite，Server 回覆 200OK (自己是 dstIP)
    //          => 更新在 _sipDialogList 中的 SipDialogModel 物件，並塞入 ToTag
    //          
    //  3. 有 "application/sdp" 的 "ACK sip:"
    //     => 被叫端: 等Server回 ACK (自己是 dstIP)
    //
    //  4. BYE sip:
    //     結束通話。(用 CallID + FromTag + ToTag 來辨識) 
    // 
    //  以上參考 PacketInfoModel.GetSipCommand

    public class RecRtpModel {
        public string DialogID { get; set; } = string.Empty;
        // 只對 Flag = StartRec/StopRec 有用
        public ENUM_CallDirection CallDir { get; set; } = ENUM_CallDirection.Unknown;
        public ulong PktIndex { get; set; }
        public ENUM_IPDir IpDir { get; set; } // parser 用這個欄位來辨識是發送(snd)或接收(rcv)封包
        public DateTime StartTalkTime { get; set; }
        public DateTime? StopTalkTime { get; set; }
        public string ExtNo { get; set; } = string.Empty;
        public string CallID { get; set; } = string.Empty;
        public string SessionID { get; set; } = string.Empty;        
        public RtpModel Rtp { get; set; }
        public ENUM_RTP_RecFlag Flag { get; set; }
        public string CallerID { get; set; } = string.Empty;  // = From
        public string CalledID { get; set; } = string.Empty;  // = To
        public DateTime? PktCaptureTime { get; set; } = null;

    }
}
