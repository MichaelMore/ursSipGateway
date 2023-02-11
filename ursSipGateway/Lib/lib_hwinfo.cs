using System;
using System.Collections.Generic;
using System.Management;
using Newtonsoft.Json;
using System.Text;


namespace Project.Lib {
    public class DeviceInfoModel {
        public string CpuID { private set; get; }
        public string BaseBoardID { private set; get; }
        public string DiskVolumeSN { private set; get; } // 專指 Disk C 的序號
        public string BiosName { private set; get; }
        public string BiosVersion { private set; get; }
        public DeviceInfoModel(string cpuID, string boardID, string diskSN, string biosName, string biosVer) { 
            CpuID = cpuID;
            BaseBoardID= boardID;
            DiskVolumeSN = diskSN;
            BiosName= biosName;
            BiosVersion = biosVer;
        }
    }

    internal static class lib_hwInfo
    {
        private static int lastErrorCode = 0;
        static lib_hwInfo() {                        
        }

        //注意：這個 GetHWInfo 無法取得多 CPUID，要另外呼叫 GetX1() 才可以
        private static void GetHWInfo(string key, ref Dictionary<string, string> dic) {
            string[] strList;
            string str;
            ushort[] shortList;

            dic.Clear();
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from " + key);
            try {
                foreach(ManagementObject obj in searcher.Get()) {
                    foreach(PropertyData p in obj.Properties) {
                        if(p.Value != null && p.Value.ToString() != "") {
                            switch(p.Value.GetType().ToString()) {
                                case "System.String[]":
                                    strList = (string[])p.Value;
                                    str = "";
                                    foreach(string s in strList) {
                                        str += s + " ";
                                    }
                                    dic.Add(p.Name, str);
                                    break;
                                //
                                case "System.UInt16[]":
                                    shortList = (ushort[])p.Value;
                                    str = "";
                                    foreach(ushort st in shortList) {
                                        str += st.ToString() + " ";
                                    }
                                    dic.Add(p.Name, str);
                                    break;
                                //
                                default:
                                    dic.Add(p.Name, p.Value.ToString());
                                    break;
                            }
                        }
                    }
                }
            }
            catch {
            }
        }

        private static void GetCpuInfo(ref Dictionary<string, string> dic) {
            GetHWInfo("Win32_Processor", ref dic);
        }

        private static void GetOperationSystemInfo(ref Dictionary<string, string> dic) {
            GetHWInfo("Win32_OperatingSystem", ref dic);
        }

        private static void GetBoardInfo(ref Dictionary<string, string> dic) {
            GetHWInfo("Win32_BaseBoard", ref dic);
        }


        // 注意：這裡的 GetCpuID 不會得到多個 CPU ID，在mktLib2008.dll 的 GetX1() 是可以的
        // 因此，不用這裡的GetCpuID。        
        //public string GetCpuID()
        //{
        //    var cpuDic = new Dictionary<string, string>();
        //    GetCpuInfo(ref cpuDic);
        //    if (cpuDic.ContainsKey("ProcessorId"))
        //        return cpuDic["ProcessorId"];
        //    else
        //        return "";
        //}

        //public string GetBoardID()
        //{
        //    var boardDic = new Dictionary<string, string>();
        //    GetBoardInfo(ref boardDic);
        //    if (boardDic.ContainsKey("SerialNumber"))
        //        return boardDic["SerialNumber"];
        //    else
        //        return "";
        //}

        internal static string GetCpuName() {
            var cpuDic = new Dictionary<string, string>();
            GetCpuInfo(ref cpuDic);
            if(cpuDic.ContainsKey("Name"))
                return cpuDic["Name"];
            else
                return "";
        }

        internal static string GetOSName() {
            var osDic = new Dictionary<string, string>();
            GetOperationSystemInfo(ref osDic);
            if(osDic.ContainsKey("Caption"))
                return osDic["Caption"];
            else
                return "Unknown";
        }

        internal static long GetPhysicalMemInMB() {
            string Query = "SELECT Capacity FROM Win32_PhysicalMemory";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(Query);

            UInt64 Capacity = 0;
            foreach(ManagementObject WniPART in searcher.Get()) {
                Capacity += Convert.ToUInt64(WniPART.Properties["Capacity"].Value);
            }
            return (long)Capacity / 1024 / 1024;
        }

        internal static decimal GetPhysicalMemInGB() {
            var mb = GetPhysicalMemInMB();
            return Math.Round(Convert.ToDecimal(mb / 1024), 2);
        }

        internal static string GetDeviceInfo(out string err) {
            err = "";
            DeviceInfoModel devInfo = new DeviceInfoModel(GetX1(), GetX2(), GetX3(), GetX4(), GetX5());            
            if (devInfo.CpuID == "" || devInfo.BaseBoardID == "") {
                err = "failed to get device info.";
                return ""; // error
            }
            else {
                string jsonStr = JsonConvert.SerializeObject(devInfo);
                return lib_encode.EncryptAES256(jsonStr, lib_license.getPkey(), lib_license.getIv(), out err);
            }            
        }
        
        // 取得 CPUID，可以取得多組 CPU ID，中間以 ; 區隔
        internal static string GetX1() {
            string x1 = "";
            string keyWord = getWin32() + "_" + getProcessor();
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from "+ keyWord); // Win32_Processor
            try {
                foreach (ManagementObject share in searcher.Get()) {
                    foreach (PropertyData PC in share.Properties) {
                        if (PC.Name == getProcessor()+ getId()) // "ProcessorId"
                            x1 = x1 + PC.Value.ToString() + ";";
                    }
                }
                x1 = x1.TrimEnd(';');
            }
            catch (Exception) {
                x1 = "";
            }
            return x1;
        }

        // 取得 BaseBoardID
        internal static string GetX2() {
            string x2 = "";
            var keyWord = getWin32() + "_" + getBaseBoard();
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from "+ keyWord); // Win32_BaseBoard
            try {
                foreach (ManagementObject share in searcher.Get()) {
                    foreach (PropertyData PC in share.Properties) {
                        if (PC.Name == getSN()) // "SerialNumber"
                            x2 = x2 + PC.Value.ToString() + ";";
                    }
                }
                x2 = x2.TrimEnd(';');
            }
            catch (Exception) {
                x2 = "";
            }
            return x2;
        }

        // 取得 Disk C 的 VolumeSerialNumber(專指 Disk C 的序號)
        internal static string GetX3() {
            string x3 = "None";
            //string keyWord = getWin32() + "_" + getProcessor();
            string keyWord = "Win32_LogicalDisk";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from " + keyWord); // Win32_Processor
            ManagementObject xShare = null;
            try {
                foreach(ManagementObject share in searcher.Get()) {
                    foreach(PropertyData PC in share.Properties) {
                        if(PC.Name == "DeviceID") {
                            if(PC.Value.ToString().ToUpper() == "C:") {
                                xShare = share;
                                break;
                            }
                        }
                    }
                    if(xShare != null) {
                        foreach(PropertyData PC in xShare.Properties) {
                            if(PC.Name == "VolumeSerialNumber") {
                                x3 = PC.Value.ToString();
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            catch(Exception) {
                x3 = "None";
            }
            return x3;
        }

        // 取得 BIOS  Name
        internal static string GetX4() {
            var x4 = "None";
            string keyWord = getWin32() + "_BIOS";            
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from " + keyWord); // Win32_Processor            
            try {
                foreach(ManagementObject share in searcher.Get()) {
                    foreach(PropertyData PC in share.Properties) {
                        if(PC.Name == "Name") {
                            x4 = PC.Value.ToString();
                            break;
                        }
                    }
                }
            }
            catch(Exception) {
                x4 = "None";
            }
            return x4;
        }

        // 取得 BIOS Version
        internal static string GetX5() {
            var x5 = "None";
            string keyWord = getWin32() + "_BIOS";
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("select * from " + keyWord); // Win32_Processor            
            try {
                foreach(ManagementObject share in searcher.Get()) {
                    foreach(PropertyData PC in share.Properties) {
                        if(PC.Name == "Version") {
                            x5 = PC.Value.ToString();
                            break;
                        }
                    }
                }
            }
            catch(Exception) {
                x5 = "None";
            }
            return x5;
        }
        
        private static string getWin32() {
            int[] chars = new int[] {87, 105, 110, 51, 50}; // "Win32"
            var s = "";
            for (var i = 0; i < chars.Length; i++)
                s = s + ((char)chars[i]).ToString();
            return s;
        }

        private static string getProcessor() {
            int[] chars = new int[] { 80, 114, 111, 99, 101, 115, 115, 111, 114}; // "Processor"
            var s = "";
            for (var i = 0; i < chars.Length; i++)
                s = s + ((char)chars[i]).ToString();
            return s;
        }

        private static string getId() {
            int[] chars = new int[]{73, 100}; // "Id"
            var s = "";
            for (var i = 0; i < chars.Length; i++)
                s = s + ((char)chars[i]).ToString();
            return s;
        }

        private static string getBaseBoard() {
            int[] chars = new int[] {66, 97, 115, 101, 66, 111, 97, 114, 100}; // "BaseBoard"
            var s = "";
            for (var i = 0; i < chars.Length; i++)
                s = s + ((char)chars[i]).ToString();
            return s;
        }

        private static string getSN() {
            int[] chars = new int[] { 83, 101, 114, 105, 97, 108, 78, 117, 109, 98, 101, 114 }; // "SerialNumber"
            var s = "";
            for (var i = 0; i < chars.Length; i++)
                s = s + ((char)chars[i]).ToString();
            return s;
        }
        
    }

}