using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {

    public class SipByeModel {
        public string Ip { set; get; }
        public string Extn { set; get; }
        public string Mac { set; get; }
        public ENUM_SIP_MonitorType MonType { set; get; }
    }
}
