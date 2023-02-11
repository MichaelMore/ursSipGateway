using System.Data.SqlClient;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using MiniProfiler.Integrations;
using System.Text;
using Project.Lib;
using Project.Enums;
using Project.ProjectCtrl;
using NLog;
using Dapper;

namespace Project.Database {
    public class BaseDb {
        public string DBConnStr { internal set; get; } = "";
        public int DBQueryTimeout { internal set; get; } = 60;
        public string DBSchemaName { internal set; get; } = "";
        protected CustomDbProfiler DBProf;
        protected Logger sqlTraceLog = LogManager.GetLogger("SqlTrace");
        protected Logger sqlErrorLog = LogManager.GetLogger("SqlError");

        public BaseDb() {            
            DBProf = new CustomDbProfiler();
            DBConnStr = GlobalVar.AppSettings.DBConnection.MainDBConnStr;
            DBConnStr = DecodeDbConnStr(DBConnStr);
            DBSchemaName = $"[{GlobalVar.AppSettings.DBConnection.DBName}].[{GlobalVar.AppSettings.DBConnection.SchemaName}].";
            if (GlobalVar.AppSettings.DBConnection.DBConnectTimeout > 30)
                DBQueryTimeout = GlobalVar.AppSettings.DBConnection.DBConnectTimeout;
        }

        // Destructor
        ~BaseDb() {        
            DBProf = null;
        }

        public int ReadVarbinaryToFile(string sql, string filePath) {
            int ret = 1;
            try {
                using (SqlConnection conn = new SqlConnection(DBConnStr)) {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn)) {
                        cmd.CommandTimeout = DBQueryTimeout;
                        using (SqlDataReader data = cmd.ExecuteReader()) {
                            if (data.Read()) {
                                byte[] content = (byte[])data[0];
                                System.IO.File.WriteAllBytes(filePath, content);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                sqlErrorLog.Error($"{lib_misc.GetFunctionName()} exception: {ex.Message}");
                ret = -1;
            }
            return ret;
        }

        public int ReadVarbinaryToByteArray(string sql, ref byte[] byteArray) {
            int ret = 1;
            try {
                using (SqlConnection conn = new SqlConnection(DBConnStr)) {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand(sql, conn)) {
                        cmd.CommandTimeout = DBQueryTimeout;
                        using (SqlDataReader data = cmd.ExecuteReader()) {
                            if (data.Read()) {
                                byteArray = (byte[])data[0];
                            }
                        }
                    }
                }
            }
            catch (Exception ex) {
                sqlErrorLog.Error($"{lib_misc.GetFunctionName()} exception: {ex.Message}");
                ret = -1;
            }
            return ret;
        }

        private string DecodeDbConnStr(string str) {
            var err = "";            

            if (string.IsNullOrEmpty(str) || str.Contains("Data Source")) { // 表示空字串或沒加密
                return str;
            }
            else {
                var decodeStr = lib_encode.DecryptAES256(str, GlobalVar.DBAesKey, GlobalVar.DBAesIV, out err);
                if (err != "")
                    sqlErrorLog.Error($"{lib_misc.GetFunctionName()} excetpion: {err}");
                return decodeStr;
            }
        }

        public ExceptionHandle UpdateModuleStatus(string moduleName, string moduleID, int errorCode, string errorMsg, int status) {
            ExceptionHandle errHD = new ExceptionHandle();

            string s = @"IF EXISTS (SELECT * FROM TB_ModuleStatus WHERE ModuleID=@moduleID)
                             UPDATE TB_ModuleStatus SET ModuleName = @moduleName, ModuleVer = @moduleVer, LastResponse = @lastResponse, ErrorCode = @errorCode, ErrorMsg = @errorMsg, Status=@status    WHERE ModuleID = @moduleID
                         ELSE
                             INSERT INTO TB_ModuleStatus(ModuleID, ModuleName, ModuleVer, LastResponse, ErrorCode, ErrorMsg , Status) VALUES(@moduleID, @moduleName, @moduleVer, @lastResponse, @errorCode, @errorMsg , @status)";

            if (errorCode == -1 && status == 0) {
                s = $@"UPDATE TB_ModuleStatus SET ModuleName = @moduleName, ModuleVer = @moduleVer, LastResponse = @lastResponse, ErrorCode = @errorCode, Status=@status    WHERE ModuleID = @moduleID";
            }
            try {
                using (SqlConnection conn = new SqlConnection(DBConnStr)) {
                    int ret = conn.Execute(s, new {
                        @moduleName = @moduleName,
                        @moduleVer = GlobalVar.CurrentVersion,
                        @lastResponse = DateTime.Now,
                        @errorCode = errorCode,
                        @errorMsg = errorMsg,
                        @status = status,
                        @moduleID = moduleID
                    }, commandTimeout: DBQueryTimeout);
                }
            } catch (Exception ex) {
                errHD.ParseError(ex);
            }
            return errHD;
        }

        public async Task<int> LogSqlAudit(string funcID, ExceptionHandle errHD) {
            
            await Task.Delay(1);

            ExceptionHandle err = new ExceptionHandle();
            var logType = errHD.Success ? ENUM_SqlLogType.Trace : ENUM_SqlLogType.Error;

            if (logType == ENUM_SqlLogType.Trace) {
                if (!GlobalVar.AppSettings.DBConnection.SqlTrace)
                    return 0;
            }

            var exeCommand = new List<string>();
            foreach (var cmd in DBProf.ProfilerContext.ExecutedCommands) {
                exeCommand.Add($"\t CommandType={cmd.CommandType}, DBName={cmd.Database}");
                exeCommand.Add($"\t {cmd.CommandText}");
                exeCommand.Add("\t Parameters:");
                foreach (var p in cmd.Parameters)
                    exeCommand.Add($"\t\t{p}");
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"\tagentAcct=WEB_API\r\n");
            sb.Append($"\thostName={Dns.GetHostName()}\r\n");
            sb.Append($"\tsourceIP={GlobalVar.LocalIP}\r\n");
            sb.Append($"\taccessAcct={new SqlConnectionStringBuilder(DBConnStr).UserID}\r\n");
            //sb.Append($"\tcontrollerName={controllerContext.ActionDescriptor.ControllerName}\r\n");
            sb.Append($"\tfuncID={funcID}\r\n");
            sb.Append("###ExecutedCommands =>\r\n");
            sb.Append($"{string.Join('\n', exeCommand)}\r\n");
            sb.Append("###FailedCommands =>\r\n");
            sb.Append($"{DBProf.ProfilerContext.GetFailedCommands()}\r\n");

            if (logType == ENUM_SqlLogType.Trace)
                sqlTraceLog.Info(sb.ToString());
            else {
                sqlErrorLog.Error(errHD.UserMessage);
                sqlErrorLog.Error(sb.ToString());
            }
            return 1;
        }
    }
}