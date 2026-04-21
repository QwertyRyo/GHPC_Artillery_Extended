using MelonLoader;
using HarmonyLib;
using System.Collections;
using System.IO;
using GHPC.Event;
using System.Collections.Generic;
using System;
using GHPC;
using GHPC.Mission;
using GHPC.Player;
using GHPC.Event.Interfaces;
using UnityEngine;
using System.Reflection;
using GHPC.UI.Map;
using UnityEngine.UI;
using GHPC.Weapons.Artillery;
using GHPC.Weaponry.Artillery;
using GHPC.Weaponry;
using GHPC.Weapons;
using GHPC.Campaign.Data;
using GHPC.Mission.Data.Campaign;
[assembly:MelonInfo(typeof(GHPC_Artillery_Rework.GHPC_Arty_Class),
                    "GHPC Artillery Rework", "1.1.0", "Qwertyryo")]
[assembly:MelonGame("Radian Simulations LLC", "GHPC")]

namespace GHPC_Artillery_Rework {
  public class GHPC_Arty_Class : MelonMod {
    // HE — OnCall
    public static MelonPreferences_Entry<float> heVolume;
    public static MelonPreferences_Entry<float> heTimeToTarget;
    public static MelonPreferences_Entry<float> heAccuracy;
    // HE — Planned
    public static MelonPreferences_Entry<float> hePlannedVolume;
    public static MelonPreferences_Entry<float> hePlannedTimeToTarget;
    public static MelonPreferences_Entry<float> hePlannedAccuracy;
    // Smoke — OnCall
    public static MelonPreferences_Entry<float> smokeVolume;
    public static MelonPreferences_Entry<float> smokeTimeToTarget;
    public static MelonPreferences_Entry<float> smokeAccuracy;
    // Smoke — Planned
    public static MelonPreferences_Entry<float> smokePlannedVolume;
    public static MelonPreferences_Entry<float> smokePlannedTimeToTarget;
    public static MelonPreferences_Entry<float> smokePlannedAccuracy;
    // Illumination — OnCall
    public static MelonPreferences_Entry<float> illuVolume;
    public static MelonPreferences_Entry<float> illuTimeToTarget;
    public static MelonPreferences_Entry<float> illuAccuracy;
    // Illumination — Planned
    public static MelonPreferences_Entry<float> illuPlannedVolume;
    public static MelonPreferences_Entry<float> illuPlannedTimeToTarget;
    public static MelonPreferences_Entry<float> illuPlannedAccuracy;

    public override void OnInitializeMelon() {
      MelonLogger.Msg("GHPC Artillery Extended initialized.");

      MelonPreferences_Category cfg = MelonPreferences.CreateCategory("GHPC Artillery Extended");
      heVolume          = cfg.CreateEntry<float>("HE OnCall volume multiplier", 1f);
      heVolume.Comment = "There are two types of artillery in GHPC; OnCall, which occurs with player input, and planned, which happens automatically. Volume multiplier increases the amount of shells fired - Set 2.5 for 2.5x the amount of shells.Time-to-target multiplier decreases the time it takes for shells to arrive. Set to 2.0 for half the time-to-target. Accuracy affects the dispersion radius of the shells, set to a higher number for tighter firing patterns.";

      heTimeToTarget    = cfg.CreateEntry<float>("HE OnCall time-to-target multiplier", 1f);
      heAccuracy        = cfg.CreateEntry<float>("HE OnCall accuracy multiplier", 1f);
      hePlannedVolume   = cfg.CreateEntry<float>("HE Planned volume multiplier", 1f);
      hePlannedTimeToTarget = cfg.CreateEntry<float>("HE Planned time-to-target multiplier", 1f);
      hePlannedAccuracy = cfg.CreateEntry<float>("HE Planned accuracy multiplier", 1f);

      smokeVolume          = cfg.CreateEntry<float>("Smoke OnCall volume multiplier", 1f);
      smokeTimeToTarget    = cfg.CreateEntry<float>("Smoke OnCall time-to-target multiplier", 1f);
      smokeAccuracy        = cfg.CreateEntry<float>("Smoke OnCall accuracy multiplier", 1f);
      smokePlannedVolume   = cfg.CreateEntry<float>("Smoke Planned volume multiplier", 1f);
      smokePlannedTimeToTarget = cfg.CreateEntry<float>("Smoke Planned time-to-target multiplier", 1f);
      smokePlannedAccuracy = cfg.CreateEntry<float>("Smoke Planned accuracy multiplier", 1f);

      illuVolume          = cfg.CreateEntry<float>("Illumination OnCall volume multiplier", 1f);
      illuTimeToTarget    = cfg.CreateEntry<float>("Illumination OnCall time-to-target multiplier", 1f);
      illuAccuracy        = cfg.CreateEntry<float>("Illumination OnCall accuracy multiplier", 1f);
      illuPlannedVolume   = cfg.CreateEntry<float>("Illumination Planned volume multiplier", 1f);
      illuPlannedTimeToTarget = cfg.CreateEntry<float>("Illumination Planned time-to-target multiplier", 1f);
      illuPlannedAccuracy = cfg.CreateEntry<float>("Illumination Planned accuracy multiplier", 1f);

      MelonPreferences.Save();
      MelonLogger.Msg("[ArtilleryExtended] Preferences created, applying patches...");
      try {
        var harmony = new HarmonyLib.Harmony("GHPC_Artillery_Rework");
        harmony.PatchAll();
        MelonLogger.Msg("[ArtilleryExtended] PatchAll complete.");
      } catch (Exception e) {
        MelonLogger.Msg($"[ArtilleryExtended] PatchAll failed: {e}");
      }
    }
    public static float Clamped(MelonPreferences_Entry<float> entry, float fallback = 1f) {
      if (entry == null)
        return fallback;
      float v = entry.Value;
      return v > 0f ? v : fallback;
    }
  }

  // A thread-local holder for "the faction of the currently-executing planned fire mission"
  public static class PlannedMissionContext
  {
    [ThreadStatic] private static Faction _currentFaction;
    [ThreadStatic] private static bool _isSet;

    public static void Set(Faction f) { _currentFaction = f; _isSet = true; }
    public static void Clear()        { _currentFaction = default; _isSet = false; }

    public static bool TryGet(out Faction f)
    {
        f = _currentFaction;
        return _isSet;
    }
}
  public static class CallerDetector
{
    public enum CallSource { Unknown, OnCall, Planned }

public static CallSource Detect()
{
    var stack = new System.Diagnostics.StackTrace(false);
    for (int i = 1; i < stack.FrameCount; i++)
    {
        var method = stack.GetFrame(i).GetMethod();
        if (method == null) continue;

        var declaring = method.DeclaringType?.FullName ?? "";

        // Skip Harmony / MonoMod / our own patch plumbing
        if (declaring.Contains("Harmony")) continue;
        if (declaring.Contains("MonoMod")) continue;
        if (declaring.StartsWith("GHPC_Artillery_Rework")) continue;
        if (method.Name.Contains("DMD<")) continue;
        if (method.Name == "SendFireMissionOnCall")  return CallSource.OnCall;
        if (method.Name == "SendFireMissionPlanned") return CallSource.Planned;
        return CallSource.Unknown;
    }
    return CallSource.Unknown;
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

public static class PlayerAllegianceStore
{
    public static string Name { get; set; } = "Unknown";
    public static Faction? Current { get; set; }  
    
    public static bool HasValue => Current.HasValue;

}
[HarmonyPatch]
public static class Patch_Campaign_LaunchDynamicMissionFaction
  {
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("GHPC.Mission.DynamicMissionLauncher");
        return AccessTools.Method(type, "LaunchDynamicMission");
    }
    static void Postfix()
{
    var allegiance = DynamicMissionComposer.MissionData.PlayerTeam;
    PlayerAllegianceStore.Current = allegiance;
    PlayerAllegianceStore.Name = allegiance.ToString();
    //MelonLogger.Msg($"[Allegiance] Captured: {PlayerAllegianceStore.Name}");

}
  }
  
[HarmonyPatch]
public static class Patch_MapController_InitControlState
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("GHPC.UI.MapController");
        return AccessTools.Method(type, "InitControlState");
    }

    static void Postfix()
{
    var allegiance = PlayerInput.Instance?.CurrentPlayerUnit?.Allegiance;
    if (allegiance.HasValue)
    {
        PlayerAllegianceStore.Current = allegiance.Value;
        PlayerAllegianceStore.Name = allegiance.Value.ToString();
        //MelonLogger.Msg($"[Allegiance] Captured: {PlayerAllegianceStore.Name}");
    }
}
}

  [HarmonyPatch]
public static class Patch_FireMissionManager_SendFireMissionPlanned
{
    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("GHPC.Weapons.Artillery.FireMissionManager");
        if (type == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (t.Name == "FireMissionManager") { type = t; break; }
                }
                if (type != null) break;
            }
        }

        return AccessTools.Method(type, "SendFireMissionPlanned",
            new[] {
                typeof(Faction), typeof(Vector3), typeof(float),
                typeof(IndirectFireMunitionType),
                typeof(int), typeof(float), typeof(float)
            });
    }

    static void Prefix(Faction team)
    {
        PlannedMissionContext.Set(team);
    }

    static void Finalizer()
    {
        PlannedMissionContext.Clear();
    }
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
        /*MelonLogger.Msg($"[FiringId] {__instance.SupportName} " +
                        $"(btn#{__instance.GetInstanceID()}): " +
                        $"captured reporter {reporter.GetType().Name}" +
                        $"#{reporter.GetHashCode():X8} at idx={i}, " +
                        $"listSize={list.Count}");*/
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
                    IndirectFireMunitionType preferredMunitions)
{
    if (!__result) return;

    ResolveOnce();
    if (_instanceProp == null || _addAlertMethod == null) return;

    string shell_type = preferredMunitions.ToString();
    switch (shell_type)
      {
        case "AntiPersonnel":
          shell_type = "HE";
          break;
        case "AntiArmor":
          shell_type = "HE";
          break;
        case "Smoke":
          shell_type = "WP";
          break;

      }

    string random_message = new[] {
        " This is gonna be a big one.",
        " Stand clear.",
        " Better plug those ears.",
        " Clean up anyone who's left after this.",
        " Sit back and watch the fireworks.",
        " Those bitches have no idea what's coming.",
        " Get some!",
        " Eat shit and die, motherfuckers!"
    }[UnityEngine.Random.Range(0, 8)];
    if (shell_type != "HE") random_message = "";

    var hud = _instanceProp.GetValue(null);
    if (hud == null) return;

    var t = __instance.GetType();
    int shots = (int)(AccessTools.Field(t, "_currentShotQuota")
                        ?.GetValue(__instance) ?? 0);
    float impact = (float)(AccessTools.Property(t, "TimeUntilImpactSeconds")
                            ?.GetValue(__instance) ?? 0f);

    // Branch by call source
    var source = CallerDetector.Detect();
    string msg;
    if (source == CallerDetector.CallSource.Planned)
    {
      
      string teamStr = PlannedMissionContext.TryGet(out var f) ? f.ToString() : "Unknown";
      if (PlayerAllegianceStore.HasValue && teamStr == PlayerAllegianceStore.Name)
        {
          msg = $"Battalion FSO: Executing fire mission of {shots} rounds {shell_type}, " +
              $"{(int)Math.Ceiling(impact)}s to impact.";
        }
        else
        {
          msg = $"SIGINT is reporting an enemy fire mission inbound, {shots} rounds {shell_type}, " + $"{(int)Math.Ceiling(impact)}s to impact.";
        }
    }
    else
    {
        msg = $"Battalion FSO: Executing fire mission on your target — " +
              $"{shots} rounds {shell_type}, " +
              $"{(int)Math.Ceiling(impact)} seconds time to target.{random_message}";
    }

    try
    {
        _addAlertMethod.Invoke(hud, new object[] { msg, 4f });
        //MelonLogger.Msg($"[Alert] Sent: {msg}");
    }
    catch (Exception e)
    {
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

    private static bool _multiplierApplied = false;

    static void Prefix(object __instance, ref float delaySeconds,
                   ref int roundCount, ref float radiusMeters,
                   ref float secondsBetweenRounds, ref IndirectFireMunitionType preferredMunitions)
    {
        if (_multiplierApplied) { //MelonLogger.Msg("[ArtyMult] Skipping re-entrant call"); 
        
        return; }

        var source = CallerDetector.Detect();
        if (source == CallerDetector.CallSource.Unknown) return;

        bool planned = source == CallerDetector.CallSource.Planned;
        bool isSmoke = preferredMunitions == IndirectFireMunitionType.Smoke;
        bool isIllu  = preferredMunitions == IndirectFireMunitionType.Illumination;

        float volMult, ttMult, accMult;
        if (isSmoke) {
            volMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.smokePlannedVolume   : GHPC_Arty_Class.smokeVolume);
            ttMult  = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.smokePlannedTimeToTarget : GHPC_Arty_Class.smokeTimeToTarget);
            accMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.smokePlannedAccuracy  : GHPC_Arty_Class.smokeAccuracy);
        } else if (isIllu) {
            volMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.illuPlannedVolume   : GHPC_Arty_Class.illuVolume);
            ttMult  = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.illuPlannedTimeToTarget : GHPC_Arty_Class.illuTimeToTarget);
            accMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.illuPlannedAccuracy  : GHPC_Arty_Class.illuAccuracy);
        } else {
            volMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.hePlannedVolume   : GHPC_Arty_Class.heVolume);
            ttMult  = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.hePlannedTimeToTarget : GHPC_Arty_Class.heTimeToTarget);
            accMult = GHPC_Arty_Class.Clamped(planned ? GHPC_Arty_Class.hePlannedAccuracy  : GHPC_Arty_Class.heAccuracy);
        }

        _multiplierApplied = true;

        if (roundCount < 0)
            roundCount = (int)_shotsF.GetValue(__instance);
        roundCount = (int)(roundCount * volMult);

        if (secondsBetweenRounds < 0f)
            secondsBetweenRounds = (float)_interShotDelayF.GetValue(__instance);
        secondsBetweenRounds /= volMult;

        if (radiusMeters < 0f)
            radiusMeters = (float)_dispersionF.GetValue(__instance);
        radiusMeters /= accMult;

        delaySeconds /= ttMult;

        /*MelonLogger.Msg($"[ArtyMult-{source}] rounds×{volMult}={roundCount}, " +
                        $"interShot÷{volMult}={secondsBetweenRounds:F2}s, " +
                        $"delay÷{ttMult}={delaySeconds:F1}s, " +
                        $"radius÷{accMult}={radiusMeters:F1}m");*/
    }

    static void Finalizer() => _multiplierApplied = false;
  }

}