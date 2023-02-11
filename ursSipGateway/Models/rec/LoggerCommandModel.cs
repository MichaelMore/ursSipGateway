using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    public class LoggerCommandModel {
        public string AccountID { get; set; } = string.Empty; // 呼叫者帳號
        public string Password { get; set; } = string.Empty; // 呼叫者密碼
        public string Sender { get; set; } = string.Empty; // 呼叫者是誰，隨便填，目前沒想法
        public string AgentID { get; set; } = string.Empty; // 呼叫者的 AgentID，沒有就填 ""

        public RTPDestinationModel RtpDest { set; get; } = null;   // 就是監聽者這邊的 IP        
        public string ExtNo { set; get; } = string.Empty; // 此命令的對象分機
        public string Command { set; get; } = string.Empty; // 命令本身，大小寫皆可
        public string Params { set; get; } = string.Empty;  // 命令的相關參數
    }

    // 傳送 RTP 監聽封包的目的 IP/Port，如果沒有，就是 null
    public class RTPDestinationModel {
        public string Host { get; set; } = string.Empty; // 呼叫者是誰，隨便填，目前沒想法
        public int TxPort { get; set; }
        public int RxPort { get; set; }
    }
}
