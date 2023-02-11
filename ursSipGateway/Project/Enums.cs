using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web;

namespace Project.Enums {
    public enum ENUM_PayloadType {
        PT_PCMU = 0,
        PT_GSM = 3,
        PT_G723 = 4,
        PT_LPC = 7,
        PT_PCMA = 8,
        PT_G722 = 9,        
        PT_L16_ST = 10,
        PT_L16_MONO = 11,
        PT_G729 = 18
    }


    // IP 的傳送位置
    public enum ENUM_RTP_RecFlag {
        StopRec = -1,   // 停止錄音
        StartRec = 0,   // 開始錄音        
        Recording = 1,   // 錄音中
        StartPressToHold = 2, // 開始按下 hold(主動)
        StartBeHeld = 3, // 開始被Hold(被動)
        StopHeld = 4, // 取消 Hold(主被動通用)
    }

    // SIP Command 交握(Handshake) 狀態
    public enum ENUM_SIP_Dialog_Status {
        Init = 0,
        Waiting = 1,   // 正在等待 SIP 的 200Ok 或 Ack
        Talking = 2    // 完成 handshake，開始通話        
    }

    public enum ENUM_SIP_Dialog_Hold {        
        None = 0, 
        PressToHold = 1,   // 正在Hold(主動)
        BeHeld = 2, // 正在Hold(被動)
    }



    // IP 的傳送位置
    public enum ENUM_IPDir {
        [Description("未知")]
        Unknown = 0,

        [Description("發送端")]
        Send = 1,   

        [Description("接收端")]
        Recv = 2      
    }

    // 錄音間控設備的型態:
    // ip:  設備的 ip 固定，用 ip 來辨識設備
    // mac: 設備的 ip 不固定，用 mac 來辨識設備(mac會固定，但不方便辨識)
    public enum ENUM_SIP_MonitorType {        
        IP = 1,
        MAC = 2
    }

    // IP通訊類型
    public enum ENUM_IPType {        
        TCP = 1,
        UDP = 2
    }

    // 封包的結構類型
    // SipCommand: 用來描述 SIP Signal 的可見文字(可以用 TCP 或 UDP 來傳送)
    // RTP: 由 UDP 傳送，比較少看到 TCP(速度慢)， Header(12 Byte) + 音檔 RawData
    
    public enum ENUM_CallDirection {
        Unknown = -1,
        Outbound = 0,
        Inbound = 1
    }

    public enum ENUM_SipStatus {
        Idle = 0,
        Ivite = 1,
        Ringing = 2,
        Talking = 3,
        Hold = 4
    }

    public enum ENUM_SIPCommand {
        S_Unknown = -1,
        S_Incomplete = 0, // 未完整的 SIP 命令(有 Via:, From:, To: 但 Content-Length 未滿足)
        S_Completed = 1,  // 完整的 SIP_Command >= 1 就是完整，< 1 就不完整
        S_Invite = 2,
        S_200ok = 3,
        S_Bye = 4,
        S_Ack = 5        
    }

    public enum ENUM_LineStatus {
        [Description("線路錯誤")]
        Failed = -1,

        [Description("閒置")]
        Idle = 0,

        [Description("響鈴")]
        Ring = 1,

        [Description("撥出")]
        Inbound = 2,

        [Description("外撥")]
        Outbound = 3,

        [Description("內線通話")]
        Intercom = 4
    }

    public enum ENUM_SqlLogType : int {
        [Description("SqlTrace")]
        Trace = 0,

        [Description("SqlError")]
        Error = 1
    }

    public enum ENUM_LogType {
        [Description("訊息")]
        Info = 1,
        [Description("告警")]
        Alarm = 2,
        [Description("錯誤")]
        Error = 3,
        [Description("嚴重")]
        Fatal = 4
    }  
        
}