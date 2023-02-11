using Newtonsoft.Json;
using NLog;
using NLog.Fluent;
using Project.ProjectCtrl;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;

namespace Project.Lib {
    internal class LicRegisterExModel {
        public long LicSeq { set; get; }
        public int LicVer { set; get; }
        public string ProductID { set; get; }
        public string DealerID { set; get; }// 批發商
        public string DealerName { set; get; }
        public string CustID { set; get; }
        public string CustName { set; get; }
        public string HwCpuID { set; get; }
        public string HwBaseBoardID { set; get; }
        public string HwDiskVolumeSN { set; get; } // 專指 Disk C 的序號
        public string HwBiosName { set; get; }
        public string HwBiosVersion { set; get; }
        public string AuthCode { set; get; }
        public DateTime? DemoExpired { set; get; }
        //
        public int UraPort { set; get; }
        public int UrdPort { set; get; }
        public int UrtPort { set; get; }
        public int UripPort { set; get; }
        public int SynipPort { set; get; }
        //        
        public string SystemFunction { set; get; }  // 系統允許那些功能        
        public string WebFunction { set; get; }     // 網頁允許那些功能        
        public string RptFunction { set; get; }     // 報表允許那些功能
        public string AdvRptFunction { set; get; }  // 進階報表功能
        public string AdvFuncExpired { set; get; }  // 進階功能截止日期
                                                    //
        public string Rev1 { set; get; }
        public string Rev2 { set; get; }
        public string Rev3 { set; get; }
        //
        public DateTime? InstallDateTime { set; get; }
        public string InstallHostInfo { set; get; }
        public string InstallSWName { set; get; }
        public string InstallSWVersion { set; get; }
        public int AuthFlag { set; get; }
    }

    internal class LicRegisterEx2Model {
        public long LicSeq { set; get; }
        public int LicVer { set; get; }
        public string ProductID { set; get; }
        public string DealerID { set; get; }// 批發商
        public string DealerName { set; get; }
        public string CustID { set; get; }
        public string CustName { set; get; }
        public string HwCpuID { set; get; }
        public string HwBaseBoardID { set; get; }
        public string HwDiskVolumeSN { set; get; } // 專指 Disk C 的序號
        public string HwBiosName { set; get; }
        public string HwBiosVersion { set; get; }
        public string AuthCode { set; get; }
        public DateTime? DemoExpired { set; get; }

        // 錄音系統使用
        public int UraPort { set; get; }
        public int UrdPort { set; get; }
        public int UrtPort { set; get; }
        public int UripPort { set; get; }
        public int SynipPort { set; get; }

        // iQMS 或 Call Center 使用, 20190520 Added
        public int AgentCount { set; get; } // 客服人員數量
        public int AuditAgentCount { set; get; } // 督導席人員數量
        public int ScoreAgentCount { set; get; } // 評分人員數量
        public int FaxCount { set; get; } // 傳真數量數量
        public int MonitorCount { set; get; } // 監聽數量

        public string SystemFunction { set; get; }  // 系統允許那些功能        
        public string WebFunction { set; get; }     // 網頁允許那些功能        
        public string RptFunction { set; get; }     // 報表允許那些功能
        public string AdvRptFunction { set; get; }  // 進階報表功能
        public string AdvFuncExpired { set; get; }  // 進階功能截止日期
                                                    //
        public string Rev1 { set; get; }
        public string Rev2 { set; get; }
        public string Rev3 { set; get; }
        //
        public DateTime? InstallDateTime { set; get; }
        public string InstallHostInfo { set; get; }
        public string InstallSWName { set; get; }
        public string InstallSWVersion { set; get; }
        public int AuthFlag { set; get; }
    }

    internal static class lib_license {

        internal static string getPkey() {
            //private static string key = "0955502123" + "0955683903" + "9011663903" + "MW"; // 32 個英文或數字        
            int a = 950;
            int b = 500;
            int c = 100;
            string x = "0" + (a + 5).ToString() + (b + 2).ToString() + (c + 23).ToString(); // 0955502123            
            string y = "0" + (a + 5).ToString() + (b + 183).ToString() + (c + 803).ToString(); // 0955683903
            string z = "0" + (a - 39).ToString() + (b + 163).ToString() + (c + 803).ToString(); // 9011663903
            return x + y + z + "MW";
        }

        // 說明：getIv() 只是在預防DLL被反組譯時增加破解難度。這個 iv 一定要跟 LicenseManager 專案中: MKTConst.cs 的 LicenseIv 一致
        // internal const string LicenseIv = "0492721473" + "mktwen"; // 16 個英文或數字
        internal static string getIv() {
            //private static string iv = "0492721473" + "mktwen"; // 16 個英文或數字
            int a = 50;
            int b = 2700;
            int c = 470;
            return "0" + (a - 1).ToString() + (b + 21).ToString() + (c + 3).ToString() + "mktwen";
        }

        //private Logger LOG = LogManager.GetLogger("License");
        internal static int LastErrorCode { get; private set; } = 0;

        static lib_license() { 
        }

        internal static object DecodeLicenseFile(string authFileName, out int licVer, out string err) {
            licVer = 0;
            LicRegisterExModel model = null;
            LicRegisterEx2Model model2 = null;
            var encode = "";
            LastErrorCode = 0;
            err = "";
            
            // 檢查 file exists
            if (!System.IO.File.Exists(authFileName)) {
                LastErrorCode = -1; // read lic file error
                err = $"lic file not found({authFileName})";
                return null;                
            }
            
            // 讀取 file            
            try {
                encode = System.IO.File.ReadAllText(authFileName, Encoding.UTF8);
                if (encode[0] == '"') {
                    encode = encode.Substring(1, encode.Length - 2);
                }
            }
            catch (Exception ex) {
                LastErrorCode = -2; // read lic file error
                err = $"read lic file error({ex.Message})";
                return null;                
            }            
            
            var decodeJson = lib_encode.DecryptAES256(encode, getPkey(), getIv(), out err);
            if (!string.IsNullOrEmpty(decodeJson)) {
                try {
                    if (decodeJson.Contains("\"LicVer\":1")) {
                        licVer = 1;
                        model = JsonConvert.DeserializeObject<LicRegisterExModel>(decodeJson);
                        return model;
                    }
                    else if (decodeJson.Contains("\"LicVer\":2")) {
                        licVer = 2;
                        model2 = JsonConvert.DeserializeObject<LicRegisterEx2Model>(decodeJson);
                        return model2;
                    }
                    else {
                        LastErrorCode = -5; 
                        err = $"decoded result without lic ver";
                        return null;
                    }                    
                }
                catch (Exception ex) {                    
                    LastErrorCode = -4; // json convert error
                    err = $"decoded result convert JSON error: {ex.Message}";
                    return null;                    
                }
            }
            else {                
                LastErrorCode = -3; // decode error
                err = "decoded result is null";
                return null;                                    
            }            
        }

        private static string EncodeDeviceInfo(out string err) {
            err = "";
            DeviceInfoModel devInfo = new DeviceInfoModel(lib_hwInfo.GetX1(), lib_hwInfo.GetX2(), lib_hwInfo.GetX3(), lib_hwInfo.GetX4(), lib_hwInfo.GetX5());            
            if (devInfo.CpuID == "" || devInfo.BaseBoardID == "") {
                err = "failed to get device info.";
                return ""; // error
            }
            else {
                string jsonStr = JsonConvert.SerializeObject(devInfo);
                return lib_encode.EncryptAES256(jsonStr, lib_license.getPkey(), lib_license.getIv(), out err);
            }
        }

        internal static string GetLicenceKey(out string err) {
            err = "";
            var ret = "";
            var encodedDevInfo = EncodeDeviceInfo(out err);
            if (encodedDevInfo != null) {
                var model = new {
                    SWName = "ursSipParser",
                    SWVersion = GlobalVar.CurrentVersion,
                    FunctionName = "GetLicenseEx2", // 表示要呼叫 LicManager 的 GetLicenseEx2(新版)，另外一個是GetLicenseEx(舊版)
                    DeviceInfo = encodedDevInfo
                };
                ret = JsonConvert.SerializeObject(model);            
            }
            return ret;
        }


    }
}
