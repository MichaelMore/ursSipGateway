using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using System.Security.Cryptography;
using System.Globalization;

namespace Project.Lib {
    public static partial class Extensions {        
        public static string ToStdStr(this DateTime @this, string dateSep = "-", string timeSep = ":", int fractionDigit = 0) {
            var frac = "";
            if (fractionDigit > 0) {
                var len = fractionDigit;
                if (len > 6)
                    len = 6;
                frac = $".{new string('f', len)}";
            }
            var format = $"yyyy{dateSep}MM{dateSep}dd HH{timeSep}mm{timeSep}ss{frac}";
            return @this.ToString(format);
        }

        public static string ToTimeStr(this DateTime @this, string timeSep = ":", int fractionDigit = 0) {
            var frac = "";
            if (fractionDigit > 0) {
                var len = fractionDigit;
                if (len > 6)
                    len = 6;
                frac = $".{new string('f', len)}";
            }
            var format = $"HH{timeSep}mm{timeSep}ss{frac}";
            return @this.ToString(format);
        }




    }

}

