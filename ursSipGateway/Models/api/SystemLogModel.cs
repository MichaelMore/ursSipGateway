using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {    

    public class SystemLogModel {        
        public string LoggerID { get; set; }
        public string LoggerName { get; set; }
        public string LogClass { get; set; }
        public int LogType { get; set; } = 1; // 1:info, 2:alarm, 3:error, 4:fatal
        public string Msg { get; set; } = "";

        public SystemLogModel() {            
        }


    }
}
