using MelonLoader;
using HarmonyLib;
using System.Collections;
using System.IO;
using GHPC.Event;
using System.Collections.Generic;
using System;
using GHPC.Event.Interfaces;
using UnityEngine;
using System.Reflection;
using GHPC.UI.Map;
using UnityEngine.UI;
using GHPC.Weapons.Artillery;
using GHPC.Weaponry.Artillery;
using GHPC.Weaponry;
using GHPC.Weapons;
[assembly:MelonInfo(typeof(GHPC_Artillery_Rework.GHPC_Arty_Class),
                    "GHPC Artillery Rework", "1.0.0", "Qwertyryo")]
[assembly:MelonGame("Radian Simulations LLC", "GHPC")]

namespace GHPC_Artillery_Rework {
  public class GHPC_Arty_Class : MelonMod {
    public static MelonPreferences_Entry<int> volume;
    public static MelonPreferences_Entry<int> timeToTargetMultipler;
    public static MelonPreferences_Entry<int> accuracyMultiplier;

    public override void OnInitializeMelon() {
      MelonLogger.Msg("GHPC Artillery Rework initialized.");
      MelonPreferences_Category cfg =
          MelonPreferences.CreateCategory("GHPC Artillery Rework");

      volume = cfg.CreateEntry<int>("Artillery volume multiplier", 1);
      volume.Comment =
          "Set the volume multiplier for artillery rounds. 1 is default, 2 is double the amount of shells, etc. Supports integers from 1 to 15, but not great for your PC past 5.";

      timeToTargetMultipler =
          cfg.CreateEntry<int>("Time to target multiplier", 1);
      timeToTargetMultipler.Comment =
          "Set the multiplier for speed time to target. 1 is default, 2 means shells will hit the target in half the time, etc. Supports integers from 1 to 10.";

      accuracyMultiplier = cfg.CreateEntry<int>("Accuracy multiplier", 1);
      accuracyMultiplier.Comment =
          "Set the multiplier for artillery accuracy. 1 is default, 2 means shells will be twice as accurate, etc. Supports integers from 1 to 10.";
      var harmony = new HarmonyLib.Harmony("GHPC_Artillery_Rework");
      harmony.PatchAll();
    }
    public static int Clamped(MelonPreferences_Entry<int> entry, int min,
                              int max, int fallback = 1) {
      if (entry == null)
        return fallback;
      int v = entry.Value;
      return (v < min || v > max) ? fallback : v;
    }
  }

  public static class FiringIdStore {
    private static readonly Dictionary<MapIconControlType, object> _store =
        new Dictionary<MapIconControlType, object>();

    public static void Set(MapIconControlType btn,
                           object reporter) => _store[btn] = reporter;

    public static bool TryGet(MapIconControlType btn, out object reporter) =>
        _store.TryGetValue(btn, out reporter);

    public static void Remove(MapIconControlType btn) => _store.Remove(btn);
    public static void Clear() => _store.Clear();
  }

  [HarmonyPatch(typeof(MapIconControlType),
                nameof(MapIconControlType.UpdateCooldownDelayResponse))]
  public static class Patch_MapIconControlType_UpdateCooldownDelayResponse {
    private static readonly FieldInfo _refListField =
        AccessTools.Field(typeof(CooldownManager), "_cooldownReferences");

    private static readonly Type _cooldownDataType =
        typeof(CooldownManager)
            .GetNestedType("CooldownData",
                           BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo _updaterField =
        AccessTools.Field(_cooldownDataType, "Updater");

    private static readonly FieldInfo _reporterField =
        AccessTools.Field(_cooldownDataType, "Reporter");

    static void Postfix(MapIconControlType __instance) {
      if (FiringIdStore.TryGet(__instance, out _))
        return;

      var mgr = CooldownManager.Instance;
      if (mgr == null)
        return;

      var list = _refListField.GetValue(mgr) as System.Collections.IList;
      if (list == null)
        return;

      for (int i = 0; i < list.Count; i++) {
        object entry = list[i];
        var updater = _updaterField.GetValue(entry) as ICooldownUpdater;
        if (!ReferenceEquals(updater, __instance))
          continue;

        var reporter = _reporterField.GetValue(entry);
        if (reporter == null)
          continue;

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

  [HarmonyPatch(typeof(MapIconControlType),
                nameof(MapIconControlType.UpdateCooldownDelayResponse))]
  public static class Patch_MapIconControlType_KeepInteractable {
    private static readonly FieldInfo _buttonField =
        AccessTools.Field(typeof(MapIconControlType), "_button");

    static void Postfix(MapIconControlType __instance) {
      var button = _buttonField.GetValue(__instance) as Button;
      if (button != null)
        button.interactable = true;
    }
  }
  [HarmonyPatch(typeof(MapFireSupportPanel),
                nameof(MapFireSupportPanel.OnClickSupportButton))]
  public static class Patch_MapFireSupportPanel_OnClickSupportButton {
    // Reporter field cache (populated lazily)
    private static Type _cachedReporterType;
    private static FieldInfo _remainingCooldownBF;
    private static FieldInfo _remainingDelayBF;
    private static FieldInfo _timeUntilImpactBF;
    private static FieldInfo _isFiringBF;
    private static FieldInfo _shotCounterF;
    private static FieldInfo _currentShotQuotaF;
    private static FieldInfo _interShotTimerF;
    private static FieldInfo _cooldownSecondsF;

    private static void EnsureReporterFields(Type t) {
      if (_cachedReporterType == t)
        return;
      _cachedReporterType = t;
      _remainingCooldownBF =
          AccessTools.Field(t, "<RemainingCooldown>k__BackingField");
      _remainingDelayBF =
          AccessTools.Field(t, "<RemainingDelay>k__BackingField");
      _timeUntilImpactBF =
          AccessTools.Field(t, "<TimeUntilImpactSeconds>k__BackingField");
      _isFiringBF = AccessTools.Field(t, "<IsFiring>k__BackingField");
      _shotCounterF = AccessTools.Field(t, "_shotCounter");
      _currentShotQuotaF = AccessTools.Field(t, "_currentShotQuota");
      _interShotTimerF = AccessTools.Field(t, "_interShotTimer");
      _cooldownSecondsF = AccessTools.Field(t, "_cooldownSeconds");
    }

    private static float _lastCancelTime = -10f;
    private static MapIconControlType _lastCancelButton;

    private static MethodInfo _cancelCurrentSupport;

    static bool Prefix(MapFireSupportPanel __instance,
                       MapIconControlType fireSupportButton) {
      if (ReferenceEquals(_lastCancelButton, fireSupportButton) &&
          Time.unscaledTime - _lastCancelTime < 0.2f) {
        MelonLogger.Msg($"[FiringId-debounce] " +
                        $"{fireSupportButton.SupportName}: " +
                        $"ignoring duplicate click, skipping original");
        return false;
      }

      if (!FiringIdStore.TryGet(fireSupportButton, out object reporter) ||
          reporter == null) {
        return true;
      }

      EnsureReporterFields(reporter.GetType());
      // Guard: only cancel if the battery is actually mid-mission.

      bool curFiring = (bool)(_isFiringBF?.GetValue(reporter) ?? false);
      float curDelay = (float)(_remainingDelayBF?.GetValue(reporter) ?? 0f);
      float curImpact = (float)(_timeUntilImpactBF?.GetValue(reporter) ?? 0f);

      if (!curFiring && curDelay <= 0f && curImpact <= 0f) {
        MelonLogger.Msg($"[FiringId] {fireSupportButton.SupportName}: " +
                        $"stale store entry (battery idle), running original");
        FiringIdStore.Remove(fireSupportButton);
        return true;
      }

      /* float preDelay = (float)(_remainingDelayBF?.GetValue(reporter) ?? 0f);
       float preCooldown =
           (float)(_remainingCooldownBF?.GetValue(reporter) ?? 0f);
       float preImpact = (float)(_timeUntilImpactBF?.GetValue(reporter) ?? 0f);
       bool preFiring = (bool)(_isFiringBF?.GetValue(reporter) ?? false);
       int preShotCount = (int)(_shotCounterF?.GetValue(reporter) ?? 0);
       int preQuota = (int)(_currentShotQuotaF?.GetValue(reporter) ?? 0);
       MelonLogger.Msg($"[FiringId-pre] {fireSupportButton.SupportName} " +
                       $"reporter#{reporter.GetHashCode():X8}: " +
                       $"IsFiring={preFiring}, " +
                       $"RemDelay={preDelay:F2}, RemCd={preCooldown:F2}, " +
                       $"TUI={preImpact:F2}, shots={preShotCount}/{preQuota}");
 */
      float normalCooldown = _cooldownSecondsF != null
                                 ? (float)_cooldownSecondsF.GetValue(reporter)
                                 : 20f;

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
      return false;
    }

    private static void TryCancelCurrentSupport(MapFireSupportPanel panel) {
      if (_cancelCurrentSupport == null) {
        _cancelCurrentSupport =
            typeof(MapFireSupportPanel)
                .GetMethod("CancelCurrentSupport", BindingFlags.Instance |
                                                       BindingFlags.Public |
                                                       BindingFlags.NonPublic);
      }
      if (_cancelCurrentSupport == null) {
        MelonLogger.Msg(
            "[FiringId-warn] CancelCurrentSupport method not found");
        return;
      }

      var parameters = _cancelCurrentSupport.GetParameters();
      object[] args;
      if (parameters.Length == 1) {
        var paramType = parameters[0].ParameterType;
        object emptyVal = Enum.IsDefined(paramType, "Empty")
                              ? Enum.Parse(paramType, "Empty")
                              : Activator.CreateInstance(paramType);
        args = new object[] { emptyVal };
      } else {
        args = Array.Empty<object>();
      }

      try {
        _cancelCurrentSupport.Invoke(panel, args);
      } catch (Exception e) {
        MelonLogger.Msg($"[FiringId-warn] CancelCurrentSupport threw: " +
                        $"{e.InnerException?.Message ?? e.Message}");
      }
    }
  }
  [HarmonyPatch]
  public static class Patch_ArtilleryBattery_SendFireMission {
    static MethodBase TargetMethod() {
      var type =
          AccessTools.TypeByName("GHPC.Weapons.Artillery.ArtilleryBattery");
      return AccessTools.Method(type, "SendFireMission");
    }

    private static Type _alertHudType;
    private static PropertyInfo _instanceProp;
    private static MethodInfo _addAlertMethod;
    private static bool _resolved;

    private static void ResolveOnce() {
      if (_resolved)
        return;
      _resolved = true;

      _alertHudType = AccessTools.TypeByName("GHPC.UI.Hud.AlertHud");
      if (_alertHudType == null) {
        MelonLogger.Msg("[Alert-warn] AlertHud type not found");
        return;
      }

      _instanceProp = AccessTools.Property(_alertHudType, "Instance");
      _addAlertMethod =
          AccessTools.Method(_alertHudType, "AddAlertMessage",
                             new[] { typeof(string), typeof(float) });

      MelonLogger.Msg($"[Alert] AlertHud resolved: " +
                      $"instanceProp={_instanceProp != null}, " +
                      $"addAlertMethod={_addAlertMethod != null}");
    }

    static void Postfix(bool __result, ArtilleryBattery __instance,
                        IndirectFireMunitionType preferredMunitions) {
      if (!__result)
        return;

      ResolveOnce();
      if (_instanceProp == null || _addAlertMethod == null)
        return;
      var currentMunitions = (BatteryMunitionsChoice)AccessTools.Field(typeof(ArtilleryBattery), "_currentMunitions").GetValue(__instance);
      string shell_type = "HE";
      string random_message = new[] {
            " This is gonna be a big one.",
            " Stand clear.",
            " Better plug those ears.",
            " Clean up anyone who's left after this.",
            " Sit back and watch the fireworks.",
            " Those bitches have no idea what's coming.",
            " Get some, motherfuckers!",
            " Eat shit and die, motherfuckers!"
        }[UnityEngine.Random.Range(0, 7)];
      if (currentMunitions.Ammo?.AmmoType == null) {
        shell_type = "WP";
        random_message = "";
      }
      var hud = _instanceProp.GetValue(null);
      if (hud == null)
        return;
      var t = __instance.GetType();
      int shots = (int)(AccessTools.Field(t, "_currentShotQuota")
                            ?.GetValue(__instance) ??
                        0);
      float impact = (float)(AccessTools.Property(t, "TimeUntilImpactSeconds")
                                 ?.GetValue(__instance) ??
                             0f);

       
    

      string msg =
          $"Battalion FSO: Executing fire suppression mission — {shots} rounds {shell_type}, {(int)Math.Ceiling(impact)} seconds time to target.{random_message}";
      try {
        _addAlertMethod.Invoke(hud, new object[] { msg, 4f });
        MelonLogger.Msg($"[Alert] Sent: {msg}");
      } catch (Exception e) {
        MelonLogger.Msg($"[Alert-warn] AddAlertMessage threw: " +
                        $"{e.InnerException?.Message ?? e.Message}");
      }
    }
  }
  [HarmonyPatch]
  public static class Patch_ArtilleryBattery_SendFireMission_ApplyMultipliers {
    static MethodBase TargetMethod() {
      var type =
          AccessTools.TypeByName("GHPC.Weapons.Artillery.ArtilleryBattery");
      return AccessTools.Method(type, "SendFireMission");
    }

    private static readonly FieldInfo _shotsF = AccessTools.Field(
        AccessTools.TypeByName("GHPC.Weapons.Artillery.ArtilleryBattery"),
        "_shots");
    private static readonly FieldInfo _interShotDelayF =
        AccessTools.Field(AccessTools.TypeByName("GHPC.Weapons.Artillery.ArtilleryBattery"),
                      "_interShotDelaySeconds");


    private static readonly FieldInfo _dispersionF = AccessTools.Field(
        AccessTools.TypeByName("GHPC.Weapons.Artillery.ArtilleryBattery"),
        "_randomDispersionRadiusMeters");

    static void Prefix(object __instance, ref float delaySeconds,
                       ref int roundCount, ref float radiusMeters, ref float secondsBetweenRounds) {
      int volMult = GHPC_Arty_Class.Clamped(GHPC_Arty_Class.volume, 1, 15);
      int ttMult =
          GHPC_Arty_Class.Clamped(GHPC_Arty_Class.timeToTargetMultipler, 1, 10);
      int accMult =
          GHPC_Arty_Class.Clamped(GHPC_Arty_Class.accuracyMultiplier, 1, 10);

      if (roundCount < 0)
        roundCount = (int)_shotsF.GetValue(__instance);
      roundCount *= volMult;

      if (secondsBetweenRounds < 0f)
        secondsBetweenRounds = (float)_interShotDelayF.GetValue(__instance);
      secondsBetweenRounds /= volMult;

      if (radiusMeters < 0f)
        radiusMeters = (float)_dispersionF.GetValue(__instance);
      radiusMeters /= accMult;

      delaySeconds /= ttMult;

      MelonLogger.Msg($"[ArtyMult] rounds×{volMult}={roundCount}, " +
                      $"delay÷{ttMult}={delaySeconds:F1}s, " +
                      $"radius÷{accMult}={radiusMeters:F1}m");
    }
  }

}