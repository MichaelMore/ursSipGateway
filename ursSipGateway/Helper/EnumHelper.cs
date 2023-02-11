using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Project.Helpers {
    public static class EnumHelper {
        // Get the value of the description attribute if the   
        // enum has one, otherwise use the value.  
        public static string GetDescription<TEnum>(this TEnum value) {
            var fi = value.GetType().GetField(value.ToString());

            if (fi != null) {
                var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attributes.Length > 0) {
                    return attributes[0].Description;
                }
            }

            return value.ToString();
        }

        // TODO: 解決 null retirn 的告警
        /// <summary>
        /// Build a select list for an enum
        /// </summary>
        public static SelectList SelectListFor<T>() where T : struct {
            Type t = typeof(T);
            return !t.IsEnum ? null
                             : new SelectList(BuildSelectListItemsEx(t), "Value", "Text");
        }

        /// <summary>
        /// Build a select list for an enum with a particular value selected 
        /// </summary>
        public static SelectList SelectListFor<T>(T selected) where T : struct {
            Type t = typeof(T);
            return !t.IsEnum ? null
                             : new SelectList(BuildSelectListItemsEx(t), "Value", "Text", selected.ToString());
        }


        // 這個是取 Enum 的 name，不是 value
        // 例如:  [Description("系統權限")]
        //        SystemRole = 1
        // 取到的是 Value = "SystemRole", Text = "系統權限"        
        private static IEnumerable<SelectListItem> BuildSelectListItems(Type t) {
            return Enum.GetValues(t)
                       .Cast<Enum>()
                       .Select(e => new SelectListItem { Value = e.ToString(), Text = e.GetDescription() });
        }

        // 例如:  [Description("系統權限")]
        //        SystemRole = 1
        // 取到的是 Value = "1", Text = "系統權限"
        public static IEnumerable<SelectListItem> BuildSelectListItemsEx(Type t) {
            return Enum.GetValues(t)
                       .Cast<Enum>()
                       .Select(e => new SelectListItem { Value = Convert.ToInt32(e).ToString(), Text = e.GetDescription() });
        }
    }

    public static class EnumExtension {
        public static string ToDescription<TEnum>(this TEnum value) {
            if (value == null) {
                return string.Empty;
            }
            var fi = value.GetType().GetField(value.ToString());

            if (fi != null) {
                var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

                if (attributes.Length > 0) {
                    return attributes[0].Description;
                }
            }
            return value.ToString();
        }
    }

    public static class Extensions {
        public static IList<T> Clone<T>(this IList<T> listToClone) where T : ICloneable {
            return listToClone.Select(item => (T)item.Clone()).ToList();
        }
    }

    public static class TempDataExtensions {
        public static void Put<T>(this ITempDataDictionary tempData, string key, T value) where T : class {
            tempData[key] = JsonConvert.SerializeObject(value);
        }

        public static T Get<T>(this ITempDataDictionary tempData, string key) where T : class {
            object o;
            tempData.TryGetValue(key, out o);
            return o == null ? null : JsonConvert.DeserializeObject<T>((string)o);
        }
    }

}