using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using static CitizenFX.Core.Native.API;
using MenuAPI;
using System.Linq;

namespace CStancer
{
    public class StancerScript : BaseScript
    {
        private Menu menu;
        private MenuListItem wheelTargetList;
        private MenuSliderItem suspensionSlider, trackSlider, camberSlider, wheelSizeSlider, wheelWidthSlider, tireColliderSizeSlider;
        private MenuItem resetItem, infoItem;

        private int currentVeh = -1, lastVeh = -1;
        private enum WheelTarget { All, Front, Rear, Center }
        private WheelTarget CurrentWheelTarget => (WheelTarget)wheelTargetList.ListIndex;

        private float valSuspension = 0f, valTrack = 0f, valCamber = 0f;
        private float valWheelSize = 0f, valWheelWidth = 0f, valTireCollider = 0f;

        private const string StateData = "cstancer:data";

        private const float ScaleSuspension   = 100f;
        private const float ScaleTrack        = 100f;
        private const float ScaleCamber       = 100f;
        private const float ScaleWheelSize    = 100f;
        private const float ScaleWheelWidth   = 100f;
        private const float ScaleTireCollider = 100f;

        private long lastSyncTime = 0;
        private Dictionary<int, StanceData> appliedStances = new Dictionary<int, StanceData>();

        private struct StanceData
        {
            public float Track;
            public float Camber;
            public float Suspension;
            public float WheelSize;
            public float WheelWidth;
            public float TireCollider;
            public int Target;

            public bool Equals(StanceData other)
            {
                return Track == other.Track && Camber == other.Camber && Suspension == other.Suspension &&
                       WheelSize == other.WheelSize && WheelWidth == other.WheelWidth &&
                       TireCollider == other.TireCollider && Target == other.Target;
            }
        }

        public StancerScript()
        {
            MenuController.MenuToggleKey = (Control)(-1);
            SetupMenu();
            RegisterCommand("cstancer", new Action<int, List<object>, string>((s, a, r) => ToggleMenu()), false);
            RegisterKeyMapping("cstancer", "Open CStancer Menu", "keyboard", "none");
            Tick += OnTick;
        }

        private void SetupMenu()
        {
            menu = new Menu("CStancer", "Stance Tuning");
            MenuController.AddMenu(menu);
            MenuController.MenuAlignment = MenuController.MenuAlignmentOption.Right;

            wheelTargetList        = new MenuListItem("Apply To", new List<string> { "All", "Front", "Rear", "Center" }, 0, "");
            suspensionSlider       = new MenuSliderItem("Suspension Height", "Press Enter to type", -200, 200, 0, true);
            trackSlider            = new MenuSliderItem("Track Width",       "Press Enter to type", -200, 200, 0, true);
            camberSlider           = new MenuSliderItem("Camber",            "Press Enter to type", -200, 200, 0, true);
            wheelSizeSlider        = new MenuSliderItem("Wheel Size",        "Press Enter to type", -200, 200, 0, true);
            wheelWidthSlider       = new MenuSliderItem("Wheel Width",       "Press Enter to type", -200, 200, 0, true);
            tireColliderSizeSlider = new MenuSliderItem("Tire Collider",     "Press Enter to type", -200, 200, 0, true);
            resetItem              = new MenuItem("Reset Stance", "Restore stock offsets");
            infoItem               = new MenuItem("Vehicle Info", "No vehicle") { Enabled = false };

            menu.AddMenuItem(wheelTargetList);
            menu.AddMenuItem(suspensionSlider); menu.AddMenuItem(trackSlider); menu.AddMenuItem(camberSlider);  
            menu.AddMenuItem(wheelSizeSlider);  menu.AddMenuItem(wheelWidthSlider); menu.AddMenuItem(tireColliderSizeSlider);
            menu.AddMenuItem(resetItem); menu.AddMenuItem(infoItem);

            menu.OnSliderPositionChange += (m, item, oldPos, newPos, idx) => {
                if (currentVeh == -1) return;
                SetRealValue(item, (float)newPos / GetScaleForItem(item));
                PushState();
            };

            menu.OnSliderItemSelect += async (m, item, pos, idx) => {
                float currentVal = 0f;
                string target = "";

                if      (item == suspensionSlider)       { target = "suspension";   currentVal = valSuspension; }
                else if (item == trackSlider)            { target = "track";        currentVal = valTrack; }    
                else if (item == camberSlider)           { target = "camber";       currentVal = valCamber; }   
                else if (item == wheelSizeSlider)        { target = "wheelsize";    currentVal = valWheelSize; }
                else if (item == wheelWidthSlider)       { target = "wheelwidth";   currentVal = valWheelWidth; }
                else if (item == tireColliderSizeSlider) { target = "tirecollider"; currentVal = valTireCollider; }

                if (string.IsNullOrEmpty(target)) return;

                menu.CloseMenu();
                string result = await Utils.GetUserInput("FMMC_KEY_TIP8", currentVal.ToString("G", System.Globalization.CultureInfo.InvariantCulture), 128);

                if (!string.IsNullOrEmpty(result) && float.TryParse(result, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    ApplyTypedValue(target, v);
                    PushState();
                }
                menu.OpenMenu();
            };

            menu.OnItemSelect += (m, item, idx) => {
                if (item == resetItem) { ClearStance(); SyncFromVehicle(currentVeh); return; }
            };
        }

        private float GetScaleForItem(MenuSliderItem item)
        {
            if (item == suspensionSlider) return ScaleSuspension;
            if (item == trackSlider) return ScaleTrack;
            if (item == camberSlider) return ScaleCamber;
            if (item == wheelSizeSlider) return ScaleWheelSize;
            if (item == wheelWidthSlider) return ScaleWheelWidth;
            if (item == tireColliderSizeSlider) return ScaleTireCollider;
            return 1.0f;
        }

        private void ToggleMenu() {
            if (MenuController.IsAnyMenuOpen()) MenuController.CloseAllMenus();
            else if (currentVeh != -1) menu.OpenMenu();
        }

        private async Task OnTick() {
            await BaseScript.Delay(0);
            int ped = PlayerPedId();
            if (IsPedInAnyVehicle(ped, false)) {
                currentVeh = GetVehiclePedIsIn(ped, false);
                if (currentVeh != lastVeh) { SyncFromVehicle(currentVeh); lastVeh = currentVeh; }
                UpdateDescriptions();
                infoItem.Text = $"{GetVehicleMake(currentVeh)} {GetVehicleModel(currentVeh)}";
                infoItem.Description = $"Size: {GetVehicleWheelSize(currentVeh):F2}  Width: {GetVehicleWheelWidth(currentVeh):F2}";
            } else {
                currentVeh = -1;
                if (lastVeh != -1) { lastVeh = -1; MenuController.CloseAllMenus(); }
            }

            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            if (now - lastSyncTime > 1000) {
                SyncAllStances();
                lastSyncTime = now;
            }
        }

        private static string GetVehicleMake(int veh) => GetMakeNameFromVehicleModel((uint)GetEntityModel(veh));
        private static string GetVehicleModel(int veh) => GetDisplayNameFromVehicleModel((uint)GetEntityModel(veh));

        private void ApplyTypedValue(string target, float value) {
            if (currentVeh == -1) return;
            switch (target) {
                case "suspension":   valSuspension = value; suspensionSlider.Position = Utils.Clamp((int)(value * ScaleSuspension), suspensionSlider.Min, suspensionSlider.Max); break;
                case "track":        valTrack = value; trackSlider.Position = Utils.Clamp((int)(value * ScaleTrack), trackSlider.Min, trackSlider.Max); break;
                case "camber":       valCamber = value; camberSlider.Position = Utils.Clamp((int)(value * ScaleCamber), camberSlider.Min, camberSlider.Max); break;
                case "wheelsize":    valWheelSize = value; wheelSizeSlider.Position = Utils.Clamp((int)(value * ScaleWheelSize), wheelSizeSlider.Min, wheelSizeSlider.Max); break;
                case "wheelwidth":   valWheelWidth = value; wheelWidthSlider.Position = Utils.Clamp((int)(value * ScaleWheelWidth), wheelWidthSlider.Min, wheelWidthSlider.Max); break;
                case "tirecollider": valTireCollider = value; tireColliderSizeSlider.Position = Utils.Clamp((int)(value * ScaleTireCollider), tireColliderSizeSlider.Min, tireColliderSizeSlider.Max); break;
            }
        }

        private void PushState() {
            if (currentVeh == -1 || !NetworkGetEntityIsNetworked(currentVeh)) return;
            var state = (StateBag)((Entity)Entity.FromHandle(currentVeh)).State;
            state.Set(StateData, new Dictionary<string, object> {
                ["track"] = valTrack, ["camber"] = valCamber, ["suspension"] = valSuspension,
                ["wheelsize"] = valWheelSize, ["wheelwidth"] = valWheelWidth, ["tirecollider"] = valTireCollider,
                ["wheeltarget"] = (int)CurrentWheelTarget
            }, true);
        }

        private void ClearStance() {
            if (currentVeh == -1) return;
            valSuspension = 0f; valTrack = -GetVehicleWheelXOffset(currentVeh, 0);
            valCamber = GetVehicleWheelYRotation(currentVeh, 0);
            valWheelSize = GetVehicleWheelSize(currentVeh); valWheelWidth = GetVehicleWheelWidth(currentVeh); valTireCollider = 0f;
            wheelTargetList.ListIndex = 0;
            PushState();
            UpdateSliderPositions();
        }

        private void SyncFromVehicle(int veh) {
            var state = (StateBag)((Entity)Entity.FromHandle(veh)).State;
            var data = state.Get(StateData) as IDictionary;
            if (data != null) {
                valSuspension  = Utils.GetSafeFloat(data, "suspension");
                valTrack       = Utils.GetSafeFloat(data, "track");
                valCamber      = Utils.GetSafeFloat(data, "camber");
                valWheelSize   = Utils.GetSafeFloat(data, "wheelsize");
                valWheelWidth  = Utils.GetSafeFloat(data, "wheelwidth");
                valTireCollider= Utils.GetSafeFloat(data, "tirecollider");
                wheelTargetList.ListIndex = Utils.GetSafeInt(data, "wheeltarget");
            } else {
                valSuspension = 0f; valTrack = -GetVehicleWheelXOffset(veh, 0); valCamber = GetVehicleWheelYRotation(veh, 0);
                valWheelSize  = GetVehicleWheelSize(veh); valWheelWidth = GetVehicleWheelWidth(veh); valTireCollider = 0f;
                wheelTargetList.ListIndex = 0;
            }
            UpdateSliderPositions();
        }

        private void UpdateSliderPositions() {
            suspensionSlider.Position       = Utils.Clamp((int)(valSuspension   * ScaleSuspension),   suspensionSlider.Min,       suspensionSlider.Max);
            trackSlider.Position            = Utils.Clamp((int)(valTrack        * ScaleTrack),         trackSlider.Min,            trackSlider.Max);
            camberSlider.Position           = Utils.Clamp((int)(valCamber       * ScaleCamber),        camberSlider.Min,           camberSlider.Max);
            wheelSizeSlider.Position        = Utils.Clamp((int)(valWheelSize    * ScaleWheelSize),     wheelSizeSlider.Min,        wheelSizeSlider.Max);
            wheelWidthSlider.Position       = Utils.Clamp((int)(valWheelWidth   * ScaleWheelWidth),    wheelWidthSlider.Min,       wheelWidthSlider.Max);
            tireColliderSizeSlider.Position = Utils.Clamp((int)(valTireCollider * ScaleTireCollider),  tireColliderSizeSlider.Min, tireColliderSizeSlider.Max);
        }

        private void UpdateDescriptions() {
            suspensionSlider.Description       = $"V: {valSuspension:F4}  |  Enter to type";
            trackSlider.Description            = $"V: {valTrack:F4}  |  Enter to type";
            camberSlider.Description           = $"V: {valCamber:F4}  |  Enter to type";
            wheelSizeSlider.Description        = $"V: {valWheelSize:F4}  |  Enter to type";
            wheelWidthSlider.Description       = $"V: {valWheelWidth:F4}  |  Enter to type";
            tireColliderSizeSlider.Description = $"V: {valTireCollider:F4}  |  Enter to type";
        }

        private void SetRealValue(MenuSliderItem s, float v) {
            if      (s == suspensionSlider)       valSuspension   = v;
            else if (s == trackSlider)            valTrack        = v;
            else if (s == camberSlider)           valCamber       = v;
            else if (s == wheelSizeSlider)        valWheelSize    = v;
            else if (s == wheelWidthSlider)       valWheelWidth   = v;
            else if (s == tireColliderSizeSlider) valTireCollider = v;
        }

        private void SyncAllStances() {
            foreach (var veh in World.GetAllVehicles()) {
                if (!DoesEntityExist(veh.Handle)) continue;
                
                var state = (StateBag)((Entity)veh).State;
                object dataObj = state.Get(StateData);
                var data = dataObj as IDictionary;
                
                if (data == null) {
                    if (appliedStances.ContainsKey(veh.Handle)) appliedStances.Remove(veh.Handle);
                    continue;
                }

                StanceData currentStance = new StanceData {
                    Track = Utils.GetSafeFloat(data, "track"),
                    Camber = Utils.GetSafeFloat(data, "camber"),
                    Suspension = Utils.GetSafeFloat(data, "suspension"),
                    WheelSize = Utils.GetSafeFloat(data, "wheelsize"),
                    WheelWidth = Utils.GetSafeFloat(data, "wheelwidth"),
                    TireCollider = Utils.GetSafeFloat(data, "tirecollider"),
                    Target = Utils.GetSafeInt(data, "wheeltarget")
                };

                if (!appliedStances.TryGetValue(veh.Handle, out StanceData applied) || !applied.Equals(currentStance)) {
                    ApplyStance(veh.Handle, currentStance);
                    appliedStances[veh.Handle] = currentStance;
                }
            }

            foreach (var handle in appliedStances.Keys.ToList()) {
                if (!DoesEntityExist(handle)) appliedStances.Remove(handle);
            }
        }

        private void ApplyStance(int veh, StanceData d) {
            SetVehicleSuspensionHeight(veh, d.Suspension);
            SetVehicleWheelSize(veh, d.WheelSize);
            SetVehicleWheelWidth(veh, d.WheelWidth);
            int count = GetVehicleNumberOfWheels(veh);
            for (int i = 0; i < count; i++) {
                if (!IsWheelAffected(i, count, (WheelTarget)d.Target)) continue;
                SetVehicleWheelXOffset(veh, i, (i % 2 == 0) ? -d.Track : d.Track);
                SetVehicleWheelYRotation(veh, i, (i % 2 == 0) ? d.Camber : -d.Camber);
                if (d.TireCollider != 0f) Function.Call((Hash)0xB962D05CUL, veh, i, d.TireCollider);
            }
        }

        private static bool IsWheelAffected(int idx, int total, WheelTarget t) {
            if (t == WheelTarget.Front)  return idx < 2;
            if (t == WheelTarget.Rear)   return idx >= total - 2;
            if (t == WheelTarget.Center) return idx >= 2 && idx <= total - 3;
            return true;
        }
    }
}
