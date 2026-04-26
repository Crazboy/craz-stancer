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

        // Safer StateBag retrieval
        internal static float? GetStateFloatNullable(StateBag s, string key) {
            object v = s.Get(key);
            if (v == null) return null;
            try { return Convert.ToSingle(v); } catch { return null; }
        }

        internal static float GetStateFloat(StateBag s, string key, float defaultValue = 0f) {
            object v = s.Get(key);
            if (v == null) return defaultValue;
            try { return Convert.ToSingle(v); } catch { return defaultValue; }
        }

        internal static int GetStateInt(StateBag s, string key, int defaultValue = 0) {
            object v = s.Get(key);
            if (v == null) return defaultValue;
            try { return Convert.ToInt32(v); } catch { return defaultValue; }
        }

        internal static bool GetStateBool(StateBag s, string key, bool defaultValue = false) {
            object v = s.Get(key);
            if (v == null) return defaultValue;
            try { return Convert.ToBoolean(v); } catch { return defaultValue; }
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
