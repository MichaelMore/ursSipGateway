using Project.Database;
using Project.Helper;
using Project.AppSetting;
using Project.ProjectCtrl;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project.WorkerService {
    class DemoWorker : BaseWorker {

        public override string className => GetType().Name;
        protected DemoDb db => new DemoDb();

        public DemoWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger(className);
        }

        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            nlog.Info($"{GlobalVar.ProjectName} ExecuteAsync starting ...");

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            DateTime checkTime = DateTime.Now;

            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute*1000, stoppingToken);

            while (!stoppingToken.IsCancellationRequested) {                

                // 等待時間是否已到
                if (!TimeIsUp(GlobalVar.AppSettings.WorkerOption.ProcessIntervalSec, ref checkTime)) {
                    await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);
                    continue;
                }
                nlog.Info($"");
                nlog.Info($"");

                try {
                    #region Do your work
                    nlog.Info("執行你的工作 ...");
                    #endregion
                }
                catch (Exception ex) {
                    nlog.Info($"執行工作發生錯誤：{ex.Message}");
                }
                nlog.Info($"========== 執行工作完成 ==========");
                await Task.Delay(GlobalVar.AppSettings.WorkerOption.LoopIntervalMSec, stoppingToken);
            }
        }
        
    }
}
