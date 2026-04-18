using MelonLoader;
using HarmonyLib;
using GHPC.Event;
using System.Collections.Generic;
using System;
using GHPC.Event.Interfaces;
using UnityEngine;
using System.Reflection;
using GHPC.UI.Map;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(GHPC_Artillery_Rework.GHPC_Arty_Class), "GHPC Artillery Rework", "1.2.2", "Qwertyryo")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace GHPC_Artillery_Rework
{
    public class GHPC_Arty_Class : MelonMod
    {
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("GHPC Artillery Rework initialized.");
            var harmony = new HarmonyLib.Harmony("GHPC_Artillery_Rework");
            harmony.PatchAll();
        }
    }

    // -------------------------------------------------------------------
    // Store maps a button to its single entry index in
    // CooldownManager._cooldownReferences.
    // -------------------------------------------------------------------
    public static class FiringIdStore
    {
        private static readonly Dictionary<MapIconControlType, int> _store
            = new Dictionary<MapIconControlType, int>();

        public static void Set(MapIconControlType btn, int idx) => _store[btn] = idx;

        public static bool TryGet(MapIconControlType btn, out int idx) =>
            _store.TryGetValue(btn, out idx);

        public static void Remove(MapIconControlType btn) => _store.Remove(btn);
        public static void Clear() => _store.Clear();
    }

    // -------------------------------------------------------------------
    // 1. On UpdateCooldownDelayResponse, find the list entry whose
    //    Updater is this MapIconControlType and remember its index.
    // -------------------------------------------------------------------
    [HarmonyPatch(typeof(MapIconControlType),
                  nameof(MapIconControlType.UpdateCooldownDelayResponse))]
    public static class Patch_MapIconControlType_UpdateCooldownDelayResponse
    {
        private static readonly FieldInfo _refListField =
            AccessTools.Field(typeof(CooldownManager), "_cooldownReferences");

        private static readonly Type _cooldownDataType =
            typeof(CooldownManager).GetNestedType("CooldownData",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _updaterField =
            AccessTools.Field(_cooldownDataType, "Updater");

        static void Postfix(MapIconControlType __instance)
        {
            if (FiringIdStore.TryGet(__instance, out _)) return;

            var mgr = CooldownManager.Instance;
            if (mgr == null) return;

            var list = _refListField.GetValue(mgr) as System.Collections.IList;
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                var updater = _updaterField.GetValue(list[i]) as ICooldownUpdater;
                if (ReferenceEquals(updater, __instance))
                {
                    FiringIdStore.Set(__instance, i);
                    MelonLogger.Msg($"[FiringId] {__instance.SupportName}: stored index={i}");
                    return;
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // 2. Force the button to stay interactable during the delay phase,
    //    overriding the original method which sets it to false.
    // -------------------------------------------------------------------
    [HarmonyPatch(typeof(MapIconControlType),
                  nameof(MapIconControlType.UpdateCooldownDelayResponse))]
    public static class Patch_MapIconControlType_KeepInteractable
    {
        private static readonly FieldInfo _buttonField =
            AccessTools.Field(typeof(MapIconControlType), "_button");

        static void Postfix(MapIconControlType __instance)
        {
            var button = _buttonField.GetValue(__instance) as Button;
            if (button != null)
                button.interactable = true;
        }
    }

    // -------------------------------------------------------------------
    // 3. On click, reach the Reporter (ArtilleryBattery) and zero out
    //    its auto-property backing fields for RemainingDelay,
    //    RemainingCooldown, and TimeUntilImpactSeconds.
    // -------------------------------------------------------------------
    [HarmonyPatch(typeof(MapFireSupportPanel),
              nameof(MapFireSupportPanel.OnClickSupportButton))]
    public static class Patch_MapFireSupportPanel_OnClickSupportButton
    {
        private static readonly FieldInfo _refListField =
            AccessTools.Field(typeof(CooldownManager), "_cooldownReferences");

        private static readonly Type _cooldownDataType =
            typeof(CooldownManager).GetNestedType("CooldownData",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo _reporterField =
            AccessTools.Field(_cooldownDataType, "Reporter");

        // Resolved lazily once we've seen a reporter
        private static Type     _cachedReporterType;
        private static FieldInfo _remainingCooldownBF;
        private static FieldInfo _remainingDelayBF;
        private static FieldInfo _timeUntilImpactBF;
        private static FieldInfo _isFiringBF;
        private static FieldInfo _shotCounterF;
        private static FieldInfo _currentShotQuotaF;
        private static FieldInfo _interShotTimerF;
        private static FieldInfo _cooldownSecondsF;

        private static void EnsureReporterFields(Type t)
        {
            if (_cachedReporterType == t) return;
            _cachedReporterType    = t;
            _remainingCooldownBF   = AccessTools.Field(t, "<RemainingCooldown>k__BackingField");
            _remainingDelayBF      = AccessTools.Field(t, "<RemainingDelay>k__BackingField");
            _timeUntilImpactBF     = AccessTools.Field(t, "<TimeUntilImpactSeconds>k__BackingField");
            _isFiringBF            = AccessTools.Field(t, "<IsFiring>k__BackingField");
            _shotCounterF          = AccessTools.Field(t, "_shotCounter");
            _currentShotQuotaF     = AccessTools.Field(t, "_currentShotQuota");
            _interShotTimerF       = AccessTools.Field(t, "_interShotTimer");
            _cooldownSecondsF      = AccessTools.Field(t, "_cooldownSeconds");
        }

        static bool Prefix(MapFireSupportPanel __instance,
                   MapIconControlType fireSupportButton)
{
    if (!FiringIdStore.TryGet(fireSupportButton, out int idx))
        return true;

    var mgr = CooldownManager.Instance;
    if (mgr == null) return true;

    var list = _refListField.GetValue(mgr) as System.Collections.IList;
    if (list == null || idx < 0 || idx >= list.Count) return true;

    object boxed = list[idx];
    var reporter = _reporterField.GetValue(boxed);
    if (reporter == null) return true;

    EnsureReporterFields(reporter.GetType());

    float normalCooldown = _cooldownSecondsF != null
        ? (float)_cooldownSecondsF.GetValue(reporter)
        : 20f;

    // Replicate the natural "mission complete" transition from DoUpdate:
    //   _interShotTimer = 0, _shotCounter = 0, IsFiring = false, RemainingDelay = 0
    // Plus zero TimeUntilImpact and start normal cooldown.
    _isFiringBF?.SetValue(reporter, false);
    _remainingDelayBF?.SetValue(reporter, 0f);
    _timeUntilImpactBF?.SetValue(reporter, 0f);
    _interShotTimerF?.SetValue(reporter, 0f);
    _shotCounterF?.SetValue(reporter, 0);
    _remainingCooldownBF?.SetValue(reporter, normalCooldown);

    // Let the game clean up its own mission bookkeeping if the method exists
    TryCancelCurrentSupport(__instance);

    MelonLogger.Msg($"[FiringId] Cancelled {fireSupportButton.SupportName}: " +
                    $"cooldown reset to {normalCooldown:F1}s");

    FiringIdStore.Remove(fireSupportButton);
    return false;
}
// Reflection helper — CancelCurrentSupport might be public or private
private static MethodInfo _cancelCurrentSupport;
private static void TryCancelCurrentSupport(MapFireSupportPanel panel)
{
    if (_cancelCurrentSupport == null)
    {
        // Try to find the method regardless of access level, and handle
        // the MapMissionResult enum argument via reflection.
        var panelType = typeof(MapFireSupportPanel);
        _cancelCurrentSupport = panelType.GetMethod("CancelCurrentSupport",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
    if (_cancelCurrentSupport == null)
    {
        MelonLogger.Msg("[FiringId] CancelCurrentSupport method not found");
        return;
    }

    // Build the MapMissionResult.Empty argument
    var parameters = _cancelCurrentSupport.GetParameters();
    object[] args;
    if (parameters.Length == 1)
    {
        var paramType = parameters[0].ParameterType;
        // Assume the enum has a value named "Empty"; fall back to default(paramType)
        object emptyVal = Enum.IsDefined(paramType, "Empty")
            ? Enum.Parse(paramType, "Empty")
            : Activator.CreateInstance(paramType);
        args = new object[] { emptyVal };
    }
    else
    {
        args = Array.Empty<object>();
    }

    try
    {
        _cancelCurrentSupport.Invoke(panel, args);
        MelonLogger.Msg("[FiringId] CancelCurrentSupport invoked");
    }
    catch (Exception e)
    {
        MelonLogger.Msg($"[FiringId] CancelCurrentSupport threw: {e.InnerException?.Message ?? e.Message}");
    }
}
        }
}