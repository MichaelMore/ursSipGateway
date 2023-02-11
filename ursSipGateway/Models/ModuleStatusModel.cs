using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Project.Models {

    public class ModuleStatusModel {
        public long ModuleSeq { set; get; }
        public string ServerID { set; get; }
        public string ModuleClass { set; get; }
        public string ModuleID { set; get; }
        public string ModuleVer { set; get; }
        public int Status { set; get; }
        public DateTime LastResponse { set; get; }


    }


}
