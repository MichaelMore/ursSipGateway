using Microsoft.AspNetCore.Http;
using Project.Enums;
using Project.Helpers;
using System;
using System.ComponentModel;
using System.Data.SqlClient;
using System.Text;
using System.Web;

namespace Project.Lib {

    public class ExceptionHandle {
        // private 屬性
        private bool success;
        private ErrorCode code;

        // public 屬性
        public int SqlErrorNum { get; private set; }
        public ErrorCode Code {
            get {
                return this.code;
            }
            set {
                this.code = value;
                success = (code == ErrorCode.ERR_OK);
                if (!success) {
                    UserMessage = GetErrorCodeMessage(this.code);
                }
            }
        }
        public bool Success {
            get {
                return success;
            }
        }
        public string UserMessage { get; private set; } = "";
        public string TraceMessage { get; set; } = "";
        public bool IsSqlError { get; set; } = false;

        public ExceptionHandle() {
            Reset();
        }

        public void Reset() {
            success = true;
            code = ErrorCode.ERR_OK;
            UserMessage = "";
            TraceMessage = "";
            IsSqlError = false;
        }

        public string ConvertISO_8859_1(string s) {
            return StringToISO_8859_1(s);
        }

        /// <summary>
        /// Builds the exception message.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <returns></returns>
        public static string BuildExceptionMessage(Exception x) {
            Exception logException = x;
            if (x.InnerException != null) {
                logException = x.InnerException;
            }
            StringBuilder message = new StringBuilder();

            try {
                message.AppendLine();
                message.AppendLine("Message : " + logException.Message);
                // Type of Exception
                string exType = logException.GetType().Name;
                message.AppendLine("Type of Exception : " + exType);                
                if (exType.ToUpper() == "SqlException".ToUpper()) {
                    SqlException sqlEx = (SqlException)x;
                    message.AppendLine("SQL Error Code : " + sqlEx.Number);
                    message.AppendLine("SQL Line Number : " + sqlEx.LineNumber);
                    message.AppendLine("Stored Procedure : " + sqlEx.Procedure);
                }
                // Source of the message
                message.AppendLine("Source : " + logException.Source);
                // Stack Trace of the error
                message.AppendLine("Stack Trace : " + logException.StackTrace);
                // Method where the error occurred
                message.AppendLine("TargetSite : " + logException.TargetSite);
            }
            catch (Exception ex) {
                message.AppendLine($"BuildExceptionMessage exception: {ex.Message}");
            }
            return message.ToString();
        }

        public static string GetErrorCodeMessage(ErrorCode code) {
            return code.ToDescription();
        }

        public string ParseError(Exception x) {            
            if (x == null) {
                success = true;
                return "";
            }                
            else // 如果有 x != null 表示有 Exception
                success = false;
            //
            Exception logException = x;
            if (x.InnerException != null) {
                logException = x.InnerException;
            }
            
            string exType = logException.GetType().Name;
            if (exType.ToUpper() == "SqlException".ToUpper()) {
                Code = ErrorCode.ERR_DB_EXCEPTION;
                IsSqlError = true;
                SqlException sqlEx = (SqlException)x;
                SqlErrorNum = sqlEx.Number;
                UserMessage = $"資料庫錯誤({SqlErrorNum}):<br >{sqlEx.Message}";            
            }
            else { // normal error
                Code = ErrorCode.ERR_EXCEPTION; // 一般的系統意外錯誤
                IsSqlError = false;
                SqlErrorNum = 0;
                UserMessage = $"意外的處理錯誤:<br >{x.Message}"; 
            }
            TraceMessage = BuildExceptionMessage(x);
            return UserMessage;
        }

        //private string GetSqlErrorMsg(bool convert8859_1 = false) {
        //    string msg = "";
        //    switch (SqlErrorNum) {
        //        case -902627: msg = "資料的索引鍵值重複，無法新增或更新資料"; break;
        //        case -900547: msg = "因為資料已被引用, 無法刪除資料"; break;
        //        //                
        //        default: msg = "未定義的資料庫錯誤."; break;
        //    }
        //    //
        //    if (!convert8859_1)
        //        return string.Format("{0}({1})", msg, (int)SqlErrorNum);
        //    else
        //        return string.Format("{0}({1})", StringToISO_8859_1(msg), (int)SqlErrorNum);
        //}

        //public static string GetErrorMsg(ErrorCode errorCode, bool withErrorCode = true, bool convert8859_1 = false) {
        //    string msg = errorCode.GetDescription();
        //    if (convert8859_1)
        //        msg = StringToISO_8859_1(msg);

        //    if (withErrorCode)
        //        msg = $"{msg}(錯誤代碼：{(int)errorCode})";

        //    return msg;
        //}

        private static string StringToISO_8859_1(string srcText) {
            string dst = "";
            char[] src = srcText.ToCharArray();
            for (int i = 0; i < src.Length; i++) {
                string str = @"&#" + (int)src[i] + ";";
                dst += str;
            }
            return dst;
        }

    }

    public class ExceptionHandle<T> : ExceptionHandle {
        public T Data { get; set; }
    }

}