using PacketDotNet;
using Project.Enums;
using Project.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Project.Models {

    public class RtpHeaderModel {
        public int Ver { set; get; } = 0;       // bit x  2: 固定為2
        public int Padding { set; get; }        // bit x  1: 設定是否要在資料的尾端加上padding。(某些加密演算法需要固定長度的padding)
        public int Extension { set; get; }      // bit x  1: 設定是否要增加擴充的extension header。此extension應加在CSRC list之後，若此封包不存在CSRC list，則加在SSRC 之後。下面為extension header，其中length記錄了header extension共佔據了幾個four-octet。
        public int CRSC { set; get; }           // bit x  4: CRSC的數量
        public int Marker { set; get; }         // bit x  1: 針對不同的 profile，Marker會有不同意思，一般用法是用來標註 frame boundaries。
        public int PayloadType { set; get; }    // bit x  7: 定義了128種不同的編解碼方式。
        public ushort SeqNum { set; get; }      // bit x 16: 16位元的序號，用來偵測封包遺失，或進行封包重組。
        public uint Timestamp { set; get; }    // bit x 32: 採樣時的時間，用來作同步和jitter計算。可以使用系統時間或是採樣週期(sampling clock)
        public uint SSRC { set; get; }         // bit x 32: 同步來源。在一個RTP會話中(Dialog)，不應該存在相同的SSRC，因此此值需要亂數產生，並且需要偵測是否重複的SSRC產生，並解決此重複的問題。        
    }


    public class RtpModel {
        public DateTime CaptureTime { set; get; }
        public RtpHeaderModel Header { set; get; }      // RTP Header
        public byte[] HeaderBytes { set; get; }                // 語音資料
        public byte[] AudioBytes { set; get; }                // 語音資料

        // payload = 12 bytes header + rawData
        public RtpModel(byte[] payload, DateTime captureTime) {
            if (payload == null || payload.Length <= 12)
                return;
            CaptureTime = captureTime;
            Header = new RtpHeaderModel() {
                // 第 0 byte
                Ver = payload[0] >> 6,          // 取第 0,1 個 bit => 位元右移 6
                Padding = payload[0] & 32,      // 取第 2 個 bit => and 00100000
                Extension = payload[0] & 16,    // 取第 3 個 bit => and 00010000
                CRSC = payload[0] & 15,         // 取第  4,5,6,7 個 bit => and 00001111
                // 第 1 byte
                Marker = payload[1] >> 7,       // 取第 1 個 bit => 位元右移 7
                PayloadType = payload[1] & 127, // 取第 1~7 個 bit => and 01111111
                // get 2, 3 byte                
                SeqNum = GetValue_2Bytes(payload, 2),
                // get 4, 5, 6, 7 bytes
                Timestamp = GetValue_4Bytes(payload, 4),
                // get 8, 9, 10, 11 bytes                
                SSRC = GetValue_4Bytes(payload, 8),           
            };

            // copy header byte
            HeaderBytes = new byte[12];
            Array.Copy(payload, 0, HeaderBytes, 0, 12);

            // copy audio raw data
            AudioBytes = new byte[payload.Length - 12];
            for (int i = 0; i < payload.Length-12; i++)
                AudioBytes[i] = 0xff;
            Array.Copy(payload, 12, AudioBytes, 0, AudioBytes.Length);
        }
        public string GetHeaderLog() {
            var mark = Header.Marker == 1 ? "(Mark)" : "      ";
            return $"PT={Header.PayloadType}{mark}, SSRC=0x{Convert.ToString(Header.SSRC, 16).ToUpper()}, Seq={Header.SeqNum}, Time={Header.Timestamp}, CapTime={CaptureTime.ToTimeStr(":", 6)}, payload={AudioBytes.Length}";
        }

        public string GetHeaderHex() {
            var s = "";            
            foreach (var b in HeaderBytes) {
                s = s + $"{b.ToString("x2")} ";                
            }
            return s.Trim();
        }


        private ushort GetValue_2Bytes(byte[] srcBytes, int startIndex) {            
            int byteLen = 2; // ushort is 2 bytes
            var temp = new byte[byteLen];
            try {
                Array.Copy(srcBytes, startIndex, temp, 0, byteLen);
                if (BitConverter.IsLittleEndian) {
                    var newTemp = ReverseBytes(temp);
                    return BitConverter.ToUInt16(newTemp, 0);
                }
                else {
                    return BitConverter.ToUInt16(temp, 0);
                }
            }
            catch(Exception ex) {
                return 0;
            }           
        }

        private uint GetValue_4Bytes(byte[] srcBytes, int startIndex) {            
            int byteLen = 4; // uint is 4 bytes
            var temp = new byte[byteLen];
            try {
                Array.Copy(srcBytes, startIndex, temp, 0, byteLen);
                if (BitConverter.IsLittleEndian) {
                    var newTemp = ReverseBytes(temp);
                    return BitConverter.ToUInt32(newTemp, 0);
                }
                else {
                    return BitConverter.ToUInt32(temp, 0);
                }
            }
            catch(Exception ex) {
                return 0;
            }            
        }

        // 因為 RTP 的記憶體是 BigEndian，所以必須判斷，若是 LittleEndian，就必須將 Bytes Array 前後倒轉
        private byte[] ReverseBytes(byte[] srcBytes) {            
            var retBytes = new byte[srcBytes.Length];
            var pos = srcBytes.Length - 1;
            for (var i=0; i< srcBytes.Length; i++) {
                retBytes[pos] = srcBytes[i];
                pos--;
            }
            return retBytes;

        }



        
    }
}
