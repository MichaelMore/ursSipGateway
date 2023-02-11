using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    public class RecFileModel {
        public string ExtNo { get; set; } = string.Empty;
        public ENUM_CallDirection CallDir { get; set; } = ENUM_CallDirection.Unknown;
        public string CallID { get; set; } = string.Empty;
        public DateTime RecStartTime { get; set; }
        public DateTime RecStopTime { get; set; }
        public int Duration { get; set; }
        public string CallerID { get; set; } = string.Empty;
        public string CalledID { get; set; } = string.Empty;
        public string SendRawFileName { get; set; } = string.Empty;
        public string RecvRawFileName { get; set; } = string.Empty;        
        public List<SsrcControlModel> SendSSRCList { get; set; } = new List<SsrcControlModel>();
        public List<SsrcControlModel> RecvSSRCList { get; set; } = new List<SsrcControlModel>();
        public List<HoldModel> HoldList { get; set; } = new List<HoldModel>();
    }
}
