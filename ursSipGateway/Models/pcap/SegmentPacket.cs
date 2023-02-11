using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ursSipParser.Models {
    internal class SegmentPacket {
        public ulong PacketIndex { internal set; get; } = 0; // 紀錄被切割的第 1 個封包的號碼
        public byte[] PayloadData { internal set; get; } = null;

        public SegmentPacket() {
        }

        // 設定切割包的第 1 個封包
        public void SetPacket(ulong index, byte[] segmentData) {
            PacketIndex = index;
            PayloadData = new byte[segmentData.Length];
            // 把 segmentData 複製到 PayloadData
            Array.Copy(segmentData, 0, PayloadData, 0, segmentData.Length);
            return;
        }

        // 判斷是否為第 2 個封包
        public bool IsTheSecondSegmentPacket(ulong index) {
            if (PayloadData != null && PayloadData.Length > 0) {
                if (index == PacketIndex + 1) { // 封包一定會連續，判斷是否為第 2 個封包
                    return true;
                }
                else { // 不是下一包，但如果 PayloadData 有值，也要清掉
                    Reset();
                }
            }
            return false;
        }

        // 重設 PacketIndex，釋放 PayloadData 變為 null
        public void Reset() {
            PacketIndex = 0;
            if (PayloadData != null) {
                Array.Clear(PayloadData, 0, PayloadData.Length);
                PayloadData = null;
            }
        }

    }
}
