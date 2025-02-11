﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using ActionGame;
using ADV;
using HarmonyLib;
using KKAPI.Utilities;
using KKS_VR.Interpreters;
using KKS_VR.Settings;
using Sirenix.Serialization.Utilities;
using StrayTech;
using VRGIN.Core;
using Object = UnityEngine.Object;

/*
 * Fixes for issues that are in the base game but are only relevant in VR.
 */
namespace KKS_VR.Fixes
{
    /// <summary>
    /// Suppress character update for invisible characters in some sub-scenes of Roaming.
    /// </summary>
    [HarmonyPatch(typeof(ChaControl))]
    public class ChaControlPatches1
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChaControl.LateUpdateForce))]
        private static bool PreLateUpdateForce(ChaControl __instance)
        {
            return !SafeToSkipUpdate(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(ChaControl.UpdateForce))]
        private static bool PreUpdateForce(ChaControl __instance)
        {
            return !SafeToSkipUpdate(__instance);
        }

        public static bool SafeToSkipUpdate(ChaControl control)
        {
            return
                VR.Settings is KoikatuSettings settings &&
                settings.OptimizeHInsideRoaming &&
                control.objTop?.activeSelf == false &&
                VR.Interpreter is KoikatuInterpreter interpreter &&
                (interpreter.CurrentScene == KoikatuInterpreter.SceneType.HScene ||
                 interpreter.CurrentScene == KoikatuInterpreter.SceneType.TalkScene);
        }
    }

    /// <summary>
    /// Fix game crash during map load
    /// todo hack, handle properly?
    /// </summary>
    [HarmonyPatch(typeof(SunLightInfo))]
    public class FogHack1
    {
        [HarmonyFinalizer]
        [HarmonyPatch(nameof(SunLightInfo.Set))]
        private static Exception PreLateUpdateForce(Exception __exception)
        {
            if (__exception != null) VRPlugin.Logger.LogDebug("Caught expected crash: " + __exception);
            return null;
        }
    }

    /// <summary>
    /// Fix game crash during map load
    /// todo hack, handle properly?
    /// </summary>
    [HarmonyPatch(typeof(ActionMap))]
    public class FogHack2
    {
        // todo hack, handle properly
        [HarmonyFinalizer]
        [HarmonyPatch(nameof(ActionMap.UpdateCameraFog))]
        private static Exception PreLateUpdateForce(Exception __exception)
        {
            if (__exception != null) VRPlugin.Logger.LogDebug("Caught expected crash: " + __exception);
            return null;
        }
    }

    /// <summary>
    /// Fix game crash during ADV scene load
    /// </summary>
    [HarmonyPatch(typeof(Manager.Game))]
    public class ADVSceneFix1
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(Manager.Game.cameraEffector), MethodType.Getter)]
        private static void FixMissingCameraEffector(Manager.Game __instance, ref CameraEffector __result)
        {
            if (__result == null && __instance.isCameraChanged)
                // vr camera doesn't have this component on it, which crashes game code with nullref. Use the component on original advcamera instead
                __instance._cameraEffector = __result = Object.FindObjectOfType<CameraEffector>();
        }
    }

    /// <summary>
    /// Fix being unable to do some actions in roaming mode
    /// </summary>
    [HarmonyPatch(typeof(CameraSystem))]
    public class ADVSceneFix2
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(CameraSystem.SystemStatus), MethodType.Getter)]
        private static bool FixNeverEndingTransition(ref CameraSystem.CameraSystemStatus __result)
        {
            __result = CameraSystem.CameraSystemStatus.Inactive;
            return false;
        }
    }

    /// <summary>
    /// Fix ADV scenes messing with the VR camera by moving it or setting flags on it. Feed it the default 2D camera instead so it's happy.
    /// </summary>
    [HarmonyPatch]
    public class ADVSceneFix3
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return CoroutineUtils.GetMoveNext(AccessTools.Method(typeof(TalkScene), nameof(TalkScene.Setup)));
        }

        private static UnityEngine.Camera GetOriginalMainCamera()
        {
            // vr camera doesn't have this component on it
            var originalMainCamera = (Manager.Game.instance.cameraEffector ?? Object.FindObjectOfType<CameraEffector>()).GetComponent<UnityEngine.Camera>();
            VRPlugin.Logger.LogDebug($"GetOriginalMainCamera called, cam found: {originalMainCamera?.GetFullPath()}\n{new StackTrace()}");
            return originalMainCamera;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase __originalMethod)
        {
            var targert = AccessTools.PropertyGetter(typeof(UnityEngine.Camera), nameof(UnityEngine.Camera.main));
            var replacement = AccessTools.Method(typeof(ADVSceneFix3), nameof(GetOriginalMainCamera));
            return insts.Manipulator(
                instr => instr.opcode == OpCodes.Call && (MethodInfo)instr.operand == targert,
                instr =>
                {
                    instr.operand = replacement;
                    VRPlugin.Logger.LogDebug("Patched Camera.main in " + __originalMethod.GetNiceName());
                });
        }
    }

    /// <summary>
    /// Fix ADV scenes messing with the VR camera by moving it or setting flags on it. Feed it the default 2D camera instead so it's happy.
    /// </summary>
    [HarmonyPatch]
    public class ADVSceneFix4
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ADVScene), nameof(ADVScene.Init));
        }

        private static UnityEngine.Camera GetOriginalMainCamera()
        {
            // vr camera doesn't have this component on it
            var originalMainCamera = (Manager.Game.instance.cameraEffector ?? Object.FindObjectOfType<CameraEffector>()).GetComponent<UnityEngine.Camera>();
            VRPlugin.Logger.LogDebug($"GetOriginalMainCamera called, cam found: {originalMainCamera?.GetFullPath()}\n{new StackTrace()}");
            return originalMainCamera;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase __originalMethod)
        {
            var targert = AccessTools.PropertyGetter(typeof(UnityEngine.Camera), nameof(UnityEngine.Camera.main));
            var replacement = AccessTools.Method(typeof(ADVSceneFix4), nameof(GetOriginalMainCamera));
            return insts.Manipulator(
                instr => instr.opcode == OpCodes.Call && (MethodInfo)instr.operand == targert,
                instr =>
                {
                    instr.operand = replacement;
                    VRPlugin.Logger.LogDebug("Patched Camera.main in " + __originalMethod.GetNiceName());
                });
        }

        private static void Postfix(ADVScene __instance)
        {
            Manager.Sound.Listener = UnityEngine.Camera.main.transform;
        }
    }

    /// <summary>
    /// Fix crash when playing ADV scenes
    /// </summary>
    [HarmonyPatch]
    public class CycleCrossFadeFix1
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return CoroutineUtils.GetMoveNext(AccessTools.Method(typeof(Cycle), nameof(Cycle.WakeUp)));
        }

        private static bool IsProcessWithNullcheck(CrossFade instance)
        {
            return instance != null && instance.isProcess;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts, MethodBase __originalMethod)
        {
            var targert = AccessTools.PropertyGetter(typeof(CrossFade), nameof(CrossFade.isProcess));
            var replacement = AccessTools.Method(typeof(CycleCrossFadeFix1), nameof(IsProcessWithNullcheck));
            return insts.Manipulator(
                instr => instr.opcode == OpCodes.Callvirt && (MethodInfo)instr.operand == targert,
                instr =>
                {
                    instr.opcode = OpCodes.Call;
                    instr.operand = replacement;
                    VRPlugin.Logger.LogDebug("Patched CrossFade.isProcess in " + __originalMethod.GetFullName());
                });
        }
    }
}
