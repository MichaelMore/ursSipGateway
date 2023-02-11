using Newtonsoft.Json;
using Project.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Project.Models {
    

    // 處理 Send/Recv 封包寫檔的功能
    public class AudioRawModel {
        public string FileName { get; set; } = string.Empty;
        public ulong TotalSize { get; set; } = 0; // bytes
        public ulong TotalPkt { get; set; } = 0;        
        
        private string _rawFileName = "";                
        private int _maxBytesToWrite = 8000;        
        
        private Dictionary<ushort, byte[]> _audioBuffer = new Dictionary<ushort, byte[]>(); // key = rtp sequence
        private int _audioSize = 0;


        // 最大寫入檔案的封包大小，例如語音每秒佔  8000 bytes(8KB)，為了要 1 秒寫 1 次檔案，
        // maxPacketSize = 8000
        public AudioRawModel(string rawFileName, int maxBytesToWrite) {
            _rawFileName = rawFileName;
            _maxBytesToWrite = maxBytesToWrite;            
        }

        public bool WritePacket(ushort rtpSeq, byte[] byteData, out string errMsg) {
            errMsg = "";
            // 之前音檔聽起來不順，就是重複序號 + 先後秩序混亂的問題，序號已存在，不允許再寫入
            if (_audioBuffer.ContainsKey(rtpSeq)) {
                return true; // 算正常
            }

            _audioBuffer.Add(rtpSeq, byteData);
            _audioSize = _audioSize + byteData.Length;

            if (_audioSize >= _maxBytesToWrite) {
                WriteStream(out errMsg);             
            }
            TotalSize = TotalSize + (ulong)byteData.Length;
            TotalPkt++;
            return errMsg == "";
        }

        public bool Close(out string errMsg) {
            errMsg = "";
            var ret = true;
            if (_audioSize > 0) {
                ret = WriteStream(out errMsg);                
            }
            return ret;
        }

        private bool WriteStream(out string errMsg) {
            errMsg = "";
            // 寫入檔案前˙先針對 RTP Seq 進行排序，之前音檔聽起來不順，就是重複序號 + 先後秩序混亂的問題
            var dictAudio = _audioBuffer.OrderBy(x=>x.Key); // rtp 序號排序
            try {
                using (var fs = new FileStream(_rawFileName, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)) {
                    fs.Seek(0, SeekOrigin.End);
                    if (!fs.CanWrite) {
                        errMsg = $"FileStream cannot write(fname={_rawFileName})";
                        return false;
                    }
                    foreach(var item in dictAudio) {
                        fs.Write(item.Value, 0, item.Value.Length);
                    }
                    //                
                    _audioBuffer.Clear();
                    _audioSize = 0;
                    return true;
                }
            }
            catch (Exception ex) {
                _audioBuffer.Clear();
                errMsg = $"WriteStreamEx exception: {ex.Message}, (fname={Path.GetFileName(_rawFileName)})";
                return false;
            }
        }

    }
}
