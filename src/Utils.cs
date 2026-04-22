using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace CStancer
{
    internal static class Utils
    {
        internal static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        internal static float GetSafeFloat(object data, string key, float defaultValue = 0f) {
            var val = GetValue(data, key);
            return val != null ? Convert.ToSingle(val) : defaultValue;
        }

        internal static int GetSafeInt(object data, string key, int defaultValue = 0) {
            var val = GetValue(data, key);
            return val != null ? Convert.ToInt32(val) : defaultValue;
        }

        internal static bool GetSafeBool(object data, string key, bool defaultValue = false) {
            var val = GetValue(data, key);
            return val != null ? Convert.ToBoolean(val) : defaultValue;
        }

        private static object GetValue(object data, string key) {
            if (data == null) return null;
            if (data is IDictionary dict) {
                if (dict.Contains(key)) return dict[key];
                return null;
            }
            // Fallback for dynamic/expando objects common in State Bags
            try {
                var props = data.GetType().GetProperty("Item");
                if (props != null) return props.GetValue(data, new object[] { key });
            } catch { }
            return null;
        }

        // StateBag helpers
        internal static float GetStateFloat(StateBag s, string key, float defaultValue = 0f) {
            object v = s.Get(key);
            return v != null ? Convert.ToSingle(v) : defaultValue;
        }

        internal static int GetStateInt(StateBag s, string key, int defaultValue = 0) {
            object v = s.Get(key);
            return v != null ? Convert.ToInt32(v) : defaultValue;
        }

        internal static bool GetStateBool(StateBag s, string key, bool defaultValue = false) {
            object v = s.Get(key);
            return v != null ? Convert.ToBoolean(v) : defaultValue;
        }

        public static async Task<string> GetUserInput(string windowTitle, string defaultText, int maxLength)
        {
            await BaseScript.Delay(200);
            DisplayOnscreenKeyboard(1, windowTitle, "", defaultText, "", "", "", maxLength);
            while (UpdateOnscreenKeyboard() == 0) await BaseScript.Delay(0);
            if (UpdateOnscreenKeyboard() == 1) return GetOnscreenKeyboardResult();
            return null;
        }
    }
}
