using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project.AppSetting {
    public class AppSettings {
        public string ServerID { get; set; }
        public string AppID { get; set; }
        public string LicenseFile { get; set; }
        public AppSettings_DBConnection DBConnection { get; set; }
        public AppSettings_WorkerOption WorkerOption { get; set; }
        public AppSettings_Logging Logging { get; set; }

        public AppSettings_Recording Recording { get; set; }

        public int CommandPort { get; set; }

        public AppSettings_LiveMonGateway LiveMonGateway { get; set; }

        // 被監控的網卡設定
        public List<string> NetworkInterface { get; set; }

        public AppSettings_WebAPI WebAPI { get; set; }

        public AppSettings_Monitor Monitor { get; set; }

    }

    public class AppSettings_DBConnection {
        public string DBName { get; set; }
        public string SchemaName { get; set; }
        public string MainDBConnStr { get; set; }
        public int DBConnectTimeout { get; set; } = 60;
        public bool SqlTrace { get; set; } = false;

        // Contructure
        public AppSettings_DBConnection() {
            DBConnectTimeout = 60;
        }
    }

    public class AppSettings_WorkerOption {
        public int DelayBefroreExecute { get; set; }
        public bool UpdateModuleStatus { get; set; } = false;
        public int UpdateModuleStatusIntervalMin { get; set; } = 5;
        public int LoopIntervalMSec { get; set; } = 100; // default = 100ms
        public int ProcessIntervalSec { get; set; } = 20; // default = 20s        
    }

    public class AppSettings_Logging {
        public bool SipCommand { set; get; } = true;
        public bool SdpCommand { set; get; } = true;
        public bool RtpHeader { set; get; } = false;
        public bool RtpHeaderHex { set; get; } = false;
        public bool RecRtp { set; get; } = false;
        public bool SSRCInfo { set; get; } = false;
    }

    public class AppSettings_Monitor {        
        public string SipProtocol { get; set; } = "udp";
        public int SipPort { get; set; }
        public bool EnablePromiscuousModel { set; get; } = false;
        public int ReadPacketTimeoutMilliSec { set; get; } = 10;
        public string BasicFilter { get; set; } = "";
        public List<string> FilterIPRange { get; set; }
        public int RtpMinPort { get; set; } = 1024;
        public int RtpMaxPort { get; set; } = 65535;        

        public string MonType { set; get; } = "ip";
        public int AudioRawFreshSize { set; get; } = 8000;

        //Todo: 應該要從 SIP/SDP 中取得(a=rtpmap 清單)，再配合 RTP.PayloadType 來對照，進而取得 BPS (bytes per second)
        public int AudioBytesPerSec { set; get; } = 8000; //每一秒佔用的語音 Byte 數
        public int AudioBytesPerFrame { set; get; } = 160; //音訊每個 Frame 的 Bytes 數量

        public ENUM_SIP_MonitorType MonitorType { set; get; }
        public List<AppSettings_Monitor_Device> Device { get; set; }
    }

    public class AppSettings_Monitor_Device {
        public string Extn { get; set; }
        public string IpAddr { get; set; } 
        public string MacAddr { get; set; }         
    }

    public class AppSettings_LiveMonGateway {        
        public int RenewIntervalSec { set; get; }
        public string Host { get; set; }
        public int Port { get; set; }        
    }

    public class AppSettings_Recording {
        public long LoggerSeq { get; set; } = 1;
        public string LoggerID { get; set; } = "SipLogger";
        public string LoggerName { get; set; } = "SipLogger";
        public string RecTempPath { get; set; } = "";
        public string RecDataPath { get; set; } = "";
        public bool VolumeNormalize { get; set; } = true;
        public bool PadHoldSilence { get; set; } = false;
        public int RtpNoResponseTimeoutSec { get; set; } = 45;
        public int HoldNoResponseTimeoutSec { get; set; } = 3600;
    }

    public class AppSettings_WebAPI {
        public string ApiAccount { get; set; } = "";
        public string ApiPassword { get; set; } = "";
        public string GetToken { get; set; } = "";
        public string UpdateChannelStatus { get; set; } = "";
        public string WriteRecData { get; set; } = "";
        public string WriteSystemLog { get; set; } = "";
    }



}
