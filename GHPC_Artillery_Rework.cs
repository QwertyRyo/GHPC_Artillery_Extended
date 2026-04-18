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
    // Store maps a button -> the Reporter (ArtilleryBattery) directly.
    // Storing the reporter object avoids the fragility of list indices,
    // which shift when entries are removed (e.g. when another battery
    // finishes its cooldown while this one is still mid-flight).
    // -------------------------------------------------------------------
    public static class FiringIdStore
    {
        private static readonly Dictionary<MapIconControlType, object> _store
            = new Dictionary<MapIconControlType, object>();

        public static void Set(MapIconControlType btn, object reporter) =>
            _store[btn] = reporter;

        public static bool TryGet(MapIconControlType btn, out object reporter) =>
            _store.TryGetValue(btn, out reporter);

        public static void Remove(MapIconControlType btn) => _store.Remove(btn);
        public static void Clear() => _store.Clear();
    }

    // -------------------------------------------------------------------
    // 1. On UpdateCooldownDelayResponse, look up the list entry bound to
    //    this button and capture its Reporter object for later.
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

        private static readonly FieldInfo _reporterField =
            AccessTools.Field(_cooldownDataType, "Reporter");

        static void Postfix(MapIconControlType __instance)
        {
            if (FiringIdStore.TryGet(__instance, out _)) return;

            var mgr = CooldownManager.Instance;
            if (mgr == null) return;

            var list = _refListField.GetValue(mgr) as System.Collections.IList;
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                object entry = list[i];
                var updater = _updaterField.GetValue(entry) as ICooldownUpdater;
                if (!ReferenceEquals(updater, __instance)) continue;

                var reporter = _reporterField.GetValue(entry);
                if (reporter == null) continue;

                FiringIdStore.Set(__instance, reporter);
                MelonLogger.Msg($"[FiringId] {__instance.SupportName} " +
                                $"(btn#{__instance.GetInstanceID()}): " +
                                $"captured reporter {reporter.GetType().Name}" +
                                $"#{reporter.GetHashCode():X8} at idx={i}, " +
                                $"listSize={list.Count}");
                return;
            }

            MelonLogger.Msg($"[FiringId-warn] {__instance.SupportName}: " +
                            $"no matching entry in list of size {list.Count}");
        }
    }

    // -------------------------------------------------------------------
    // 2. Force the button to stay interactable during the delay phase.
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
    // 3. On click, read the stored Reporter and cancel that specific
    //    barrage. Using the Reporter directly (rather than a list index)
    //    is safe regardless of what other batteries are doing.
    // -------------------------------------------------------------------
    [HarmonyPatch(typeof(MapFireSupportPanel),
                  nameof(MapFireSupportPanel.OnClickSupportButton))]
    public static class Patch_MapFireSupportPanel_OnClickSupportButton
    {
        // Reporter field cache (populated lazily)
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

        // Per-button debounce to absorb the game's duplicate OnClickSupportButton
        // fire within ~200ms on the same button. Doesn't block clicks on OTHER buttons.
        private static float _lastCancelTime = -10f;
        private static MapIconControlType _lastCancelButton;

        private static MethodInfo _cancelCurrentSupport;

        static bool Prefix(MapFireSupportPanel __instance,
                           MapIconControlType fireSupportButton)
        {
            // Debounce: ignore duplicate click on the SAME button within 200ms.
            if (ReferenceEquals(_lastCancelButton, fireSupportButton) &&
                Time.unscaledTime - _lastCancelTime < 0.2f)
            {
                MelonLogger.Msg($"[FiringId-debounce] " +
                                $"{fireSupportButton.SupportName}: " +
                                $"ignoring duplicate click, skipping original");
                return false;
            }

            // No stored Reporter -> this is a normal (non-cancel) click.
            // Let the original method run to open the deploy menu.
            if (!FiringIdStore.TryGet(fireSupportButton, out object reporter) ||
                reporter == null)
            {
                return true;
            }

            EnsureReporterFields(reporter.GetType());

            // Pre-mutation diagnostic
            float preDelay     = (float)(_remainingDelayBF?.GetValue(reporter) ?? 0f);
            float preCooldown  = (float)(_remainingCooldownBF?.GetValue(reporter) ?? 0f);
            float preImpact    = (float)(_timeUntilImpactBF?.GetValue(reporter) ?? 0f);
            bool  preFiring    = (bool) (_isFiringBF?.GetValue(reporter) ?? false);
            int   preShotCount = (int)  (_shotCounterF?.GetValue(reporter) ?? 0);
            int   preQuota     = (int)  (_currentShotQuotaF?.GetValue(reporter) ?? 0);
            MelonLogger.Msg($"[FiringId-pre] {fireSupportButton.SupportName} " +
                            $"reporter#{reporter.GetHashCode():X8}: " +
                            $"IsFiring={preFiring}, " +
                            $"RemDelay={preDelay:F2}, RemCd={preCooldown:F2}, " +
                            $"TUI={preImpact:F2}, shots={preShotCount}/{preQuota}");

            float normalCooldown = _cooldownSecondsF != null
                ? (float)_cooldownSecondsF.GetValue(reporter)
                : 20f;

            // Replicate the natural "mission complete" branch from DoUpdate:
            //   _interShotTimer = 0, _shotCounter = 0, IsFiring = false, RemainingDelay = 0
            // Plus zero TimeUntilImpact and set RemainingCooldown to the normal value.
            _isFiringBF?.SetValue(reporter, false);
            _remainingDelayBF?.SetValue(reporter, 0f);
            _timeUntilImpactBF?.SetValue(reporter, 0f);
            _interShotTimerF?.SetValue(reporter, 0f);
            _shotCounterF?.SetValue(reporter, 0);
            _remainingCooldownBF?.SetValue(reporter, normalCooldown);

            TryCancelCurrentSupport(__instance);

            MelonLogger.Msg($"[FiringId-post] {fireSupportButton.SupportName}: " +
                            $"cancelled, cooldown={normalCooldown:F1}s");

            FiringIdStore.Remove(fireSupportButton);
            _lastCancelTime = Time.unscaledTime;
            _lastCancelButton = fireSupportButton;
            return false;  // skip original -> no aim reticle
        }

        private static void TryCancelCurrentSupport(MapFireSupportPanel panel)
        {
            if (_cancelCurrentSupport == null)
            {
                _cancelCurrentSupport = typeof(MapFireSupportPanel).GetMethod(
                    "CancelCurrentSupport",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_cancelCurrentSupport == null)
            {
                MelonLogger.Msg("[FiringId-warn] CancelCurrentSupport method not found");
                return;
            }

            var parameters = _cancelCurrentSupport.GetParameters();
            object[] args;
            if (parameters.Length == 1)
            {
                var paramType = parameters[0].ParameterType;
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
            }
            catch (Exception e)
            {
                MelonLogger.Msg($"[FiringId-warn] CancelCurrentSupport threw: " +
                                $"{e.InnerException?.Message ?? e.Message}");
            }
        }
    }
}