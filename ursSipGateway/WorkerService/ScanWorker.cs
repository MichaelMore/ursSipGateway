using Project.Helper;
using Project.AppSetting;
using Project.ProjectCtrl;
using NLog;

namespace Project.WorkerService {
    class ScanWorker : BaseWorker {
        public override string className => GetType().Name;        

        public ScanWorker(HttpClientHelper httpClientHelper, IHostApplicationLifetime hostLifeTime) : base(httpClientHelper, hostLifeTime) {
            //初始化nlog
            nlog = LogManager.GetLogger(className);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            //必須給定前置時間給 Batch 檔啟動服務,不然console會判定service啟動錯誤
            await Task.Delay(GlobalVar.AppSettings.WorkerOption.DelayBefroreExecute * 1000, stoppingToken);

            nlog.Info($"{className} ExecuteAsync starting ...");            

            //DateTime checkTime = DateTime.MinValue; // 故意讓流程一開始要先跑
            DateTime checkTime = DateTime.Now;
            DateTime checkLiveMonitorRenew = DateTime.Now;

            while (!stoppingToken.IsCancellationRequested) {                
                try {                    
                    // 檢查分機的 Renew
                    CheckLiveMonitorRenew(50, ref checkLiveMonitorRenew);                    
                    // ... 其他相關的 Scan
                }
                catch (Exception ex) {
                    nlog.Info($"執行工作發生錯誤：{ex.Message}");
                }                
                await Task.Delay(1000, stoppingToken);
            }
        }

        private void CheckLiveMonitorRenew(int waitSec, ref DateTime checkTime) {
            var IsOpenedList = new List<string>();
            try {
                // 處理監聽的 RTP forward
                var diffSec = (DateTime.Now - checkTime).TotalSeconds;
                if (diffSec >= waitSec) { // 50 秒看一次
                    checkTime = DateTime.Now;
                    nlog.Info($">>> 開始檢查分機監聽 Renew ...");
                    foreach (var mon in GlobalVar.DictLiveMonitor) {                        
                        var ret = mon.Value.CheckRenew();
                        //nlog.Info($"\t 分機({mon.Key})監聽狀態 = {ret}"); <= 太多 log 了
                        if (ret) {
                            IsOpenedList.Add(mon.Key);
                        }
                    }
                    // print
                    nlog.Info($"*** 全部監聽的分機共 {IsOpenedList.Count} 支: {string.Join(", ", IsOpenedList)}\r\n\r\n");
                }
            }
            catch (Exception ex) {
                nlog.Info($"process CheckLiveMonitorRenew raise an exception: {ex.Message}");                
            }
        }


        
    }
}
