using Newtonsoft.Json;
using Project.Enums;
using Project.Lib;
using Project.ProjectCtrl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Project.Models {

    public class HoldModel {
        public ENUM_IPDir Dir; // Hold 的方向
        public DateTime? StartPacketTime;
        public DateTime? EndPacketTime;
        public decimal HoldSec = 0;

        public decimal SetHoldTime() {
            if (StartPacketTime.HasValue && EndPacketTime.HasValue) {
                HoldSec = (decimal)Math.Round((EndPacketTime.Value - StartPacketTime.Value).TotalSeconds, 2);
            }
            return HoldSec;
        }

        public string StartTimeSec() {
            return StartPacketTime.HasValue ? StartPacketTime.Value.ToTimeStr(":", 3) : "null";
        }

        public string EndTimeSec() {
            return EndPacketTime.HasValue ? EndPacketTime.Value.ToTimeStr(":", 3) : "null";
        }

        public string GetHoldDurationSec() {
            return $"{StartTimeSec()} ~ {EndTimeSec()}";
        }
    }


}
