using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    #region **************************** SIP 攔截說明 v1.0 *******************************************
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
    // 
    //  以上參考 PacketInfoModel.GetSipCommand
    #endregion

    #region **************************** SIP 攔截說明 v1.1 *******************************************
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
    // 
    //  以上參考 PacketInfoModel.GetSipCommand
    #endregion

    //TODO: 整理 SipDialogModel 物件及 Method

    // 注意: SipSdpModel 是以 CallID 為主，每一隻分機會有多個 CallID 在錄音
    public class SipDialogModel {
        public string ID { get; set; } = ""; // 要等到 Status=Talking 時才會有 ID，也就是 FromTag+ToTag 都有的時候
        public ulong TotalPkt { get; set; } = 0;
        public string ExtNo { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Mac { get; set; } = string.Empty;
        public bool Invite { get; set; } = false; // 主動 Invite => 主叫端
        public string FromExt { get; set; } = string.Empty; 
        public string ToExt { get; set; } = string.Empty;

        // 0: init,  1. 等待 200OK(主叫)或 ACK(被叫)，2: 開始通話(錄音), 3: 正在 hold(主動), 4: 正在 hold(被動)
        public ENUM_SIP_Dialog_Status Status { get; set; } = ENUM_SIP_Dialog_Status.Init ;

        // ============ 以下 3 個合起來 = 1 個 Dialog =======
        public string CallID { get; set; } = string.Empty;
        public string FromTag { get; set; } = string.Empty;
        public string ToTag { get; set; } = string.Empty;
        //==================================================

        public string SessionID { get; set; } = string.Empty;
        public int RtpPort { get; set; } = 0;        
        public DateTime InitTime { get; set; } = DateTime.Now;
        public DateTime? StartTalkTime { get; set; } = null;
        public DateTime LastPacketTime { get; set; } = DateTime.Now; // 最後一次取得封包的時間，不管收、發都會被更新

        // 標註 Hold 的狀態
        public ENUM_SIP_Dialog_Hold HoldStatus { get; private set; } = ENUM_SIP_Dialog_Hold.None;
        public DateTime? HoldStartTime { get; internal set; } = DateTime.Now; // (主動/被動) Hold 的開始時間，如果 = null，表示不是在 hold 狀態

        public SipDialogModel() {
        }

        public void PressToHold() {
            HoldStatus = ENUM_SIP_Dialog_Hold.PressToHold;
            HoldStartTime = DateTime.Now;
        }

        public void SetBeHold() {
            HoldStatus = ENUM_SIP_Dialog_Hold.BeHeld;
            HoldStartTime = DateTime.Now;
        }

        public void CancelHold() {
            HoldStatus = ENUM_SIP_Dialog_Hold.None;
        }

    }
}
