using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {
    public class PlayRtpControl {
        private Socket udpSocket = null; // 要傳送的 UDP Socket
        private object bufferLock = new object();

        private byte[] buffer = null;
        private int bufferSize = 0; // buffer 的大小，不能小於 JitterSize
        public int bufferLength = 0; // 目前 buffer 的長度 + 位置

        private int jitterSize = 0;  // 真正要傳送的大小，JitterSize 滿了就要傳送

        private ushort originalSeq = 0; // 用來記錄 Rtp.Header.Sequence 是否重複?
        private int audioBytescPerFrame;
        private int fixedSSRC = 12345; //固定的 ssrc

        private byte[] sendData = null;

        public UInt16 Seq { set; get; } // 真正傳送的 Seq(新的 Rtp.Header 的 Sequence)
        public UInt32 Timestamp { set; get; } // 真正傳送的 Timestamp(新的 Rtp.Header 的 Timestamp)
        public ENUM_IPDir IpDir { internal set; get; }
        public byte[] Jitter { internal set; get; } = null;
        public bool JitterFull { internal set; get; } = false; // jitter 裡面是否已經滿了?            
        public string LastErrorMsg { internal set; get; } = ""; 
        
        public bool Bye { internal set; get; } = false;


        // Constructer
        public PlayRtpControl(ENUM_IPDir ipDir, int bytescPerFrame, int jitterSize, int bufferSize, ENUM_PayloadType pt) {
            this.Seq = 1;
            this.Timestamp = 1;
            this.IpDir = ipDir;
            this.bufferSize = bufferSize;
            this.jitterSize = jitterSize;
            this.audioBytescPerFrame = bytescPerFrame;
            this.buffer = new byte[this.bufferSize];
            this.Jitter = new byte[this.jitterSize];

            // 準備 sendData 的 byte[] + 固定的 header 
            this.sendData = new byte[12 + audioBytescPerFrame];
            var hdr = PrepareFixedRTPHeader(pt, fixedSSRC);
            hdr.CopyTo(sendData, 0);

            // 準備 Socket
            try {
                this.udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            }
            catch (Exception ex) {
                this.LastErrorMsg = $"udp socket init error: {ex.Message}";
                this.udpSocket = null;
            }
        }

        public bool AddBuffer(PacketInfoEx packetInfo) {
            var ret = false;
            lock (bufferLock) {                
                if (packetInfo.Rtp.Header.SeqNum != originalSeq) { // 過濾重複的封包                    
                    originalSeq = packetInfo.Rtp.Header.SeqNum;
                    Array.Copy(packetInfo.Rtp.AudioBytes, 0, buffer, bufferLength, packetInfo.Rtp.AudioBytes.Length);
                    bufferLength = bufferLength + packetInfo.Rtp.AudioBytes.Length;
                    JitterFull = bufferLength >= jitterSize;
                    ret = true;
                }                
            }
            return ret;
        }

        // 
        public void Reset() {
            lock (bufferLock) {
                Bye = false;
                bufferLength = 0;
            }
        }

        public async Task StopMonitoring(IPEndPoint ipEndPoint) {            
            byte[] data = null;
            lock (bufferLock) {
                Bye = true;
                data = new byte[bufferLength];
                Array.Copy(buffer, 0, data, 0, bufferLength);
                bufferLength = 0;
            }
            await PlayRTPNow(data, ipEndPoint);
        }


        // return: 
        //      true: jitter 已滿，要撥放
        //      false: jitter 未滿
        public bool GetJitter() {
            var ret = false;
            lock (bufferLock) {
                if (bufferLength >= jitterSize) {
                    Array.Copy(buffer, 0, Jitter, 0, jitterSize);
                    bufferLength = bufferLength - jitterSize;
                    ret = true;
                }
                JitterFull = bufferLength >= jitterSize; // 扔然要計算，也許還是 full                    
            }
            return ret;
        }

        // 當停止監聽時(StopMonitoring)，Bye = true, 此時要馬上送出 data 撥放，不放在 Buffer 跟 Jitter 中
        public async Task PlayRTPNow(byte[] data, IPEndPoint ipEndPoint) {
            if (udpSocket == null || ipEndPoint == null) {
                await Task.Delay(TimeSpan.FromMilliseconds(1));
                return;
            }
            try {
                var dwDataLen = data.Length;
                var sendCount = dwDataLen / audioBytescPerFrame;
                if (dwDataLen % audioBytescPerFrame != 0) // 是否有餘數，有餘數要多送一次
                    sendCount++;

                for (var i = 0; i < sendCount; i++) {
                    var startPos = i * audioBytescPerFrame;
                    var copyLen = (startPos + 160) <= dwDataLen ? audioBytescPerFrame : dwDataLen - startPos;

                    SetRtpHeader();
                    Array.Copy(data, i * audioBytescPerFrame, sendData, 12, copyLen); // copy payload                    
                    udpSocket.SendToAsync(sendData, SocketFlags.None, ipEndPoint);
                }
            }
            catch (Exception ex) {
            }
        }


        // 針對在 Jitter 裡面的資料傳送 RTP
        public async Task PlayRTP(IPEndPoint ipEndPoint) {
            if (udpSocket == null || ipEndPoint == null) {
                await Task.Delay(TimeSpan.FromMilliseconds(1));
                return;
            }            
            try {
                var dwDataLen = Jitter.Length;
                var sendCount = dwDataLen / audioBytescPerFrame;
                if (dwDataLen % audioBytescPerFrame != 0) // 是否有餘數，有餘數要多送一次
                    sendCount++;

                for (var i = 0; i < sendCount; i++) {
                    var startPos = i * audioBytescPerFrame;
                    var copyLen = (startPos + 160) <= dwDataLen ? audioBytescPerFrame : dwDataLen - startPos;

                    SetRtpHeader();
                    Array.Copy(Jitter, i * audioBytescPerFrame, sendData, 12, copyLen); // copy payload
                    udpSocket.SendToAsync(sendData,  SocketFlags.None, ipEndPoint);
                }
            }
            catch (Exception ex) {
            }
        }

        // 準備固定的 RTP Header
        private byte[] PrepareFixedRTPHeader(ENUM_PayloadType pt, int ssrc) {
            var rtpHdr = new byte[12];
            // Byte 0 = 0x80
            rtpHdr[0] = 0x80; // ver=2(10), p=0, x=0(00), cc=0000, 2進位(10000000 = 0x80)

            // Byte 1 = 0x00(使用 G.711, PCMU)           
            rtpHdr[1] = (byte)pt;

            // RTP Sequence
            rtpHdr[2] = (byte)(1 / 256);
            rtpHdr[3] = (byte)(1 % 256);

            //Big Endian
            var byteTimeStamp = BitConverter.GetBytes(1);
            rtpHdr[4] = byteTimeStamp[3];
            rtpHdr[5] = byteTimeStamp[2];
            rtpHdr[6] = byteTimeStamp[1];
            rtpHdr[7] = byteTimeStamp[0];

            // SSRC
            var byteSsrc = BitConverter.GetBytes(ssrc);
            rtpHdr[8] = byteSsrc[3];
            rtpHdr[9] = byteSsrc[2];
            rtpHdr[10] = byteSsrc[1];
            rtpHdr[11] = byteSsrc[0];
            return rtpHdr;
        }

        // 傳送前改 RTP Header
        private void SetRtpHeader() {
            // RTP Sequence 增加
            if (Seq >= UInt16.MaxValue - 1)
                Seq = 1;
            else
                Seq++;
            // 填入 RTP Sequence
            sendData[2] = (byte)(Seq / 256);
            sendData[3] = (byte)(Seq % 256);

            // TimeStamp 增加
            if (Timestamp >= (uint.MaxValue - (audioBytescPerFrame * 2)))
                Timestamp = 1;
            else
                Timestamp = (uint)(Timestamp + audioBytescPerFrame);
            //Big Endian
            var timeStamp = BitConverter.GetBytes(Timestamp);
            // 填入 timestamp
            sendData[4] = timeStamp[3];
            sendData[5] = timeStamp[2];
            sendData[6] = timeStamp[1];
            sendData[7] = timeStamp[0];
        }
    }


}
