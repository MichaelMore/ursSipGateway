using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {    

    public class RecDataModel {
        public long LoggerSeq { get; set; } = 0;
		public string LoggerID { get; set; } = string.Empty;
        public string LoggerName { get; set; } = string.Empty;
        public int RecID { get; set; } = 0;
        public string RecDate { get; set; } = string.Empty;
        public string RecFolder { get; set; } = string.Empty;
        public string RecFileName { get; set; } = string.Empty;
        public string RecStartTime { get; set; } = string.Empty;
        public string RecStopTime { get; set; } = string.Empty;
        public int RecLen { get; set; } = 0;
        public int InboundLen { get; set; } = 0;
        public int OutboundLen { get; set; } = 0;
        public string CallerID { get; set; } = string.Empty;
        public string DTMF { get; set; } = string.Empty;
        public int CallType { get; set; } // 4: Inbound, 5: Outbounc
        public int RecType { get; set; } = 4; //3: SOD, 4: Schedule, 5: Continous
        public string DNIS { get; set; } = string.Empty;
        public int ChType { get; set; } = 6; //1: Analog, 2: Digital, 3: A-SMDR, 4: T1/E1, 5: SynIP, 6: SIP
        public int ChID { get; set; } // 等於 ExtNo
        public string ChName { get; set; } = "N/A"; // 不須指定，API 會自動 join TB_Channel中的 ChannelName
        public string AgentID { get; set; } = "N/A"; // 不須指定，API 會自動 join TB_Channel中的 AgentID
        public string AgentName { get; set; } = "N/A"; // 不須指定，API 會自動 join TB_Channel中的 AgentName
        public string ExtNo { get; set; } = string.Empty;
        public int TriggerType { get; set; } = 6; // 0: 壓降, 1: 聲音, 2:壓降+聲音, 3: DCh, 4: API, 5: CTI, 6: SIP/IP
        public int MediaType { get; set; } = 1; // 1: audio, 2: video
        public string CallerName { get; set; } = string.Empty;

        public RecDataModel() {            
        }

    }
}
