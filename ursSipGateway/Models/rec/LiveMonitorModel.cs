using Project.Enums;
using System.Net;
using System.Net.Sockets;

namespace Project.Models {

    public class LiveMonitorModel {
        private int _bytesPerFrame = 160;
        private int _jitterSize = 8000;
        private int _bufferSize = 16000;
        private int _renewIntervalSec = 60; //如果不在 180 秒內來 renew，則自動停止聽 

        public IPEndPoint RxEndPoint { get; internal set; } = null;
        public IPEndPoint TxEndPoint { get; internal set; } = null;        
        public string Host { get; internal set; } = string.Empty; // 傳送的對方IP
        public int RxPort { get; internal set; }
        public int TxPort { get; internal set; }        
        public bool IsOpened { get; internal set; } = false; // 是否要監聽
        public DateTime LastRenewTime { get; set; } = DateTime.MinValue; // 上次最後來 renew 監聽的時間        
        public string LastErrorMsg { get; internal set; } = string.Empty;

        public PlayRtpControl SendPlayRtp { get; internal set; } = null;
        public PlayRtpControl RecvPlayRtp { get; internal set; } = null;

        public LiveMonitorModel(ENUM_PayloadType pt, int bytesPerFrame, int renewIntervalSec = 60, int jitterSize = 8000) {
            _bytesPerFrame = bytesPerFrame;
            _jitterSize = jitterSize;
            _bufferSize = jitterSize * 2;
            _renewIntervalSec = renewIntervalSec;

            SendPlayRtp = new PlayRtpControl(ENUM_IPDir.Send, _bytesPerFrame, _jitterSize, _bufferSize, pt);
            RecvPlayRtp = new PlayRtpControl(ENUM_IPDir.Recv, _bytesPerFrame, _jitterSize, _bufferSize, pt);
        }
        private IPEndPoint GetIpEndPoint(IPAddress ipAddress, int port, out string err) {
            err = "";
            IPEndPoint endPoint;
            try {
                endPoint = new IPEndPoint(ipAddress, port);
            }
            catch (Exception ex) {
                err = ex.Message;
                endPoint = null;
            }
            return endPoint;
        }

        // 如果要持續監聽，必須在 renewIntervalSec 時間內，一直呼叫
        public bool StartMonitor(LoggerCommandModel cmd, out string msg) {
            msg = "";            
            CheckRenew();
            // 檢查是否已經被監聽
            if (IsOpened) { // 已在監聽
                if (cmd.RtpDest.Host.ToLower() == Host.ToLower()) {
                    LastRenewTime = DateTime.Now;
                    msg = $"監聽 Renew 完成";
                    return true;
                }
                else {
                    msg = $"已經被 {cmd.RtpDest.Host} 監聽中.";
                    return false;
                }
            }            
            
            Host = cmd.RtpDest.Host;
            TxPort = cmd.RtpDest.TxPort;
            RxPort = cmd.RtpDest.RxPort;            

            // 檢查 IPEndPoint
            TxEndPoint = GetIpEndPoint(System.Net.IPAddress.Parse(Host), TxPort, out string txErr);
            if (TxEndPoint == null) {
                msg = $"TxPort({TxPort}@{Host}) bind error: {txErr}";
                return false;
            }
            RxEndPoint = GetIpEndPoint(System.Net.IPAddress.Parse(Host), RxPort, out string rxErr);
            if (RxEndPoint == null) {
                msg = $"RxPort({RxPort}@{Host}) bind error: {rxErr}";
                return false;
            }

            msg = $"開始監聽 ...";
            IsOpened = true;
            LastRenewTime = DateTime.Now;
            return true;
        }

        public void StopMonitor() {
            RxEndPoint = null;
            TxEndPoint = null;
            IsOpened = false;
            LastRenewTime = DateTime.Now;
        }

        // 檢查如果沒有在時間內一直叫 StartMonitor，則會自動設為不監聽
        public bool CheckRenew() {            
            if (!IsOpened)
                return false;
            if ((DateTime.Now - LastRenewTime).TotalSeconds > _renewIntervalSec) {
                StopMonitor();                
            }
            else
                IsOpened = true;
            return IsOpened;
        }

    }

    
}
