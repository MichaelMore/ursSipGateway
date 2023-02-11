using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    public class MonitorRtpModel {        
        public ENUM_IPDir IpDir { get; set; } // parser 用這個欄位來辨識是發送(snd)或接收(rcv)封包        
        public RtpModel Rtp { get; set; }
        public ENUM_RTP_RecFlag Flag { get; set; } 
    }
}
