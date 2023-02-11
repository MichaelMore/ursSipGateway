using SharpPcap.LibPcap;
using SharpPcap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Lib {
    internal static class lib_pcap {
        // 檢查是 IPV4、IPV6...還是都不是
        public static System.Net.Sockets.AddressFamily CheckIfIPAddress(string ipStr) {
            AddressFamily ret = AddressFamily.Unknown;
            IPAddress address;
            if (IPAddress.TryParse(ipStr, out address)) {
                switch (address.AddressFamily) {
                    case System.Net.Sockets.AddressFamily.InterNetwork:
                        ret = AddressFamily.InterNetwork; // IPV4
                        break;
                    case System.Net.Sockets.AddressFamily.InterNetworkV6:
                        ret = AddressFamily.InterNetworkV6; // IPV6
                        break;
                    default:
                        ret = AddressFamily.Unknown;
                        break;
                }
            }
            return ret;
        }

        //public static string GetIPAddress_V4(ICaptureDevice dev) {
        //    var s = "";
        //    var ipStr = "";


        //    WinPcapDevice windev = (WinPcapDevice)dev;
        //    foreach (PcapAddress addr in windev.Addresses) {
        //        if (addr.Addr != null && addr.Addr.ipAddress != null) {
        //            s = addr.Addr.ipAddress.ToString();
        //            if (CheckIfIPAddress(s) == AddressFamily.InterNetwork) {
        //                ipStr = s;
        //                break;
        //            }
        //        }
        //    }

        //    return ipStr;
        //}

        //public static string GetIPAddress_V4(ICaptureDevice dev) {
        //    var s = "";
        //    var ipStr = "";
        //    WinPcapDevice windev = (WinPcapDevice)dev;
        //    foreach (PcapAddress addr in windev.Addresses) {
        //        if (addr.Addr != null && addr.Addr.ipAddress != null) {
        //            s = addr.Addr.ipAddress.ToString();
        //            if (CheckIfIPAddress(s) == AddressFamily.InterNetwork) {
        //                ipStr = s;
        //                break;
        //            }
        //        }
        //    }

        //    return ipStr;
        //}

    }
}
