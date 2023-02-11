using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using System.Xml.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;

namespace Project.Lib {
    public static class lib_misc {
        public static DateTime FirstDayOfYear(DateTime dateTime) {
            return new DateTime(dateTime.Year, 1, 1);
        }

        public static string FirstDayOfYear(DateTime dateTime, string format) {
            DateTime dt = new DateTime(dateTime.Year, 1, 1);
            return dt.ToString(format);
        }

        public static DateTime FirstDayOfMonth(DateTime dateTime) {
            return new DateTime(dateTime.Year, dateTime.Month, 1);
        }

        public static string FirstDayOfMonth(DateTime dateTime, string format) {
            DateTime dt = new DateTime(dateTime.Year, dateTime.Month, 1);
            return dt.ToString(format);
        }

        public static DateTime LastDayOfYear(DateTime dateTime) {
            return new DateTime(dateTime.Year, 12, 31);
        }

        public static string LastDayOfYear(DateTime dateTime, string format) {
            DateTime dt = new DateTime(dateTime.Year, 12, 31);
            return dt.ToString(format);
        }

        public static DateTime LastDayOfMonth(DateTime dateTime) {
            DateTime firstDayOfTheMonth = new DateTime(dateTime.Year, dateTime.Month, 1);
            return firstDayOfTheMonth.AddMonths(1).AddDays(-1);
        }

        public static string LastDayOfMonth(DateTime dateTime, string format) {
            DateTime firstDayOfTheMonth = new DateTime(dateTime.Year, dateTime.Month, 1);
            return firstDayOfTheMonth.AddMonths(1).AddDays(-1).ToString(format);
        }

        public static DateTime GetStartOfLastWeek() {
            int DaysToSubtract = (int)DateTime.Now.DayOfWeek + 7;
            DateTime dt = DateTime.Now.Subtract(TimeSpan.FromDays(DaysToSubtract));
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, 0);
        }

        public static DateTime GetEndOfLastWeek() {
            DateTime dt = GetStartOfLastWeek().AddDays(6);
            return new DateTime(dt.Year, dt.Month, dt.Day, 23, 59, 59, 999);
        }

        public static DateTime GetStartOfCurrentWeek() {
            int DaysToSubtract = (int)DateTime.Now.DayOfWeek;
            DateTime dt = DateTime.Now.Subtract(TimeSpan.FromDays(DaysToSubtract));
            return new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, 0);
        }

        public static DateTime GetEndOfCurrentWeek() {
            DateTime dt = GetStartOfCurrentWeek().AddDays(6);
            return new DateTime(dt.Year, dt.Month, dt.Day, 23, 59, 59, 999);
        }

        public static string ParseRangeString(string aOrgStr) {
            string result = "";
            if (string.IsNullOrEmpty(aOrgStr))
                return result;
            //            
            int start = 0, end = 0;
            var list1 = aOrgStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim()).ToList();
            foreach (string str in list1)
            {
                if (int.TryParse(str, out start)) // is digit and without -                                    
                    result = result + start.ToString() + ',';
                else if (str.IndexOf('-') >= 0)
                {
                    var list2 = str.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).Where(x => !string.IsNullOrWhiteSpace(x)).Select(y => y.Trim()).ToList();
                    if ((int.TryParse(list2[0], out start)) && (int.TryParse(list2[1], out end)))
                    {
                        // swap
                        if (start > end)
                        {
                            int tmp = end; end = start; start = tmp;
                        }
                        for (int k = start; k <= end; k++)
                        {
                            if (result.IndexOf(k.ToString() + ',') < 0) // not exists
                                result = result + k.ToString() + ',';
                        }
                    }
                }
            }

            if (result.Length > 0)
                result = result.Substring(0, result.Length - 1); // remove the last ,

            return result;
        }

        public static string GetSecondTimeExp(int sec) {
            string result = "";
            int h = 0;
            int m = 0;
            int s = 0;

            h = sec / 3600;
            s = sec - (h * 3600);
            m = s / 60;
            s = sec - (h * 3600) - (m * 60);
            //
            if (h > 0)
                result = string.Format("{0}:{1}:{2}", h, m.ToString("D2"), s.ToString("D2"));
            else if (m > 0)
                result = string.Format("{0}:{1}", m, s.ToString("D2"));
            else
                result = string.Format("{0}", s);

            //
            return result;
        }

        public static string GetSecondTimeExp2(int sec) {
            string result = "";
            int d = 0;
            int h = 0;
            int m = 0;
            int s = 0;

            h = sec / 3600;
            s = sec - (h * 3600);
            m = s / 60;
            s = sec - (h * 3600) - (m * 60);
            //
            d = h / 24;
            h = h - (d * 24);


            if (d > 0)
                result = $"{d}天{h}小時之前";
            else if (h > 0)
                result = $"{h}小時{m}分鐘之前";
            else if (m > 0)
                result = $"{m}分鐘{s}秒之前";
            else
                result = $"{s}秒之前";
            //
            return result;
        }

        public static string GetSecondTimeExp3(int sec) {
            string result = "";
            int d = 0;
            int h = 0;
            int m = 0;
            int s = 0;

            h = sec / 3600;
            s = sec - (h * 3600);
            m = s / 60;
            s = sec - (h * 3600) - (m * 60);
            //
            d = h / 24;
            h = h - (d * 24);


            if (d > 0)
                result = $"{d}天之前";
            else if (h > 0)
                result = $"{h}小時之前";
            else if (m > 0)
                result = $"{m}分鐘之前";
            else
                result = $"{s}秒之前";
            //
            return result;
        }

        public static string FindValueFromJasonArray(string targetName, JArray jArray) {
            string name = "", value = "";
            foreach (JObject item in jArray)
            {
                name = (string)item.GetValue("name");
                if (name.Equals(targetName, StringComparison.CurrentCultureIgnoreCase))
                {
                    value = (string)item.GetValue("value");
                    break;
                }
            }
            return value;
        }

        public static void Swap<T>(ref T min, ref T max) {
            T t = max;
            max = min;
            min = t;
        }

        public static int CopyFile(string from, string to, out string err) {
            err = "";
            var ret = 1;
            try
            {
                System.IO.File.Copy(from, to, true);
            }
            catch (Exception ex)
            {
                err = ex.Message;
                ret = -1;
            }
            return ret;
        }
        
        public static long? LongConvert(string value) {

            long? result = null;
            if (!string.IsNullOrEmpty(value))
            {
                long item = 0;
                bool isConvert = long.TryParse(value, out item);
                result = isConvert ? (long?)item : null;
            }
            return result;
        }

        public static string StringConvert(string value) {
            string result = null;
            if (!string.IsNullOrEmpty(value))
            {
                result = value;
            }
            return result;
        }

        public static string GetEnumDescription(Enum value) {
            FieldInfo field = value.GetType().GetField(value.ToString());
            DescriptionAttribute attribute
                    = Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute;
            return attribute == null ? value.ToString() : attribute.Description;
        }

        /// <summary>
        /// Build a select list for an enum
        /// </summary>
        public static SelectList SelectListFor<T>() where T : struct {
            Type t = typeof(T);
            return !t.IsEnum ? null
                             : new SelectList(BuildSelectListItems(t), "Value", "Text");
        }

        /// <summary>
        /// Build a select list for an enum with a particular value selected 
        /// </summary>
        public static SelectList SelectListFor<T>(T selected) where T : struct {
            Type t = typeof(T);
            return !t.IsEnum ? null
                             : new SelectList(BuildSelectListItems(t), "Value", "Text", selected.ToString());
        }

        public static IEnumerable<SelectListItem> BuildSelectListItems(Type t) {
            return Enum.GetValues(t)
                       .Cast<Enum>()
                       .Select(e => new SelectListItem { Value = Convert.ToInt32(e).ToString(), Text = GetEnumDescription(e) });
        }

        public static string GetDisplayName(Type myClass, string prop) {
            try {
                MemberInfo property = myClass.GetProperty(prop);                
                var da = property.GetCustomAttribute<DisplayAttribute>();
                if (da != null) {
                    return da.Name;
                }
                else {
                    return "unsigned";
                }                
            }
            catch (Exception ex) {
                return "???";
            }            
        }


        public static string ForceCreateFolder(string path) {
            var err = "";
            if (Directory.Exists(path))
                return err;
            try {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) {
                err = ex.Message;
            }
            return err;
        }

        public static string GetFunctionName() {
            var st = new StackTrace();
            var sf = st.GetFrame(0);
            return sf.GetMethod().Name;
        }


        /// <summary>
        /// Determines whether the collection is null or contains no elements.
        /// </summary>
        /// <typeparam name="T">The IEnumerable type.</typeparam>
        /// <param name="enumerable">The enumerable, which may be null or empty.</param>
        /// <returns>
        ///     <c>true</c> if the IEnumerable is null or empty; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> enumerable) {
            if (enumerable == null) {
                return true;
            }
            /* If this is a list, use the Count property for efficiency. 
             * The Count property is O(1) while IEnumerable.Count() is O(N). */
            var collection = enumerable as ICollection<T>;
            if (collection != null) {
                return collection.Count < 1;
            }
            return !enumerable.Any();
        }

        public static string GetLocalIP() {
            string IPaddress = "";

            try {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList) {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) {
                        IPaddress = ip.ToString();
                        break;
                    }
                }
            }
            catch (Exception ex) {                
                IPaddress = "";
            }
            return IPaddress;
        }

        public static bool CheckFileNameValidChar(string fullFileName) {
            // 檢查 PATH
            var invalidPathChar = Path.GetInvalidPathChars();
            var path = Path.GetDirectoryName(fullFileName);
            if (string.IsNullOrEmpty(path))
                return true;
            if (path.IndexOfAny(invalidPathChar) >= 0)
                return false;
            // 檢查 file name
            var invalidFileChar = Path.GetInvalidFileNameChars();
            var fileName = Path.GetFileName(fullFileName);
            if (string.IsNullOrEmpty(fileName))
                return true;
            if (fileName.IndexOfAny(invalidFileChar) >= 0)
                return false;
            //
            return true;
        }

        // 取得檔案的非法字元
        public static string GetInvalidateCharOfFilename() {            
            return new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
        }

        public static string MakeFilenameValidate(string srcStr, string replace) {
            var retStr = srcStr;
            var invalid = GetInvalidateCharOfFilename();

            // 置換每一個非法字元
            foreach (char c in invalid) {
                retStr = retStr.Replace(c.ToString(), replace);
            }
            return retStr;
        }

        public static bool IsValidJson(string strInput) {
            if (string.IsNullOrWhiteSpace(strInput)) { return false; }
            strInput = strInput.Trim();
            if ((strInput.StartsWith("{") && strInput.EndsWith("}")) || //For object & //For array
                (strInput.StartsWith("[") && strInput.EndsWith("]"))) {
                try {
                    var obj = JToken.Parse(strInput);
                    return true;
                }
                catch (JsonReaderException jex) {
                    return false;
                }
                catch (Exception ex) { //some other exception                   
                    return false;
                }
            }
            else {
                return false;
            }
        }

        public static bool DeleteFile(string fileName) {
            var ret = true;
            try {
                File.Delete(fileName);
            }
            catch (Exception ex) {
                ret = false;
            }
            return ret;
        }
        public static bool RenameFile(string from, string to) {
            var ret = true;
            try {
                File.Move(from, to);
            }
            catch (Exception ex) {
                ret = false;
            }
            return ret;
        }

        public static IPEndPoint GetIpEndPoint(IPAddress ipAddress, int port, out string err) {
            err = "";
            IPEndPoint endPoint;
            try {
                endPoint = new IPEndPoint(ipAddress, port);
            }
            catch (Exception ex) {
                err = ex.Message;
                endPoint = null;
            }
            return endPoint;
        }

    }
}