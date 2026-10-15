using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.GameContent;

namespace Remedy_And_Ruin.GameEngineTweaks.HarmonyPatches
{
    /// <summary>
    /// Makes vanilla player armor follow the hit location. ModSystemWearableStats.handleDamaged picks the
    /// armor slot (and the clothing slot that wears when the armor slot is empty) from one
    /// System.Random.NextDouble() roll cascaded 20% head, 50% body, 30% legs. The transpiler replaces that
    /// single call with <see cref="PickArmorRoll"/>, which returns a value inside the band of the hit's
    /// location (Skull head, ChestArms body, Legs legs) and otherwise falls through to the random roll.
    /// Throws if the method does not contain exactly one NextDouble call.
    /// </summary>
    [HarmonyPatch(typeof(ModSystemWearableStats), "handleDamaged")]
    public static class Patch_LocationScopedArmor
    {
        private const double HeadRoll = 0.1;
        private const double BodyRoll = 0.45;
        private const double LegsRoll = 0.85;

        private static readonly MethodInfo NextDouble = AccessTools.Method(typeof(Random), nameof(Random.NextDouble), Type.EmptyTypes);
        private static readonly MethodInfo PickArmorRollMethod = AccessTools.Method(typeof(Patch_LocationScopedArmor), nameof(PickArmorRoll));

        public static double PickArmorRoll(Random rand)
        {
            HitClassification? hit = HitContext.Current;
            if (!hit.HasValue) return rand.NextDouble();

            switch (hit.Value.Location)
            {
                case BoneLocation.Skull: return HeadRoll;
                case BoneLocation.ChestArms: return BodyRoll;
                default: return LegsRoll;
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            int found = -1;
            int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(NextDouble))
                {
                    found = i;
                    count++;
                }
            }

            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"remedyandruin: expected exactly one Random.NextDouble call in ModSystemWearableStats.handleDamaged but found {count}; the game version's armor code has changed.");
            }

            code[found] = new CodeInstruction(OpCodes.Call, PickArmorRollMethod);
            return code;
        }
    }
}
