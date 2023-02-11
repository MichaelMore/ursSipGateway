using Project.Database;
using Project.Helper;
using Project.Models;
using Project.AppSetting;
using Project.ProjectCtrl;
using NLog;
using NLog.Fluent;
using SharpPcap;
using ThreadWorker;
using WorkerThread;
using System;
using Project.Models;
using System.Text;
using Project.Lib;
using Project.Enums;

namespace Project.WorkerService {
    class MakeFileWorker : BaseWorker {

        public override string className => GetType().Name;
        protected DemoDb db => new DemoDb();

        public MakeFileWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger("Startup");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {            
            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute * 1000, stoppingToken);

            #region 啟動每一個分機的封包解析 Thread
            nlog.Info($"總共有 {GlobalVar.AppSettings.Monitor.Device.Count} 個分機 ...");
            
            foreach (var dev in GlobalVar.AppSettings.Monitor.Device) {                
                var makeFile = new MakeFileThread(dev);

                // parsePacket 加入 Dictionary 中， key = dev.Extn
                GlobalVar.dictMakeFileThread.Add(dev.Extn, makeFile);

                makeFile.StartThread();

                // 此處要等待所有的 ParseThread 全部啟動完以後才可以往下 ...
                while (true) {
                    if (makeFile.State == WorkerState.Running) {
                        break;
                    }
                    Thread.Sleep(1);
                }
            }
            #endregion

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            DateTime checkTime = DateTime.Now;            
            while (!stoppingToken.IsCancellationRequested) {
                #region 等待時間是否已到
                if (!TimeIsUp(GlobalVar.AppSettings.WorkerOption.ProcessIntervalSec, ref checkTime)) {
                    await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);                    
                    continue;
                }
                #endregion
                
                nlog.Info($"");
                #region Do your work
                try {               
                    // 目前沒做事 ...
                    nlog.Info("MainWorker ...");                    
                }
                catch (Exception ex) {
                    nlog.Info($"MainWorker 執行工作發生錯誤：{ex.Message}");
                }
                #endregion                
                await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);                
            }

            // 通知 ReadThread 結束
            //foreach (var thd in listReadThread) {
            //    thd.RequestStop();
            //}

            //// 通知 ParseThread 結束
            //foreach (var thd in listParseThread) {
            //    thd.RequestStop();
            //}
            // 此處也必須等待所有的 Tnread 都離開。
        }        


    }
}
