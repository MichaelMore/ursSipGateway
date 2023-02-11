using Project.Helpers;
using SharpPcap;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Project.Models {

    public class PcapDeviceModel {
        public ILiveDevice Device { internal set; get; }


        // private
        private string DeviceString { set; get; }
        private List<string> DeviceStrList { set; get; } = new List<string>();

        public PcapDeviceModel(ILiveDevice device) {
            Device = device;
            if (device != null) {
                DeviceString = device.ToString();
                DeviceStrList = DeviceString.Split( new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
            }
        }

        public string GetMac() {            
            return $"{Device.MacAddress}";
        }

        public string GetDesc() {
            return $"{Device.Description}";
        }

        public string GetFriendlyName() {
            var ret = "";
            try {
                foreach (var s in DeviceStrList) {
                    if (s.Contains("FriendlyName:")) {
                        ret = s.Split("FriendlyName:")[1].Trim();
                    }
                }
            }
            catch (Exception ex) {
                ret = "";
            }            
            return ret;
        }

        public string GetIPV4() {
            var ret = "";
            try {
                foreach (var s in DeviceStrList) {
                    if (s.Contains("Addr:")) {
                        var ipv4 = s.Split("Addr:")[1].Trim();
                        if (!string.IsNullOrEmpty(ipv4)) {
                            // 至少要是 10.1.1.1 = 8 碼, 最大: 255.255.255.255
                            if (ipv4.Length >= 8 && ipv4.Length <= 15) {
                                ret = ipv4;
                                break;
                            }
                        }
                    }
                }
            }
            catch(Exception ex) {
                ret = "";
            }
            return ret;
        }

    }


}
