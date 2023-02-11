using Project.Enums;
using Project.Models;
using Project.Helpers;

namespace Project.Controller {
    public class SipDialogControll {
       
        public List<SipDialogModel> Dialogs = new List<SipDialogModel>();
        private object _lock = new object();
        // constructor
        public SipDialogControll() {            
        }

        // 用 callID + FromTag + ToTag 找 dialog，
        // *** 注意: 但是 FromTag 與 ToTag 顛倒，要視為一樣 ***        
        public SipDialogModel GetDialog(string callID, string fromTag, string toTag) {
            SipDialogModel dialog = null;
            lock (_lock) {
                dialog = Dialogs.Where(x => (x.CallID == callID && x.FromTag == fromTag && x.ToTag == toTag) ||
                                            (x.CallID == callID && x.FromTag == toTag && x.ToTag == fromTag)).FirstOrDefault();
            }
            return dialog;
        }

        // 用 callID + FromTag + ToTag + Status  找 dialog，
        // *** 注意: 但是 FromTag 與 ToTag 顛倒，要視為一樣 ***        
        public SipDialogModel GetDialog(string callID, string fromTag, string toTag, ENUM_SIP_Dialog_Status status) {
            SipDialogModel dialog = null;
            lock (_lock) {
                dialog = Dialogs.Where(x => x.Status == status && ((x.CallID == callID && x.FromTag == fromTag && x.ToTag == toTag) ||
                                                                   (x.CallID == callID && x.FromTag == toTag && x.ToTag == fromTag))).FirstOrDefault();
            }
            return dialog;
        }

        // 用 CallID 找 Dialog
        public SipDialogModel GetDialog(string callID) {
            SipDialogModel dialog = null;
            lock (_lock) {
                dialog = Dialogs.Where(x => x.CallID == callID).FirstOrDefault();
            }
            return dialog;
        }

        // 用 ip + port + status 找 Dialog
        public SipDialogModel GetDialog(string ip, int port, ENUM_SIP_Dialog_Status status) {
            SipDialogModel dialog = null;
            lock (_lock) {
                dialog = Dialogs.Where(x => x.Ip == ip && x.RtpPort == port && x.Status == status).FirstOrDefault();
            }
            return dialog;
        }

        public SipDialogModel CreateSipDialog(string extNo, bool invite, string ip, string mac, PacketInfoEx packetInfo) {
            var sipDialog = new SipDialogModel() {
                ID = "", // 要等到 StartTalking 有值時，才會 assign
                ExtNo = extNo,

                CallID = packetInfo.Sip.CallID,
                FromTag = packetInfo.Sip.FromTag,
                ToTag = packetInfo.Sip.ToTag,
                Invite = invite,
                FromExt = packetInfo.Sip.FromExt,
                ToExt = packetInfo.Sip.ToExt,
                Ip = ip,
                Mac = mac,
                SessionID = packetInfo.Sip.SessionID,
                RtpPort = packetInfo.Sdp.RtpPort,
                Status = ENUM_SIP_Dialog_Status.Waiting, // 此時因為還在等 200 OK 或 ACK 所以是 Waiting
                StartTalkTime = null, // 此時因為還在等 200 OK 或 ACK 所以還未開始通話
            };
            return sipDialog;
        }


        public SipDialogModel CreateAndInsertDialog(string extNo, bool invite, string ip, string mac, PacketInfoEx packetInfo) {
            var dialog = CreateSipDialog(extNo, invite, ip, mac, packetInfo);
            lock (_lock) {
                Dialogs.Add(dialog);
            }            
            return dialog;
        }

        public bool RemoveDialog(SipDialogModel dialog) {
            var ret = false;
            lock (_lock) {
                ret = Dialogs.Remove(dialog);
            }
            return ret;
        }

        public List<SipDialogModel> GetDialogList() {
            List<SipDialogModel> ret;
            lock (_lock) {
                ret = Dialogs.GetRange(0, Dialogs.Count);
            }
            return ret;
        }

        /// <summary>
        /// 1. 設定 dialog 的 Status + StartTalkTime + ToTag
        /// 2. 設定 Dialog.ID
        /// </summary>
        /// <param name="dialog"></param>
        /// <param name="toTag"></param>
        /// <returns></returns>
        public bool SetStartTalking(ref SipDialogModel dialog) {
            var ret = false;
            lock (_lock) {
                ret = Dialogs.Contains(dialog);
                if (ret) {
                    dialog.Status = ENUM_SIP_Dialog_Status.Talking;
                    dialog.StartTalkTime = DateTime.Now;
                    if (!string.IsNullOrEmpty(dialog.ToTag) && dialog.StartTalkTime.HasValue)
                        dialog.ID = $"{dialog.CallID}_{dialog.StartTalkTime.Value.ToString("HHmmss.ffffff")}";
                }                
            }
            return ret;
        }

        /// <summary>
        /// 1. 設定 dialog 的 Status + StartTalkTime + ToTag
        /// 2. 設定 Dialog.ID
        /// </summary>
        /// <param name="dialog"></param>
        /// <param name="toTag"></param>
        /// <returns></returns>
        public bool SetStartTalking(ref SipDialogModel dialog, string toTag) {
            var ret = false;
            lock (_lock) {
                ret = Dialogs.Contains(dialog);
                if (ret) {
                    dialog.Status = ENUM_SIP_Dialog_Status.Talking;
                    dialog.StartTalkTime = DateTime.Now;
                    dialog.ToTag = toTag;
                    if (!string.IsNullOrEmpty(dialog.ToTag) && dialog.StartTalkTime.HasValue)
                        dialog.ID = $"{dialog.CallID}_{dialog.StartTalkTime.Value.ToString("HHmmss.ffffff")}";
                }
            }
            return ret;
        }

        public List<SipDialogModel> GetTalkingNoResponseList(int timeout) {
            List<SipDialogModel> ret = null;
            lock (_lock) {
                var notRespList = Dialogs.Where(x => 
                            (x.HoldStatus != ENUM_SIP_Dialog_Hold.PressToHold  && (DateTime.Now - x.LastPacketTime).TotalSeconds > timeout)                            
                        ).ToList();
                if (notRespList != null && notRespList.Count > 0)
                    ret = notRespList.GetRange(0, notRespList.Count);
            }
            return ret;
        }

        public List<SipDialogModel> GetHoldNoResponseList(int timeout) {
            List<SipDialogModel> ret = null;
            lock (_lock) {
                var notRespList = Dialogs.Where(x =>
                            (x.HoldStartTime.HasValue && x.HoldStatus == ENUM_SIP_Dialog_Hold.PressToHold && (DateTime.Now - x.HoldStartTime.Value).TotalSeconds > timeout)
                        ).ToList();
                if (notRespList != null && notRespList.Count > 0)
                    ret = notRespList.GetRange(0, notRespList.Count);
            }
            return ret;
        }

        // 取得 Dialog 狀態一直都是 init 或 waiting 的清單
        public List<SipDialogModel> GetLongWaitingList(int timeoutMin) {
            List<SipDialogModel> ret = null;
            lock (_lock) {
                var notRespList = Dialogs.Where(x => (x.Status == ENUM_SIP_Dialog_Status.Init || x.Status == ENUM_SIP_Dialog_Status.Waiting) 
                                    && (DateTime.Now - x.InitTime).TotalMinutes > timeoutMin).ToList();
                if (notRespList != null && notRespList.Count > 0)
                    ret = notRespList.GetRange(0, notRespList.Count);
            }
            return ret;
        }

    }
}
