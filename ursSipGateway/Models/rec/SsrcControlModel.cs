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

    public class SsrcControlModel {        
        public string SSRC { get; set; }
        public ENUM_IPDir Dir { get; set; } // 發送端 or 接收端
        public uint StartRtpTimestamp { set; get; }
        public uint EndRtpTimestamp { set; get; }
        public ushort StartRtpSeq { set; get; }
        public ushort EndRtpSeq { set; get; }
        public DateTime ProcessStartTime { set; get; } // 這個 SSRC 開始處理的時間
        public DateTime ProcessEndTime { set; get; } // 這個 SSRC 最後一個封包的時間
        public DateTime FirstCaptureTime { set; get; } // 這個 SSRC 第 1 封包的時間
        public DateTime LastCaptureTime { set; get; } // 這個 SSRC 最後封包的時間

        public ulong TotalPacket { set; get; } = 0;
        public decimal TotalTime { set; get; } = 0; // 單位: 秒
        public double UseTime { set; get; } = 0;

        // 語音每秒的取樣 Bytes 數，例如: G.711 = 8000 ...等。
        public int BytesPerSecond { internal set; get; } = 8000;

        // 每一個 RTP 的 Byte 數，例如: 160。 計算方式: 計算每一 RTP 封包 Header 的 Timestamp 差。
        public int FrameBytes { internal set; get; } = 160;
        
        // 每一個 RTP 封包的占用時間，單位: ms
        public int FrameMilliSec { internal set; get; } = 20;

        public bool BeHeld { internal set; get; } = false;

        public SsrcControlModel(string ssrc, ENUM_IPDir dir, int bytesPerSecond, int bytesPerFrame, bool beHeld = false) {
            SSRC = ssrc;
            Dir = dir;
            BytesPerSecond = bytesPerSecond;
            FrameBytes = bytesPerFrame;
            FrameMilliSec = 1000 / (BytesPerSecond / FrameBytes);
            BeHeld = beHeld; // 標註此 SSRC 是 Music On Hold 的音樂
        }

        // 這段作廢：計算有時會有問題，直接從 appsettings.json 設定
        //private void GetFrameBytes(uint rtpTimestamp) {            
        //    if (TotalPacket >= 5 && TotalPacket <= 10) {
        //        _timestampList.Add(rtpTimestamp); // 取第 5 ~ 10 封包，共 6 包，計算相鄰兩封包之間的差
        //        if (_timestampList.Count != 6)
        //            return;

        //        _timestampList.Sort();
        //        // 從後面往回計算，後面減前面，只要抓到大於 0，應該就是了
        //        for (var i = _timestampList.Count - 1; i >= 1; i--) {
        //            FrameBytes = Math.Abs((int)(_timestampList[i] - _timestampList[i - 1]));
        //            if (FrameBytes != 0) {
        //                FrameMilliSec = 1000 / (BytesPerSecond / FrameBytes);
        //                break;
        //            }
        //        }                
        //    }            
        //}


        // 如果每一段的 SSRC 的第一個封包沒有 Mark，則這裡的 StartRtpTimestamp、StartRtpSeq、ProcessStartTime、FirstCaptureTime 都會有問題。
        // 已解決: 不判斷 mark, 改用第 1 個封包來判斷

        public ulong AddPacket(RtpModel rtp) { 
            //if (rtp.Header.Marker == 1) { //  mark = 1, 代表是一開始的封包
            if (TotalPacket == 0) {  // 怕萬一沒有 mark
                StartRtpTimestamp = rtp.Header.Timestamp;                
                StartRtpSeq = rtp.Header.SeqNum;
                ProcessStartTime = DateTime.Now;
                FirstCaptureTime = rtp.CaptureTime;
            }
            EndRtpTimestamp = rtp.Header.Timestamp; // 當成最後一個
            EndRtpSeq = rtp.Header.SeqNum;  // 當成最後一個
            ProcessEndTime = DateTime.Now;
            LastCaptureTime = rtp.CaptureTime;
            TotalPacket++;
            TotalTime = (decimal)(TotalPacket * (FrameMilliSec / 1000.00)); // 單位: 秒            
            UseTime = (ProcessEndTime - ProcessStartTime).TotalMilliseconds;
            return TotalPacket;
        }

        
    }

}
