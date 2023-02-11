using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    public class ChannelStatusModel {
        public long LoggerSeq { get; set; }
        public string ExtNo { get; set; }
        public int LineStatus { get; set; }

        //public string ChName { get; set; } // 不用指定，API 會自動 Join TB_Channel 的 ChannelName
        //public string AgentName { get; set; } // 不用指定，API 會自動 Join TB_Channel 的 AgentName

        public int CallType { get; set; }
        public string CallerID { get; set; }        
        public string DTMF { get; set; }
        public DateTime StartTime { get; set; }
    }
}
