using ConVar;
using HarmonyLib;
using JetBrains.Annotations;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Oxide.Plugins;

[UsedImplicitly]
[Info("No Green", "misticos", "2.0.0")]
[Description("Remove admins' green names")]
internal sealed class NoGreen : RustPlugin
{
    [AutoPatch]
    [HarmonyPatch(typeof(Chat), "GetNameColor")]
    // ReSharper disable once InconsistentNaming
    private static class Chat_GetNameColor_Patch
    {
        [UsedImplicitly]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            yield return new CodeInstruction(OpCodes.Ldstr, "#5af");
            yield return new CodeInstruction(OpCodes.Ret);
        }
    }
}