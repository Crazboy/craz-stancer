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
        private float valWheelSize = 1.0f, valWheelWidth = 1.0f, valTireCollider = 0f;
        private bool modSuspension, modTrack, modCamber, modWheelSize, modWheelWidth, modTireCollider;

        // Individual State Bag Keys for reliable cross-client sync
        private const string StateTrack        = "cstancer:track";
        private const string StateCamber       = "cstancer:camber";
        private const string StateSuspension   = "cstancer:suspension";
        private const string StateWheelSize    = "cstancer:wheelsize";
        private const string StateWheelWidth   = "cstancer:wheelwidth";
        private const string StateTireCollider = "cstancer:tirecollider";
        private const string StateTarget       = "cstancer:target";
        private const string StateInitialized  = "cstancer:init";

        private const float ScaleSuspension   = 100f;
        private const float ScaleTrack        = 100f;
        private const float ScaleCamber       = 100f;
        private const float ScaleWheelSize    = 100f;
        private const float ScaleWheelWidth   = 100f;
        private const float ScaleTireCollider = 100f;

        private long lastSyncTime = 0;
        private Dictionary<int, StanceData> stanceCache = new Dictionary<int, StanceData>();

        private struct StanceData
        {
            public float? Track;
            public float? Camber;
            public float? Suspension;
            public float? WheelSize;
            public float? WheelWidth;
            public float? TireCollider;
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
            
            RegisterCommand("cstancer", new Action<int, List<object>, string>((s, a, r) => ToggleMenu()), true);
            RegisterKeyMapping("cstancer", "Open CStancer Menu", "keyboard", "");

            RegisterCommand("checkstance", new Action<int, List<object>, string>((s, a, r) => {
                int veh = GetVehiclePedIsIn(PlayerPedId(), false);
                if (veh == 0) return;
                var state = (StateBag)((Entity)Entity.FromHandle(veh)).State;
                object data = state.Get(StateInitialized);
                Debug.WriteLine($"[CStancer] State Init for {veh}: {(data != null ? data.ToString() : "NULL")}");
            }), false);

            Tick += OnTick;
            Tick += OnDrawTick;
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

            menu.OnListIndexChange += (m, item, oldIdx, newIdx, itemIdx) => {
                if (item == wheelTargetList) PushState();
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
            int veh = GetVehiclePedIsIn(ped, false);

            // ONLY the driver is allowed to "own" the vehicle's stance menu and local state
            if (veh != 0 && GetPedInVehicleSeat(veh, -1) == ped) {
                currentVeh = veh;
                if (currentVeh != lastVeh) { SyncFromVehicle(currentVeh); lastVeh = currentVeh; }
                UpdateDescriptions();
            } else {
                currentVeh = -1;
                if (lastVeh != -1) { lastVeh = -1; MenuController.CloseAllMenus(); }
            }

            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            if (now - lastSyncTime > 1000) {
                RefreshStanceCache();
                lastSyncTime = now;
            }
        }

        private async Task OnDrawTick()
        {
            // Apply all cached stances from other players (including if we are a passenger)
            foreach (var kvp in stanceCache.ToList()) {
                if (DoesEntityExist(kvp.Key)) ApplyStance(kvp.Key, kvp.Value);
                else stanceCache.Remove(kvp.Key);
            }

            // Apply local stance immediately if we are the driver
            if (currentVeh != -1) {
                var state = (StateBag)((Entity)Entity.FromHandle(currentVeh)).State;
                if (Utils.GetStateBool(state, StateInitialized)) {
                    ApplyStance(currentVeh, new StanceData {
                        Track = modTrack ? (float?)valTrack : null,
                        Camber = modCamber ? (float?)valCamber : null,
                        Suspension = modSuspension ? (float?)valSuspension : null,
                        WheelSize = modWheelSize ? (float?)valWheelSize : null,
                        WheelWidth = modWheelWidth ? (float?)valWheelWidth : null,
                        TireCollider = modTireCollider ? (float?)valTireCollider : null,
                        Target = (int)CurrentWheelTarget
                    });
                }
            }
        }

        private static string GetVehicleMake(int veh) => GetMakeNameFromVehicleModel((uint)GetEntityModel(veh));
        private static string GetVehicleModel(int veh) => GetDisplayNameFromVehicleModel((uint)GetEntityModel(veh));

        private void ApplyTypedValue(string target, float value) {
            if (currentVeh == -1) return;
            switch (target) {
                case "suspension":   valSuspension = value; modSuspension = true; suspensionSlider.Position = Utils.Clamp((int)(value * ScaleSuspension), suspensionSlider.Min, suspensionSlider.Max); break;
                case "track":        valTrack = value; modTrack = true; trackSlider.Position = Utils.Clamp((int)(value * ScaleTrack), trackSlider.Min, trackSlider.Max); break;
                case "camber":       valCamber = value; modCamber = true; camberSlider.Position = Utils.Clamp((int)(value * ScaleCamber), camberSlider.Min, camberSlider.Max); break;
                case "wheelsize":    valWheelSize = value; modWheelSize = true; wheelSizeSlider.Position = Utils.Clamp((int)(value * ScaleWheelSize), wheelSizeSlider.Min, wheelSizeSlider.Max); break;
                case "wheelwidth":   valWheelWidth = value; modWheelWidth = true; wheelWidthSlider.Position = Utils.Clamp((int)(value * ScaleWheelWidth), wheelWidthSlider.Min, wheelWidthSlider.Max); break;
                case "tirecollider": valTireCollider = value; modTireCollider = true; tireColliderSizeSlider.Position = Utils.Clamp((int)(value * ScaleTireCollider), tireColliderSizeSlider.Min, tireColliderSizeSlider.Max); break;
            }
        }

        private void PushState() {
            if (currentVeh == -1 || !NetworkGetEntityIsNetworked(currentVeh)) return;
            
            var state = (StateBag)((Entity)Entity.FromHandle(currentVeh)).State;
            if (modTrack)        state.Set(StateTrack, valTrack, true);
            if (modCamber)       state.Set(StateCamber, valCamber, true);
            if (modSuspension)   state.Set(StateSuspension, valSuspension, true);
            if (modWheelSize)    state.Set(StateWheelSize, valWheelSize, true);
            if (modWheelWidth)   state.Set(StateWheelWidth, valWheelWidth, true);
            if (modTireCollider) state.Set(StateTireCollider, valTireCollider, true);
            
            state.Set(StateTarget, (int)CurrentWheelTarget, true);
            if (modTrack || modCamber || modSuspension || modWheelSize || modWheelWidth || modTireCollider)
                state.Set(StateInitialized, true, true);
        }

        private void ClearStance() {
            if (currentVeh == -1 || !NetworkGetEntityIsNetworked(currentVeh)) return;
            
            var state = (StateBag)((Entity)Entity.FromHandle(currentVeh)).State;
            state.Set(StateInitialized, false, true);
            modSuspension = modTrack = modCamber = modWheelSize = modWheelWidth = modTireCollider = false;

            valSuspension = 0f; valTrack = -GetVehicleWheelXOffset(currentVeh, 0);
            valCamber = GetVehicleWheelYRotation(currentVeh, 0);
            valWheelSize = GetVehicleWheelSize(currentVeh); if (valWheelSize < 0.1f) valWheelSize = 1.0f;
            valWheelWidth = GetVehicleWheelWidth(currentVeh); if (valWheelWidth < 0.1f) valWheelWidth = 1.0f;
            valTireCollider = Function.Call<float>((Hash)0xB962D05CUL, currentVeh, 0);
            wheelTargetList.ListIndex = 0;
            UpdateSliderPositions();
        }

        private void SyncFromVehicle(int veh) {
            var state = (StateBag)((Entity)Entity.FromHandle(veh)).State;
            if (Utils.GetStateBool(state, StateInitialized)) {
                float? t = Utils.GetStateFloatNullable(state, StateTrack);
                float? c = Utils.GetStateFloatNullable(state, StateCamber);
                float? s = Utils.GetStateFloatNullable(state, StateSuspension);
                float? ws = Utils.GetStateFloatNullable(state, StateWheelSize);
                float? ww = Utils.GetStateFloatNullable(state, StateWheelWidth);
                float? tc = Utils.GetStateFloatNullable(state, StateTireCollider);

                if (t.HasValue)  { valTrack = t.Value; modTrack = true; }
                if (c.HasValue)  { valCamber = c.Value; modCamber = true; }
                if (s.HasValue)  { valSuspension = s.Value; modSuspension = true; }
                if (ws.HasValue) { valWheelSize = ws.Value; modWheelSize = true; }
                if (ww.HasValue) { valWheelWidth = ww.Value; modWheelWidth = true; }
                if (tc.HasValue) { valTireCollider = tc.Value; modTireCollider = true; }

                wheelTargetList.ListIndex = Utils.GetStateInt(state, StateTarget);
            } else {
                modSuspension = modTrack = modCamber = modWheelSize = modWheelWidth = modTireCollider = false;
                valSuspension = 0f; valTrack = -GetVehicleWheelXOffset(veh, 0); valCamber = GetVehicleWheelYRotation(veh, 0);
                valWheelSize  = GetVehicleWheelSize(veh); if (valWheelSize < 0.1f) valWheelSize = 1.0f;
                valWheelWidth = GetVehicleWheelWidth(veh); if (valWheelWidth < 0.1f) valWheelWidth = 1.0f;
                valTireCollider = Function.Call<float>((Hash)0xB962D05CUL, veh, 0);
                wheelTargetList.ListIndex = 0;
            }
            UpdateSliderPositions();
            UpdateDescriptions();
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
            
            if (currentVeh != -1) {
                infoItem.Text = $"{GetMakeNameFromVehicleModel((uint)GetEntityModel(currentVeh))} {GetDisplayNameFromVehicleModel((uint)GetEntityModel(currentVeh))}";
                infoItem.Description = $"T:{valTrack:F2} C:{valCamber:F2} S:{valSuspension:F2} WS:{valWheelSize:F2} WW:{valWheelWidth:F2} TC:{valTireCollider:F2}";
            }
        }

        private void SetRealValue(MenuSliderItem s, float v) {
            if      (s == suspensionSlider)       { valSuspension   = v; modSuspension = true; }
            else if (s == trackSlider)            { valTrack        = v; modTrack = true; }
            else if (s == camberSlider)           { valCamber       = v; modCamber = true; }
            else if (s == wheelSizeSlider)        { valWheelSize    = v; modWheelSize = true; }
            else if (s == wheelWidthSlider)       { valWheelWidth   = v; modWheelWidth = true; }
            else if (s == tireColliderSizeSlider) { valTireCollider = v; modTireCollider = true; }
        }

        private void RefreshStanceCache() {
            // Include our current vehicle if we are a passenger, but skip if we are the driver (handled by local state)
            foreach (var veh in World.GetAllVehicles()) {
                int handle = veh.Handle;
                if (!DoesEntityExist(handle) || handle == currentVeh) continue;
                
                var state = (StateBag)((Entity)veh).State;
                if (!Utils.GetStateBool(state, StateInitialized)) {
                    if (stanceCache.ContainsKey(handle)) stanceCache.Remove(handle);
                    continue;
                }

                stanceCache[handle] = new StanceData {
                    Track = Utils.GetStateFloatNullable(state, StateTrack),
                    Camber = Utils.GetStateFloatNullable(state, StateCamber),
                    Suspension = Utils.GetStateFloatNullable(state, StateSuspension),
                    WheelSize = Utils.GetStateFloatNullable(state, StateWheelSize),
                    WheelWidth = Utils.GetStateFloatNullable(state, StateWheelWidth),
                    TireCollider = Utils.GetStateFloatNullable(state, StateTireCollider),
                    Target = Utils.GetStateInt(state, StateTarget)
                };
            }
        }

        private void ApplyStance(int veh, StanceData d) {
            if (d.Suspension.HasValue) SetVehicleSuspensionHeight(veh, d.Suspension.Value);
            if (d.WheelSize.HasValue && d.WheelSize.Value > 0.01f) SetVehicleWheelSize(veh, d.WheelSize.Value);
            if (d.WheelWidth.HasValue && d.WheelWidth.Value > 0.01f) SetVehicleWheelWidth(veh, d.WheelWidth.Value);
            
            int count = GetVehicleNumberOfWheels(veh);
            for (int i = 0; i < count; i++) {
                if (!IsWheelAffected(i, count, (WheelTarget)d.Target)) continue;
                if (d.Track.HasValue) SetVehicleWheelXOffset(veh, i, (i % 2 == 0) ? -d.Track.Value : d.Track.Value);
                if (d.Camber.HasValue) SetVehicleWheelYRotation(veh, i, (i % 2 == 0) ? d.Camber.Value : -d.Camber.Value);
                if (d.TireCollider.HasValue && d.TireCollider.Value != 0f) Function.Call((Hash)0xB962D05CUL, veh, i, d.TireCollider.Value);
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
