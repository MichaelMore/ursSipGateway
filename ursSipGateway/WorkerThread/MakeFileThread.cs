using NLog;
using SharpPcap;
using PacketDotNet;
using WorkerThread;
using NLog.Fluent;
using Project.AppSetting;
using Project.Models;
using static System.Net.WebRequestMethods;
using Project.ProjectCtrl;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using File = System.IO.File;
using System.Diagnostics;
using Project.Lib;
using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Newtonsoft.Json;
using System.Reflection;
using static System.Collections.Specialized.BitVector32;
using Project.Database;
using Project.Enums;

// =========================================================
// Audio Raw Data 轉 Wav:
// ffmpeg -f mulaw -ar 8000 -i spp_test_01.snd spp_A03.wav
// =========================================================

namespace ThreadWorker
{
    public class MakeFileThread: IWorker
    {
        // protect, private
        protected volatile bool _shouldStop;
        protected volatile bool _shouldPause;

        private Logger _nLog;
        //private Logger _errorLog;
        private string _tag;
        private static object _queueLock = new object();        
        private Thread _myThread;        

        // public        
        public AppSettings_Monitor_Device MonDevice { get; private set; }
        public WorkerState State { get; private set; }

        public Queue<RecFileModel> RecFileQueue = new Queue<RecFileModel>(100000);

        // constructor
        public MakeFileThread(AppSettings_Monitor_Device monDev) {
            MonDevice = new AppSettings_Monitor_Device() {                
                IpAddr = monDev.IpAddr,
                MacAddr = monDev.MacAddr,
                Extn = monDev.Extn,
            };

            _tag = $"MakeFile_{MonDevice.Extn}";
            _nLog = LogManager.GetLogger(_tag);
            //_errorLog = LogManager.GetLogger($"MakeFileError_{MonDevice.Extn}");
        }        

        public void StartThread() {
            _myThread = new Thread(this.DoWork) {
                IsBackground = true,
                Name = _tag
            };
            State = WorkerState.Starting;
            _myThread.Start();
        }

        public void StopThread() {
            RequestStop(); // stopping ...
            _nLog.Info($"{_tag} is waiting to stop(join) ...");
            _myThread.Join();
            State = WorkerState.Stopped; // stopped !!!
        }

        public void AddRecFile(RecFileModel recFile) {
            lock (_queueLock) {
                RecFileQueue.Enqueue(recFile);
            }
        }


        public virtual void DoWork(object anObject) {            
            _nLog.Info("");
            _nLog.Info($"********** MakeFile {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr} is now starting ... **********");
            while(!_shouldStop) {
                State = WorkerState.Running;                
                Thread.Sleep(5);
                RecFileModel recFile = null;
                lock (_queueLock) { // <== 一定要 lock
                    if (RecFileQueue.Count > 0) {
                        recFile = RecFileQueue.Dequeue();
                    }
                }
                if (recFile != null) {                    
                    MakeRecFile(recFile);
                }
            }// end while
            _nLog.Info($"========== MakeFile {MonDevice.Extn}@{MonDevice.IpAddr}/{MonDevice.MacAddr}] terminated. ==========");
            State = WorkerState.Stopped;
        }

        private void WriteRecordingErrorLog(string msg) {
            _nLog.Error(msg);
            GlobalVar.SendAPI_WriteSystemLog(ENUM_LogType.Error, msg);
        }

        private void MakeRecFile(RecFileModel recFile) {            
            var err = "";
            var baseTab = "\t";
            _nLog.Info("");
            _nLog.Info("");
            _nLog.Info($"### 開始產生錄音檔... ext={recFile.ExtNo}, 錄音時間={recFile.RecStartTime.ToTimeStr(":", 3)}, 通話時間={recFile.Duration}, CallerID={recFile.CallerID}, CalledID={recFile.CalledID} call-ID={recFile.CallID}");
            _nLog.Info($"{JsonConvert.SerializeObject(recFile)}");

            #region 檢查 recFile、SendSSRCList、RecvSSRCList
            if (recFile == null) {
                err = $"錄音檔產生失敗: RecFileModel = null";
                _nLog.Error($"{baseTab} {err}");                
                WriteRecordingErrorLog($"分機({MonDevice.Extn}): {err}");
                return;
            }
            if (recFile.SendSSRCList == null || recFile.RecvSSRCList == null) {
                err = $"錄音檔產生失敗: SendSSRCList 或 RecvSSRCList = null";
                _nLog.Error($"{baseTab} {err}");                
                WriteRecordingErrorLog($"分機({MonDevice.Extn}): {err}, callID={recFile.CallID}");
                return;
            }
            if (recFile.SendSSRCList.Count == 0 || recFile.RecvSSRCList.Count == 0) {
                err = $"錄音檔產生失敗: SendSSRCList 或 RecvSSRCList count = 0";
                _nLog.Error($"{baseTab} {err}");
                WriteRecordingErrorLog($"分機({MonDevice.Extn}): {err}, callID={recFile.CallID}");
                return;
            }
            #endregion
            
            // 產生 2 個 wav 檔(send + recv)
            var convertRaw = ConvertRawToWavFile(recFile, 100, out string sendWavFileName, out string recvWavFileName);
            if (!convertRaw) {
                err = $"分機({MonDevice.Extn}): 錄音檔產生失敗, ConvertRawToWavFile failed, callID={recFile.CallID}";
                _nLog.Error($"{baseTab} {err}");                
                WriteRecordingErrorLog($"分機({MonDevice.Extn}): {err}, callID={recFile.CallID}");
                //TODO: Send error log by API
                return;
            }

            if (convertRaw) {
                // 插入被動 Hold 的 MOH，只針對 sendFile，因為被動Hold時，聽到的 MOH 會在 recvFile 中，所以不需要 pad
                if (!PadBeheldMoh(recFile, sendWavFileName, out string padSendFile, out err)) {
                    WriteRecordingErrorLog($"MakeRecFile 錯誤(分機={recFile.ExtNo}, callID={recFile.CallID}), {err}");                    
                }
                
                // 音量放大
                if (!VolumeNormalize(padSendFile, out string volSendFile, out err)) {
                    WriteRecordingErrorLog($"MakeRecFile 錯誤(分機={recFile.ExtNo}, callID={recFile.CallID}), {err}");                    
                }
                if (!VolumeNormalize(recvWavFileName, out string volRecvFile, out err)) {
                    WriteRecordingErrorLog($"MakeRecFile 錯誤(分機={recFile.ExtNo}, callID={recFile.CallID}), {err}");                    
                }

                // send + recv 混音
                if (MergeWavFile(recFile, volSendFile, volRecvFile, out string mergWavFileName, out err)) {
                    if (CreateRecordingFile(recFile, mergWavFileName, out string finalRecFile, out err)) {
                        _nLog.Info($"==> 錄音檔產生完成: {finalRecFile}");
                        GlobalVar.SendAPI_WriteRecData(recFile, finalRecFile);
                    }
                    else {
                        _nLog.Info($"==> 錄音檔產生失敗: {err}");
                        WriteRecordingErrorLog($"MakeRecFile 錯誤(分機={recFile.ExtNo}, callID={recFile.CallID}), {err}");                        
                        //TODO: Send error log by API
                    }
                }
                else {
                    WriteRecordingErrorLog($"MakeRecFile 錯誤(分機={recFile.ExtNo}, callID={recFile.CallID}), {err}");
                    //TODO: Send error log by API
                }
            }            
        }


        // 處理被按 Hold 時，send 封包會少了 MOH 的 SSRC，所以要到 recv 的 SSRC 中，把那些 MOH 的 SSRC 補到 send 這邊
        private bool PadBeheldMoh(RecFileModel recFile, string wavSendFileName, out string padSendFile, out string err) {
            // default 設為原來的音檔，萬一處理錯誤，使用原來的音檔
            err = "";
            var baseTab = "\t";
            padSendFile = wavSendFileName;
            _nLog.Info($"{baseTab} 開始處理 Send.MOH ...");
            
            // 沒有 hold，音檔不須後製
            if (recFile.HoldList == null || recFile.HoldList.Count == 0) {
                _nLog.Info($"{baseTab}\t 無 Hold 事件，程序略過");                
                return true;
            }

            if (!recFile.HoldList.Any(x => x.Dir == Project.Enums.ENUM_IPDir.Recv)) {
                _nLog.Info($"{baseTab}\t 無任何 BeHeld 事件，程序略過");
                return true;
            }

            if (recFile.SendSSRCList.Count >= recFile.RecvSSRCList.Count) {                
                _nLog.Info($"{baseTab}\t PadBeheldMoh: {Path.GetFileName(wavSendFileName)}, SendSSRC({recFile.SendSSRCList.Count}) >= RecvSSRC({recFile.RecvSSRCList.Count}), 程序略過");
                return false;
            }
            // 

            _nLog.Info($"{baseTab}\t 插入 MOH 音檔 ...");
            _nLog.Info($"{baseTab}\t 當前 HoldList => \r\n {JsonConvert.SerializeObject(recFile.HoldList, Formatting.Indented)}");
            foreach (var hold in recFile.HoldList.Select((value, idx) => new { value, idx })) {                
                var action = hold.value.Dir == Project.Enums.ENUM_IPDir.Send ? "主動保留" : "被保留";                
                if (hold.value.Dir != Project.Enums.ENUM_IPDir.Recv) {
                    _nLog.Info($"{baseTab}\t\t {action}=>因為不是 recv, 略過-第 {hold.idx:00} 個 hold: {hold.value.GetHoldDurationSec()}, dur = {hold.value.SetHoldTime()}s");
                    continue;
                }

                _nLog.Info($"{baseTab}\t\t {action}=>處理-第 {hold.idx:00} 個 hold: {hold.value.GetHoldDurationSec()}, dur = {hold.value.SetHoldTime()}s");
                var holdStartTime = hold.value.StartPacketTime.Value;
                // 在 RecvSSRCList 中，利用時間差正負1000ms，尋找 hold 的 SSRC
                // 但是，為了避免重複尋找(插入)，所以要先檢查 ...
                //TODO: 若 ssrc 已經存在，就不需要再插入
                foreach (var ssrc in recFile.RecvSSRCList.Select((value, idx) => new { value, idx })) {
                    var diffms = (holdStartTime - ssrc.value.FirstCaptureTime).TotalMilliseconds;
                    if (diffms >= -1000 && diffms <= 1000) {
                        _nLog.Info($"{baseTab}\t\t\t 在 RecvSSRCList 中找到第 {ssrc.idx} 個 ssrc({ssrc.value.SSRC}, dur={ssrc.value.TotalTime}) 為 MOH, 時間差(diffms)={diffms}");
                        var IsSsrcExists = recFile.SendSSRCList.Exists(x => x.SSRC == ssrc.value.SSRC);
                        if (IsSsrcExists) {
                            _nLog.Info($"{baseTab}\t\t\t 因為 ssrc = {ssrc.value.SSRC} 已經存在於 SendSSRCList 中，處理略過");
                        }
                        else {
                            ssrc.value.BeHeld = true;
                            recFile.SendSSRCList.Insert(ssrc.idx, ssrc.value);
                            _nLog.Info($"{baseTab}\t\t\t 已經插入 ssrc = {ssrc.value.SSRC} 到 SendSSRCList 中");
                        }
                        break;
                    }                    
                }
            }            
            
            _nLog.Info($"{baseTab}\t 新的 SendSSRCList =>");
            foreach (var ssrc in recFile.SendSSRCList.Select((value, idx) => new { value, idx })) {
                _nLog.Info($"{baseTab}\t\t 第 {ssrc.idx:00} 個({ssrc.value.SSRC,-10}): BeHeld={ssrc.value.BeHeld,-5}, FirstCaptureTime{ssrc.value.FirstCaptureTime.ToTimeStr(":", 3)}, TotalTime={ssrc.value.TotalTime, 6}, totalPket={ssrc.value.TotalPacket}, startRtpTimestamp={ssrc.value.StartRtpTimestamp}, StartRtpSeq={ssrc.value.StartRtpSeq}");
            }                

            _nLog.Info($"{baseTab}\t 開始同步 SendSSRCList ...");                        
            // TODO: 找一個有多段 MOH 的音檔來測試!
            decimal padPos = 0;            
            foreach (var item in recFile.SendSSRCList.Select((value, idx) => new { value, idx })) {                                
                if (item.value.BeHeld) {                    
                    var padSec = item.value.TotalTime;
                    _nLog.Info($"\t\t\t 插入 MOH, SSRC={item.value.SSRC}, {padSec}@{padPos}");
                    var newWavFile = Path.Combine(Path.GetDirectoryName(wavSendFileName), 
                                                $"{Path.GetFileNameWithoutExtension(wavSendFileName)}_beheld_{item.idx:00}.wav");
                    if (PadSilence(padSendFile, padPos, padSec, newWavFile, out err)) {
                        padSendFile = newWavFile; // 準備給下一個迴圈使用                
                    }
                    else {
                        _nLog.Error($"\t\t\t\t 插入 MOH, srcFile={Path.GetFileName(padSendFile)}, SSRC={item.value.SSRC}, pad={padSec}@{padPos} => 失敗: {err}");
                    }
                }                
                padPos = padPos + item.value.TotalTime;
            }
            return true;
        }

        private bool PadHoldSilence(RecFileModel recFile, string srcWavFileName, out string padWavFileName, out string err) {
            // default 設為原來的音檔，萬一處理錯誤，使用原來的音檔
            err = "";
            var baseTab = "\t\t";
            padWavFileName = srcWavFileName;
            if (!GlobalVar.AppSettings.Recording.PadHoldSilence) {
                _nLog.Info($"{baseTab} PadHoldSilence 設定關閉, 程序略過");
                return true;
            }

            _nLog.Info($"{baseTab} 開始處理 Hold Silence ...");            
            // 沒有 hold，音檔不須加工
            if (recFile.HoldList == null || recFile.HoldList.Count == 0) {
                _nLog.Info($"{baseTab}\t 無 Hold 事件，程序略過");                
                return true;
            }

            // print 保留 log
            PrintHoldListLog(recFile.HoldList, $"{baseTab}\t");
            
            if (!recFile.HoldList.Any(x=> x.Dir == Project.Enums.ENUM_IPDir.Send)) {
                _nLog.Info($"{baseTab}\t 無任何 Press Hold 事件，程序略過");
                return true;
            }

            DateTime wavStartTime = recFile.RecvSSRCList[0].FirstCaptureTime;
            _nLog.Info($"{baseTab}\t 開始插入 silence ..., srcFile={Path.GetFileName(srcWavFileName)}");                        
            var path = Path.GetDirectoryName(srcWavFileName);
            decimal alreadyPadLen = 0; // 紀錄已經插入的長度
            foreach (var item in recFile.HoldList.Select((value, idx) => new { value, idx })) {
                // 檢查主動 Hold?
                if (item.value.Dir != Project.Enums.ENUM_IPDir.Send)
                    continue;

                // 檢查 主動 Hold 的開始/結束時間不能 null
                var stime = item.value.StartPacketTime.HasValue ? item.value.StartPacketTime.Value.ToTimeStr(":", 3) : "null";
                var etime = item.value.EndPacketTime.HasValue ? item.value.EndPacketTime.Value.ToTimeStr(":", 3) : "null";
                if (!item.value.EndPacketTime.HasValue || !item.value.StartPacketTime.HasValue) {
                    _nLog.Error($"{baseTab}\t @{item.idx:00}: {stime} ~ {etime}, press hold 的起始或結束=null，程序略過");
                    _nLog.Info($"PadHoldSilence 錯誤: press hold 的起始或結束時間=null，程序略過(srcFile={Path.GetFileName(srcWavFileName)})");
                    continue;
                }

                //TODO: 準備一個多段 Hold 的音檔來測試!

                // 插入位置 = Hold封包開始時間 - 此段錄音(第1個SSRC)的開始時間
                //var padPos = (decimal)Math.Round((item.value.StartPacketTime.Value - wavStartTime).TotalSeconds, 2); 

                // ************************************************************************************************************************************
                // *** 插入位置要改:
                // ***  比較 Hold 的開始時間是落在哪一段 SSRC，要插在 哪一段 SSRC 播完結束以後，萬一 send與recv 的 SSRC 結束時間不一樣，要以最晚哪一個時間為主。
                // ************************************************************************************************************************************
                if (!GetPressHoldInsertPos(recFile, item.value, alreadyPadLen, out decimal padPos, out decimal padSec)) {
                    _nLog.Error($"{baseTab}\t 沒有找到對應的 Hold 插入位置，程序略過");
                    continue;
                }
                // 檢查插入負值
                if (padPos < 0 || padSec < 0) {
                    _nLog.Error($"{baseTab}\t @{item.idx:00}: {stime} ~ {etime}, padPos({padPos})<0 或 padSec({padSec})<0，程序略過");
                    _nLog.Info($"PadHoldSilence 錯誤: padPos({padPos})<0 或 padSec({padSec})<0，程序略過(srcFile={Path.GetFileName(srcWavFileName)})");
                    continue;
                }
                
                _nLog.Info($"{baseTab}\t pad silence: {padSec}@{padPos}");
                var newWavFile = Path.Combine(path, $"{Path.GetFileNameWithoutExtension(srcWavFileName)}_hold_{item.idx:00}.wav");
                if (PadSilence(padWavFileName, padPos, padSec, newWavFile, out err)) {
                    alreadyPadLen = alreadyPadLen + padSec; // 紀錄已經插入的長度
                    padWavFileName = newWavFile; // 準備給下一個迴圈使用                
                    _nLog.Info($"{baseTab}\t\t 成功 => {newWavFile}");
                }
                else {
                    _nLog.Error($"{baseTab}\t\t 失敗: {err}");
                    _nLog.Info($"PadHoldSilence.PadSilence 失敗: srcFile={Path.GetFileName(srcWavFileName)}, pad={padSec}@{padPos}, err={err}");
                }
                               
            }
            return true;
        }

        private bool GetPressHoldInsertPos(RecFileModel recFile, HoldModel hold, decimal alreadyInsertLen, out decimal padPos, out decimal padSec) {
            // 只看 Recv 的 SSRCList 就好，因為就算被 MOH，Recv 是一定會收到封包
            var baseTab = "\t\t\t\t";
            var found = false;
            padPos = 0;
            padSec = 0;
            var pos = (decimal)0.00;
            _nLog.Info($"{baseTab} 開始搜尋 RecvSsrcList ...");
            foreach (var item in recFile.RecvSSRCList.Select((value, idx) => new {value, idx})) {
                // 一旦 ssrc 的第 1 個封包時間 > hold 的開始時間，代表 hold 是在這個 ssrc 之前按下的
                if (item.value.FirstCaptureTime > hold.StartPacketTime) {                    
                    found = true;
                    padPos = pos+ alreadyInsertLen; // 要加上之前已經 pad 進去的長度
                    padSec = (decimal)Math.Round((hold.EndPacketTime.Value - hold.StartPacketTime.Value).TotalSeconds, 2);
                    _nLog.Info($"{baseTab}\t 在第 {item.idx} 個 RecvSSRCList 按下hold，alreadyInsertLen={alreadyInsertLen}, pos={pos} => 最後位置={padPos}");
                    break;                    
                }                
                pos = pos + item.value.TotalTime;
            }
            return found;
        }
        

        private void PrintHoldListLog(List<HoldModel> holdList, string baseTab) {
            #region print 保留 log
            _nLog.Info($"{baseTab} 發現 {holdList.Count} 個 Hold 事件:");
            foreach (var item in holdList.Select((value, idx) => new { value, idx })) {
                var start = item.value.StartPacketTime.HasValue ? item.value.StartPacketTime.Value.ToTimeStr(":", 3) : "null";
                var end = item.value.EndPacketTime.HasValue ? item.value.EndPacketTime.Value.ToTimeStr(":", 3) : "null";
                var action = item.value.Dir == Project.Enums.ENUM_IPDir.Send ? "主動保留" : "被保留";
                var dur = "";
                if (item.value.StartPacketTime.HasValue && item.value.EndPacketTime.HasValue)
                    dur = (item.value.EndPacketTime.Value - item.value.StartPacketTime.Value).TotalMilliseconds.ToString();
                _nLog.Info($"{baseTab}\t @{item.idx:00}: action={action}({item.value.Dir}), time={start}~{end}, dur={dur}");
            }
            #endregion
        }


        // 若有錯誤，outputWav 會是原來的 inputWav，不影響音檔的產出
        // 關於音量放打得說明:
        //      1. 加大音量，只能變為 raw 檔(無法直接轉 wav 檔，因為發現 fileSize 變 double)
        //          d:\ffmpeg\ffmpeg.exe -i "a001.wav" -ar 8000 -ac 1 -f mulaw -y -filter:a loudnorm "a002.raw"
        //      2. 再變為 wav
        //          d:\ffmpeg\sox\sox.exe -t raw -r 8000 -e u-law "a002.raw" "a003.wav"        
        private bool VolumeNormalize(string inputWavFile, out string outputWavFile, out string err) {
            var ret = false;
            err = "";
            outputWavFile = inputWavFile;
            var baseTab = "\t";

            if (!GlobalVar.AppSettings.Recording.VolumeNormalize)
                return true;

            _nLog.Info($"{baseTab} 正在音量調整...({Path.GetFileName(inputWavFile)})");
            // 1. 先檢查 rawFileName 檔案是否存在?
            if (!File.Exists(inputWavFile)) {
                err = $"VolumeNormalize: Wav 檔案不存在({inputWavFile})";
                _nLog.Error($"{baseTab}\t {err}");
                return false;
            }
            //var fileName = $"{Path.GetFileNameWithoutExtension(inputWav)}_vol.wav";
            var tempRawFile = Path.Combine(Path.GetDirectoryName(inputWavFile), $"{Path.GetFileNameWithoutExtension(inputWavFile)}_vol.raw");            
            var argConvert = $" -i \"{inputWavFile}\" -ar 8000 -ac 1 -f mulaw -y -filter:a loudnorm \"{tempRawFile}\"";
            var volRet = FFMpegCall(argConvert, 600, out err, out long usingTimeMS); // maxTime = 10 min
            if (volRet != 0) {                
                err = $"VolumeNormalize: 轉換失敗, err={err}, inputWavFile={inputWavFile}";
                _nLog.Error($"{baseTab}\t {err}");
                outputWavFile = inputWavFile; // 使用舊檔
                return false;
            }

            outputWavFile = Path.Combine(Path.GetDirectoryName(inputWavFile), $"{Path.GetFileNameWithoutExtension(inputWavFile)}_vol.wav");
            var wavRet = ConvertRaw2Wav(tempRawFile, 600, out outputWavFile, out err, out usingTimeMS);
            if (wavRet == 0) {
                _nLog.Info($"{baseTab}\t 轉換成功 => {Path.GetFileName(outputWavFile)}");
                ret = true;
            }
            else {
                err = $"VolumeNormalize.ConvertRaw2Wav: 轉換失敗, err={err}, tempRawFile={tempRawFile}";
                _nLog.Error($"{baseTab}\t {err}");
                outputWavFile = inputWavFile; // 使用舊檔
                ret = false;
            }
            return ret;
        }


        /// <summary>
        /// 從 raw 檔案 => 建立 wav 檔。
        /// raw file sample: 5014_20221013-144020_e7247380-1ed100c0-66268-1107660a@10.102.7.17_snd.raw        
        /// </summary>
        /// <param name="recFile">RecFileModel</param>
        /// <param name="sendWavFileName">所產生的 send wav file</param>
        /// <param name="recvWavFileName">所產生的 recv wav file</param>
        /// <returns>當兩個wav檔案都產生OK=true，否則 false</returns>
        private bool ConvertRawToWavFile(RecFileModel recFile, int maxTimeoutSec, out string sendWavFileName, out string recvWavFileName) {
            sendWavFileName = "";
            recvWavFileName = "";

            _nLog.Info($"\t 產生 SEND raw file...{Path.GetFileName(recFile.SendRawFileName)}");
            var sndRet = ConvertRaw2Wav(recFile.SendRawFileName, maxTimeoutSec, out sendWavFileName, out string errMsg, out long useTimeMS);
            var msg = sndRet == 0 ? $"成功, 耗時 {useTimeMS} ms" : $"raw -> wav 轉檔錯誤:{errMsg}, sendWavFile={Path.GetFileName(sendWavFileName)}";
            _nLog.Info($"\t\t => sendRet={sndRet}, {msg}");

            _nLog.Info($"\t 產生 RECV raw file...{Path.GetFileName(recFile.RecvRawFileName)}");
            var rcvRet = ConvertRaw2Wav(recFile.RecvRawFileName, maxTimeoutSec, out recvWavFileName, out errMsg, out useTimeMS);
            msg = rcvRet == 0 ? $"成功, 耗時 {useTimeMS} ms" : $"raw -> wav 轉檔錯誤:{errMsg}, recvWavFile={Path.GetFileName(recvWavFileName)}";
            _nLog.Info($"\t\t => recvRet={rcvRet}, {msg}");

            return (sndRet == 0 && rcvRet == 0);
        }

        #region 以前利用封包時間計算插入 silence 的舊程式碼
        ///// <summary>
        ///// 計算wav錄音檔(send & recv)的總長度(長度要一樣)，並據此進行 send.list<ssrc> 與 recv.list<ssrc> 的同步作業。
        ///// 1. 算出 send && recv 合併後的音檔長度
        ///// 2. 各別呼叫 SyncSSRC 來重新檢視每一段的 SSRC，已決定要如何插入 Silence
        ///// </summary>
        ///// <param name="recFile"></param>
        //private bool SyncWavFile(RecFileModel recFile, string wavSendFileName, string wavRecvFileName, out string padSendFile, out string padRecvFile) {
        //    padSendFile = "";
        //    padRecvFile = "";
        //    if (recFile == null) {
        //        _nLog.Info("\t RecFileModel = null, 錄音檔同步程序略過。");
        //        return false;
        //    }
        //    if (recFile.SendSSRCList == null || recFile.RecvSSRCList == null) {
        //        _nLog.Info("\t SendSSRCList 或 RecvSSRCList = null, 錄音檔同步程序略過。");
        //        return false;
        //    }
        //    if (recFile.SendSSRCList.Count == 0 || recFile.RecvSSRCList.Count == 0) {
        //        _nLog.Info("\t SendSSRCList 或 RecvSSRCList count = 0, 錄音檔同步程序略過。");
        //        return false;
        //    }

        //    var sendLen = recFile.SendSSRCList.Count;
        //    var recvLen = recFile.RecvSSRCList.Count;
        //    GetPacketFirstAndLastTime(recFile, out DateTime baseStartCapTime, out DateTime baseEndCapTime);
        //    _nLog.Info("");
        //    _nLog.Info($"\t baseStartCapTime={baseStartCapTime.ToTimeStr(":", 3)}, baseEndCapTime={baseEndCapTime.ToTimeStr(":", 3)}, dur={(baseEndCapTime - baseStartCapTime).TotalSeconds}");

        //    _nLog.Info($"\t ===> 開始計算 send: 共 {sendLen} 個 ssrc");
        //    padSendFile = SyncSSRC(recFile.SendSSRCList, baseStartCapTime, baseEndCapTime, wavSendFileName);

        //    _nLog.Info($"\t ===> 開始計算 recv: 共 {recvLen} 個 ssrc");
        //    padRecvFile = SyncSSRC(recFile.RecvSSRCList, baseStartCapTime, baseEndCapTime, wavRecvFileName);
        //    return true;
        //}

        ///// <summary>
        ///// 計算整段音檔封包的最早與最晚時間，包含 recFile.SendSSRCList && recFile.RecvSSRCList 
        ///// 取得此音檔開始與結束時間。
        ///// </summary>
        ///// <param name="recFile"></param>
        ///// <param name="startTime"></param>
        ///// <param name="endTime"></param>
        //private void GetPacketFirstAndLastTime(RecFileModel recFile, out DateTime startTime, out DateTime endTime) {
        //    var sendLen = recFile.SendSSRCList.Count;
        //    var recvLen = recFile.RecvSSRCList.Count;
        //    startTime = DateTime.MinValue;
        //    endTime = DateTime.MinValue;
        //    // startTime => 比較 send 跟 recv 的第 1 個封包，哪一個最早  
        //    startTime = recFile.SendSSRCList[0].FirstCaptureTime;
        //    if (recFile.RecvSSRCList[0].FirstCaptureTime < startTime) {
        //        startTime = recFile.RecvSSRCList[0].FirstCaptureTime;
        //    }
        //    // endTime => 比較 send 跟 recv 的最後 1 個封包，哪一個最晚  
        //    endTime = recFile.SendSSRCList[sendLen - 1].LastCaptureTime;
        //    if (recFile.RecvSSRCList[recvLen - 1].LastCaptureTime > endTime) {
        //        endTime = recFile.RecvSSRCList[recvLen - 1].LastCaptureTime;
        //    }            
        //}

        ///// <summary>
        ///// 1. 對 ssrc list 進行解析，分析每一段 ssrc 占用的時間，取得 ssrc 的開始時間(FirstCaptureTime)及結束時間(FirstCaptureTime + 封包數 x 每一包佔用時間)
        ///// 2. 比較第 2 段 ssrc 的 FirstCaptureTime 與前一段的結束時間的時間差，進而找出每一段 ssrc 的前面是否要插入 silence。 
        ///// 3. 插入 silence，產生最終的 send 或 recv 的 wav 檔案
        ///// 4. 只有一段 SSRC 也要算，要確保 send & recv 的 wav 合併正確。(長度盡量一樣)
        ///// 注意:
        /////     每一段 ssrc 占用的時間，用封包個數 x 0.02 所算出的時間比較準，
        /////     因為每一段 ssrc 的封包到達時間與真正撥放的時間序，是有差別的。        ///     
        ///// </summary>
        ///// <param name="ssrcList">SSRC 的 List</param>
        ///// <param name="baseStartCapTime">錄音檔第 1 個封包開始時間</param>
        ///// <param name="baseEndCapTime">錄音檔最後封包的時間</param>
        ///// <param name="orgWavFileName">還沒有 PadSilence 之前的 Wav File Name</param>
        ///// <returns>Pad Silence 之後的最終錄檔</returns>
        //private string SyncSSRC(List<SsrcControlModel> ssrcList, DateTime baseStartCapTime, DateTime baseEndCapTime, string orgWavFileName) {
        //    //var endWavFile = "";
        //    var baseCapTime = baseStartCapTime;
        //    var insertPktCount = 0;
        //    decimal insertSec = 0;
        //    decimal insertPos = 0;
        //    decimal diffMS = 0;            

        //    if (!File.Exists(orgWavFileName)) {
        //        _nLog.Info($"\t 原始 wav 檔案不存在({orgWavFileName})");
        //        return "";
        //    }

        //    var srcWav = orgWavFileName;
        //    var ssrcFrameMilliSec = 0;
        //    foreach (var obj in ssrcList.Select((ssrc, index) => (ssrc, index))) {
        //        ssrcFrameMilliSec = obj.ssrc.FrameMilliSec; // 應該每一個 ssrc 都一樣
        //        insertPos = Math.Round(insertPos, 2);
        //        #region 寫 SSRC 的 log
        //        var pktTime = Math.Round((obj.ssrc.LastCaptureTime - obj.ssrc.FirstCaptureTime).TotalSeconds, 2);
        //        var pktTimeStr = $"{obj.ssrc.FirstCaptureTime.ToTimeStr(":", 3)} ~ {obj.ssrc.LastCaptureTime.ToTimeStr(":", 3)}({pktTime}秒)";
        //        _nLog.Info($"\t\t @@第 {obj.index + 1} 個 SSRC={obj.ssrc.SSRC}({obj.ssrc.BytesPerSecond}/{obj.ssrc.FrameBytes}/{obj.ssrc.FrameMilliSec}), 封包到達={pktTimeStr}, 封包數={obj.ssrc.TotalPacket}({obj.ssrc.TotalTime}秒), baseTime={baseCapTime.ToTimeStr(":", 3)}");
        //        #endregion

        //        insertPktCount = GetInsertPacketCount(baseCapTime, obj.ssrc.FirstCaptureTime, obj.ssrc.FrameMilliSec, out diffMS, out insertSec);

        //        #region 計算下一次封包的基準時間
        //        // 重設 baseCapTime = ssrc 的最後一個封包的 CaptureTime，準備給下一個迴圈使用                
        //        //baseCapTime = obj.ssrc.LastCaptureTime; 

        //        // 下一個迴圈使用的 baseCapTime = ssrc第1個封包到達的時間 + 此 ssrc封包占用的時間 => 這才是這一段 ssrc 聲音播完的時間，
        //        // 用封包到達的時間會不准，因為有 jitter 的關係，所以...
        //        baseCapTime = obj.ssrc.FirstCaptureTime.AddSeconds((double)obj.ssrc.TotalTime);
        //        #endregion

        //        _nLog.Info($"\t\t\t ssrc.Offset={diffMS} ms，插入封包={insertPktCount}，插入={insertSec}@{insertPos}, nextBaseTime={baseCapTime.ToTimeStr(":", 3)}");                                
        //        if (insertSec > 0) {
        //            var newWavFile = Path.Combine()
        //            if (PadSilence(orgWavFileName, srcWav, obj.index, insertPos, insertSec, out string newWavFile, out string err)) {                     
        //                srcWav = newWavFile; // 準備給下一個迴圈使用                
        //            }                    
        //        }
        //        insertPos = insertPos + insertSec + (decimal)obj.ssrc.TotalTime; // 下一次的插入點
        //    }

        //    #region 這一段廢棄，因為音檔合併的時候，若長度不一樣，合併總長度會以較長的為主
        //    //// 處理最後的結束時間
        //    //insertPktCount = GetInsertPacketCount(baseCapTime, baseEndCapTime, ssrcFrameMilliSec, out diffMS, out insertSec);
        //    //_nLog.Info($"\t\t TheEnd: 通話結束.Offset={diffMS} ms，插入封包={insertSec}，插入秒數={insertSec}");
        //    //if (insertSec > 0) {
        //    //    if (PadSilence(orgWavFileName, srcWav, -1, insertPos, insertSec, out endWavFile)) {  // -1 代表由後端 pad silence                
        //    //        srcWav = endWavFile; // 準備給下一個迴圈使用                
        //    //    }
        //    //}
        //    #endregion
        //    _nLog.Info("");
        //    return srcWav;
        //}

        ///// <summary>
        ///// 對wav音檔插入 silence
        /////     從前面加 Silence: sox "before.wav" "after-pad-2.wav" pad 5@2
        /////     從後面加 Silence: sox "before.wav" "after-pad-0.wav" pad 5@-0        
        ///// </summary>
        ///// <param name="orgWavFile">還沒有 PAD 之前最原始的 wav 檔</param>
        ///// <param name="srcWavFile">本次要 pad silence 的 wav 檔</param>
        ///// <param name="index">SSRC的順序(start from 0)，-1 代表最尾端的 PadSilence</param>
        ///// <param name="padPos">插入時間點，如果 index=-1(尾端)，則 padPos 忽略</param>
        ///// <param name="padSec">插入 Silence 的時間</param>
        ///// <param name="newWavFile">新產生的 wav file</param>
        ///// <returns>是否有產生成功</returns>
        //private bool PadSilence(string orgWavFile, string srcWavFile, int index, decimal padPos, decimal padSec, out string newWavFile, out string err) {
        //    err = "";
        //    newWavFile = "";
        //    // 原始的路徑
        //    var path = Path.GetDirectoryName(orgWavFile);
        //    // 
        //    var argConvert = "";            
        //    if (index >= 0) {
        //        newWavFile = Path.Combine(path, $"{Path.GetFileNameWithoutExtension(orgWavFile)}_{index:00}.wav");
        //        argConvert = $" \"{srcWavFile}\" \"{newWavFile}\" pad {padSec}@{padPos}";
        //    }
        //    else { // index = -1，代表尾端加 silence                 
        //        newWavFile = Path.Combine(path, $"{Path.GetFileNameWithoutExtension(orgWavFile)}_end.wav");
        //        argConvert = $" \"{srcWavFile}\" \"{newWavFile}\" pad {padSec}@-0";
        //    }

        //    _nLog.Info($"\t\t\t\t Pad處理: {argConvert}");
        //    var ret = SoxCall(argConvert, 300, out err, out long useTimeMS);
        //    var msg = ret == 0 ? "成功" : $"padSilence失敗: {err}";
        //    _nLog.Info($"\t\t\t\t => Pad結果(={ret}): {msg}");

        //    if (ret == 0) {
        //        if (!File.Exists(newWavFile)) {
        //            err = $"PasSilece 成功，但 outputFile 不存在({newWavFile})";
        //            _nLog.Info($"\t\t\t\t 注意: Pad 後的 wav file 不存在({newWavFile})");
        //            return false;
        //        }
        //        return true;
        //    }            
        //    return false;
        //}

        ///// <summary>
        ///// 計算時間差，再根據 packetTimeSize 來算出要插入的封包數量(insertPacketCount)及插入的 silence 秒數(insertSec)
        ///// 說明: 例如 G.711 每一秒占用 8000 Byte 的音檔，每一包 rtp.payload 使用 160 bytes，共需要 (8000/160=50) 包來填滿 1 秒鐘，
        ///// 所以每一包占用 0.02 秒 = 20ms
        ///// </summary>
        ///// <param name="baseTime"></param>
        ///// <param name="nextTime"></param>
        ///// <param name="packetTimeSize">每一個RTP封包(payload) 所佔據的時間。</param>
        ///// <param name="diffMS">時間差，milliSecond</param>
        ///// <param name="insertSec">插入 silence 的時間值(單位:秒)</param>
        ///// <returns>插入封包數(insertPacketCount)</returns>
        //private int GetInsertPacketCount(DateTime baseTime, DateTime nextTime, int packetTimeSize, out decimal diffMS, out decimal insertSec) {
        //    diffMS = 0;
        //    insertSec = 0;
        //    var insertPacketCount = 0;
        //    // 算出時間差
        //    diffMS = (decimal)Math.Round((nextTime - baseTime).TotalMilliseconds, 2);
        //    if (diffMS > 0) {
        //        insertPacketCount = (int)(diffMS / packetTimeSize);
        //        var x = insertPacketCount * packetTimeSize / 1000.00;
        //        insertSec = (decimal)Math.Round(x, 2);
        //    }
        //    return insertPacketCount;
        //}
        #endregion


        /// <summary>
        /// 對 wav 檔插入 silence
        /// </summary>
        /// <param name="srcWavFile">來源 wav 音檔</param>
        /// <param name="padPos">插入位置</param>
        /// <param name="padSec">插入秒數</param>
        /// <param name="newWavFile">插入後新的檔名</param>
        /// <param name="err">Error Message</param>
        /// <param name="appendEnd">是否由最後端插入</param>
        /// <returns></returns>
        private bool PadSilence(string srcWavFile, decimal padPos, decimal padSec, string newWavFile, out string err, bool appendEnd = false) {
            err = "";                        
            // 
            var argConvert = "";
            if (!appendEnd) {                
                argConvert = $" \"{srcWavFile}\" \"{newWavFile}\" pad {padSec}@{padPos}";
            }
            else { // 代表尾端加 silence                                 
                argConvert = $" \"{srcWavFile}\" \"{newWavFile}\" pad {padSec}@-0";
            }

            _nLog.Info($"\t\t\t\t PadSilence 處理: {argConvert}");
            var ret = SoxCall(argConvert, 300, out err, out long useTimeMS);
            var msg = ret == 0 ? "PadSilence 成功" : $"PadSilence 失敗: {err}";
            _nLog.Info($"\t\t\t\t PadSilence 結果(={ret}): {msg}");

            if (ret == 0) {
                if (!File.Exists(newWavFile)) {
                    err = $"PadSilence 成功，但 outputFile 不存在({newWavFile})";
                    _nLog.Info($"\t\t\t\t 注意: Pad 後的 wav file 不存在({newWavFile})");
                    return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 合併 send + recv 產生一個雙聲道的音檔        
        ///</summary>
        /// <param name="recFile"> RecFileModel</param>
        /// <param name="sendWavFileName">send wav filename</param>
        /// <param name="recvWavFileName">recv wav file name</param>
        private bool MergeWavFile(RecFileModel recFile, string sendWavFileName, string recvWavFileName, out string outputWavFileName, out string err) {
            err = "";
            outputWavFileName = "";
            _nLog.Info($"\t 合併錄音檔 ... ");
            _nLog.Info($"\t\t send={Path.GetFileName(sendWavFileName)}");
            _nLog.Info($"\t\t recv={Path.GetFileName(recvWavFileName)}");
                        
            if (!File.Exists(sendWavFileName)) {
                err = $"音檔合併失敗: send wav 檔案不存在({sendWavFileName})";
                _nLog.Error($"\t\t {err}");
                return false;
            }
            if (!File.Exists(recvWavFileName)) {                
                err = $"音檔合併失敗: send wav 檔案不存在({recvWavFileName})";
                _nLog.Error($"\t\t {err}");
                return false;
            }
            // 
            var path = Path.GetDirectoryName(recFile.SendRawFileName);
            var recFileName = Path.GetFileName(recFile.SendRawFileName);
            recFileName = recFileName.Substring(0, recFileName.Length - 8) + "_merged.wav"; // 去掉 "_snd.raw" 8 碼
            outputWavFileName = Path.Combine(path, recFileName); 

            // 用 sox，send/recv 長度不一致時，merge 後的檔案長度，會以較長的為主
            var argConvert = $"-M -c 1 \"{sendWavFileName}\" -c 1 \"{recvWavFileName}\" \"{outputWavFileName}\"";
            _nLog.Info($"\t\t\t 執行指令: {argConvert}"); 
            var ret = SoxCall(argConvert, 600, out string errMsg, out long usems);
            if ( ret == 0 ) {
                _nLog.Info($"\t\t => Wav檔合併成功(ret={ret}), 花費時間: {usems} ms, outputFile={outputWavFileName}");
                return true;
            }
            else {
                err = $"Wav檔合併失敗: {errMsg}, send={Path.GetFileName(sendWavFileName)}, recv={Path.GetFileName(recvWavFileName)}";
                _nLog.Error($"\t\t => {err}");
                return false;
            }
        }

        private bool CreateRecordingFile(RecFileModel recFile, string mergeWavFile, out string outputWavFileName, out string err) {
            err = "";
            outputWavFileName = "";
            var baseTab = "\t";
            _nLog.Info($"{baseTab} 錄音檔產生中 ... ");
            #region 檢查錄音檔，有錯誤...後續藥補救
            if (!File.Exists(mergeWavFile)) {
                err = $"錄音檔產生失敗: input wav 檔案不存在({mergeWavFile})";
                _nLog.Error($"{baseTab}\t {err}");
                return false;
            }

            var padHold = PadHoldSilence(recFile, mergeWavFile, out string holdWavFile, out err);
            
            var outputPath = Path.Combine(GlobalVar.RecDataPath, recFile.RecStartTime.ToString("yyyyMM"), recFile.RecStartTime.ToString("yyyyMMdd"), recFile.ExtNo);
            _nLog.Error($"{baseTab}\t 正在檢查錄音檔路徑 ...({outputPath})");
            lib_misc.ForceCreateFolder(outputPath);
            if (!Directory.Exists(outputPath)) {
                err = $"無法建立錄音檔路徑";
                _nLog.Info($"{baseTab}\t\t {err}");
                return false;
            }
            #endregion

            // 取得最原先的 raw 檔名，用來產生最終的 wav 檔
            // 5014_20221013-144020_e7247380-1ed100c0-66268-1107660a@10.102.7.17_snd.raw => 5014_20221013-144020_e7247380-1ed100c0-66268-1107660a@10.102.7.17.wav
            var recFileName = Path.GetFileName(recFile.SendRawFileName);
            recFileName = recFileName.Substring(0, recFileName.Length - 8) + ".wav"; // 去掉 "_snd.raw" 8 碼
            outputWavFileName = Path.Combine(outputPath, recFileName);

            _nLog.Error($"{baseTab}\t 正在複製錄音檔 ...({outputWavFileName})");
            lib_misc.CopyFile(holdWavFile, outputWavFileName, out err);
            if (string.IsNullOrEmpty(err)) {
                _nLog.Info($"{baseTab}\t\t 錄音檔複製成功: {outputWavFileName}");
                return true;
            }
            else {
                _nLog.Info($"{baseTab}\t\t 錄音檔複製失敗: {err}");
                return false;
            }            
        }

        /// <summary>
        /// 轉換 raw 檔案變成 wav 檔( mono 單聲道)
        /// </summary>
        /// <param name="rawFileName">raw file name, 範例："~~~*_rcv.raw"</param>
        /// <param name="wavFileName">產生的 wav file name</param>
        /// <param name="err">錯誤訊息</param>
        /// <param name="usingTimeMS">轉換使用的時間(ms)</param>
        /// <returns>
        ///   0: ok
        ///  -1: raw file 不存在 
        ///  -2: 要產生的 wav 檔案已經存在
        /// -96: exe 檔案不存在,
        /// -97: convert exception,
        /// -98: process 沒有執行
        /// -99: timeout         
        /// </returns>
        public int ConvertRaw2Wav(string rawFileName, int maxTimeoutSec, out string wavFileName, out string err, out long usingTimeMS) {
            usingTimeMS = 0;
            err = "";                        
            
            wavFileName = Path.ChangeExtension(rawFileName, ".wav");
                        
            // 1. 先檢查 rawFileName 檔案是否存在?
            if (!File.Exists(rawFileName)) {
                err = $"raw file not found({rawFileName})";
                return -1;
            }            

            // 2. 檢查 wav 檔案是否已經存在，若已存在，要換檔名
            for(var i=1; i <=10; i++) {
                if (File.Exists(wavFileName)) {
                    var newWavFile = $"{Path.GetFileNameWithoutExtension(rawFileName)}_({i}).wav";
                    wavFileName = Path.Combine(Path.GetDirectoryName(rawFileName), newWavFile);
                }
                else {
                    break;
                }
            }
            if (File.Exists(wavFileName)) {
                err = $"the output Wav file is still exists(={wavFileName})";
                return -2;
            }

            // 3. 開始轉換
            var audioBytesPerSec = GlobalVar.AppSettings.Monitor.AudioBytesPerSec;

            #region 使用 ffmpeg...，參數有問題，File Size 會變大，疑似變 16 bit
            //// ffmpeg -f mulaw -ar 8000 -i spp_test_01.snd spp_A03.wav            
            //var argConvert = $"-f mulaw -ar {audioBytesPerSec} -i \"{rawFileName}\" \"{wavFileName}\""; // 8000 的部分看設定。
            //ret = FFMpegCall(argConvert, maxTimeoutSec, out err, out usingTimeMS);
            #endregion
                        
            //使用 sox，fileSize ok => sox.exe -t raw -r 8000 -e u-law "org_rcv.raw" "sox-01.wav"
            var argConvert = $"-t raw -r {audioBytesPerSec} -e u-law \"{rawFileName}\" \"{wavFileName}\"";             
            return SoxCall(argConvert, 300, out err, out long useTimeMS);            
        }


        /// <summary>
        /// FFmpeg 的外部呼叫
        /// </summary>
        /// <param name="args">ffmpeg.exe 後面的參數</param>
        /// <param name="maxTimeoutSec">最大執行時間</param>
        /// <param name="err">錯誤訊息: 目前只有 timeout 錯誤 及 exception</param>
        /// <param name="usingTimeMS">執行時間(ms)</param>
        /// <returns>
        ///   0: ok
        /// -96: exe 檔案不存在,
        /// -97: convert exception,
        /// -98: process 沒有執行
        /// -99: timeout         
        /// </returns>
        public int FFMpegCall(string args, int maxTimeoutSec, out string err, out long usingTimeMS) {
            usingTimeMS = 0;
            var exeFile = GlobalVar.FFMpegExeFileName;
            int ret = -98;
            err = "";

            if (!File.Exists(exeFile)) {
                err = $"EXE執行檔不存在{exeFile}";
                return -96;
            }

            Process process = new Process();
            try {
                process.StartInfo.FileName = exeFile;
                process.StartInfo.Arguments = args;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardError = false;
                //process.StartInfo.RedirectStandardInput = false;
                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.Start();

                //string output;
                //ret = GetProcessOutputWithTimeout(process, timeoutSec * 1000, out output, out err);
                ret = GetProcessOutputWithTimeout(process, maxTimeoutSec * 1000, out err, out usingTimeMS);
            }
            catch (Exception e) {
                ret = -97;
                err = e.Message;
            }

            if (!process.HasExited) {
                try {
                    process.Kill();
                    process.Close();
                    process.Dispose();
                    process = null;
                }
                catch (Exception e) {
                }
            }
            return ret;
        }


        /// <summary>
        /// SOX 的外部呼叫
        /// </summary>
        /// <param name="args">ffmpeg.exe 後面的參數</param>
        /// <param name="maxTimeoutSec">最大執行時間</param>
        /// <param name="err">錯誤訊息: 目前只有 timeout 錯誤 及 exception</param>
        /// <param name="usingTimeMS">執行時間(ms)</param>
        /// <returns>
        ///   0: ok
        /// -96: exe 檔案不存在   
        /// -97: convert exception,
        /// -98: process 沒有執行
        /// -99: timeout         
        /// </returns>
        public int SoxCall(string args, int maxTimeoutSec, out string err, out long usingTimeMS) {
            usingTimeMS = 0;
            var exeFile = GlobalVar.SoxExeFileName;
            int ret = -98;
            err = "";

            if (!File.Exists(exeFile)) {
                err = $"EXE執行檔不存在{exeFile}";
                return -96;
            }

            Process process = new Process();
            try {
                process.StartInfo.FileName = exeFile;
                process.StartInfo.Arguments = args;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardError = false;
                //process.StartInfo.RedirectStandardInput = false;
                process.StartInfo.RedirectStandardOutput = false;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                process.Start();

                //string output;
                //ret = GetProcessOutputWithTimeout(process, timeoutSec * 1000, out output, out err);
                ret = GetProcessOutputWithTimeout(process, maxTimeoutSec * 1000, out err, out usingTimeMS);
            }
            catch (Exception e) {
                ret = -97;
                err = e.Message;
            }

            if (!process.HasExited) {
                try {
                    process.Kill();
                    process.Close();
                    process.Dispose();
                    process = null;
                }
                catch (Exception e) {
                }
            }
            return ret;

        }


        /// <summary>
        /// Process 外部呼叫
        /// </summary>
        /// <param name="process">process 物件</param>
        /// <param name="timeoutMiSec">最大的執行持間</param>
        /// <param name="err">目前只有 Timeout 的錯誤</param>
        /// <param name="usingTimeMS">執行時間(ms)</param>
        /// <returns>
        ///   0: 成功       
        /// -99: timeout
        /// 其他: 失敗
        /// </returns>
        /// 其他說明:
        ///     public int GetProcessOutputWithTimeout(Process process, int timeoutMiSec, out string output, out string err) {
        ///     因為讀取 StandardOutput/StandardError 有時候會導致 ffmpeg 回不來，
        ///     原來是為了要讀到錯誤原因，比較好追蹤debug，但結果讀到的內容幫助不大，乾脆不讀了
        ///     如果還需要讀 StandardOutput/StandardError，把 mark(//) 拿掉即可。
        public int GetProcessOutputWithTimeout(Process process, int timeoutMiSec, out string err, out long usingTimeMS) {
            usingTimeMS = 0;
            //CancellationToken cancelToken;
            var _cancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _cancellationTokenSource.Token;

            //string outputLocal = "";
            //string errLocal = "";
            int localExitCode = -1;
            int exitCode = -1;

            //output = "";
            err = "";

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            var task = Task.Factory.StartNew(() => {
                //outputLocal = process.StandardOutput.ReadToEnd();
                //errLocal = process.StandardError.ReadToEnd();
                process.WaitForExit();
                localExitCode = process.ExitCode;
            }, cancellationToken);

            if (task.Wait(timeoutMiSec, cancellationToken)) {
                //output = outputLocal;                
                exitCode = localExitCode; // 這裡的 exitCode = 0 是正常，若不是，就是有問題。
            }
            else {
                //output = outputLocal;
                err = string.Format("process timeout({0})", timeoutMiSec);
                exitCode = -99; // timeout                                
            }
            stopWatch.Stop();
            usingTimeMS = stopWatch.ElapsedMilliseconds;
            _cancellationTokenSource.Dispose();
            return exitCode;
        }


        public void RequestStop() {                        
            _nLog.Info($"{_tag} is requested to stop ...");
            State = WorkerState.Stopping;
            _shouldStop = true;
        }

    }

}
