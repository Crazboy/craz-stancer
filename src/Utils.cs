using System;
using System.Collections;
using System.Threading.Tasks;
using CitizenFX.Core;
using static CitizenFX.Core.Native.API;

namespace CStancer
{
    internal static class Utils
    {
        internal static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

        internal static float GetSafeFloat(IDictionary d, string key, float defaultValue = 0f) {
            if (d == null || !d.Contains(key)) return defaultValue;
            var v = d[key]; return v != null ? Convert.ToSingle(v) : defaultValue;
        }

        internal static int GetSafeInt(IDictionary d, string key, int defaultValue = 0) {
            if (d == null || !d.Contains(key)) return defaultValue;
            var v = d[key]; return v != null ? Convert.ToInt32(v) : defaultValue;
        }

        internal static bool GetSafeBool(IDictionary d, string key, bool defaultValue = false) {
            if (d == null || !d.Contains(key)) return defaultValue;
            var v = d[key]; return v != null ? Convert.ToBoolean(v) : defaultValue;
        }

        // StateBag helpers because StateBag doesn't implement IDictionary directly in all core versions
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
            await BaseScript.Delay(200); // Wait for any existing inputs/keys to clear
            DisplayOnscreenKeyboard(1, windowTitle, "", defaultText, "", "", "", maxLength);
            
            while (UpdateOnscreenKeyboard() == 0) 
            {
                await BaseScript.Delay(0);
            }

            if (UpdateOnscreenKeyboard() == 1) return GetOnscreenKeyboardResult();
            return null;
        }
    }
}
