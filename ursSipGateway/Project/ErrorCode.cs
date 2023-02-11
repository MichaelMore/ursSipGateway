using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web;

namespace Project.Enums {
    public enum ErrorCode : int {
        #region 訊息定義
        [Description("存取未授權的網頁.")]
        MSG_ACCESS_NOAUTH_URL = 5,

        [Description("閒置逾期(Session)，強制登出.")]
        MSG_SESSION_IDLE_TIMEOUT_LOGOUT = 4,

        [Description("認證過期，強制登出.")]
        MSG_AUTH_EXPIRED_LOGOUT = 3,

        [Description("頁面正常登出.")]
        MSG_NORMAL_LOGOUT = 2,
        #endregion

        [Description("完成")]
        ERR_OK = 0,

        #region 登錄錯誤 100 ~ 199
        [Description("登錄的帳號錯誤.")]
        ERR_LOGIN_USERACCT_NOT_FOUND = 100,

        [Description("登錄的密碼錯誤")]
        ERR_LOGIN_USERPWD_ERROR = 101,
        #endregion

        #region 資料庫 200 ~ -299
        [Description("資料庫存取錯誤(Exception).")]
        ERR_DB_EXCEPTION = 200,
        //
        [Description("資料庫新增失敗.")]
        ERR_DB_INSERT = 201,

        [Description("資料庫更新失敗.")]
        ERR_DB_UPDATE = 202,

        [Description("資料庫刪除失敗.")]
        ERR_DB_DELETE = 203,

        [Description("資料庫讀取失敗.")]
        ERR_DB_FETCH = 204,

        [Description("資料庫讀取到 0 筆資料.")]
        ERR_DB_FETCH_ZERO = 205,

        [Description("資料庫新增 0 筆資料.")]
        ERR_DB_INSERT_ZERO = 206,

        [Description("資料庫更新 0 筆資料.")]
        ERR_DB_UPDATE_ZERO = 207,

        [Description("資料庫刪除 0 筆資料")]
        ERR_DB_DELETE_ZERO = 208,

        [Description("無法讀取資料表的主鍵序號(PK)")]
        ERR_DB_FETCH_SEQ = 209,

        [Description("資料庫的資料已被引用，無法刪除")]
        ERR_DB_DELETE_INUSE = 210,

        [Description("呼叫 Stored Procedure 失敗")]
        ERR_DB_STORE_PROCEDURE = 211,

        #endregion

        #region 主程式(Controller)處理錯誤 300 ~ 399
        [Description("主程式處理發生錯誤")]
        ERR_SERVER_ERROR = 300,

        [Description("無法存取使用者的登錄資訊(Login Info NULL)")]
        ERR_LOGIN_INFO_NULL = 301,

        [Description("角色已被帳號引用，無法停用")]
        ERR_ROLE_USED = 302,

        [Description("由於網頁沒有授權，無法進入此網頁")]
        ERR_URL_NO_AUTH = 303,
        #endregion

        #region 資料格式+處理錯誤 400 ~ 499
        [Description("資料空白")]
        ERR_DATA_EMPTY = 400,

        // 401 ~ 410 => 資料格式相關的錯誤
        [Description("的資料格式錯誤")]
        ERR_DATA_FORMAT = 401,

        // 411 ~ 420 => 資料轉換相關的錯誤
        [Description("資料格式轉換錯誤")] // 一般錯誤
        ERR_DATA_CONVERT = 411,

        [Description("時間與字串轉換失敗")]
        ERR_TIME_STR_CONVERT = 412,
        #endregion

        #region 檔案處理錯誤 500 ~ 599       
        [Description("檔案不存在")]
        ERR_FILE_NOT_FOUND = 501,

        [Description("資料夾不存在")]
        ERR_DIR_NOT_FOUND = 502,

        [Description("EXCEL 資料匯出失敗")]
        ERR_EXCEL_EXPORT_FAILED = 510,

        [Description("EXCEL匯入時，發生不明錯誤")]
        ERR_EXCEL_IMPORT = 511,
        #endregion

        #region JSON 相關錯誤 600 ~ -699
        [Description("JSON格式錯誤")]
        ERR_JSON_PARSE = 600,

        [Description("JSON/Model轉換錯誤")]
        ERR_JSON_TO_MODEL = 601,
        #endregion


        [Description("系統發生意外的錯誤")]
        ERR_EXCEPTION = 999
    }

}