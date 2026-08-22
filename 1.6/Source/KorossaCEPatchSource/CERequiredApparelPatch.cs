using System;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KorossaCEPatch
{
    public class CERequiredApparelWeapons : DefModExtension //확장 가능한 fallback. 이 defmod -> 하드코드 순으로 확인
    {
        public string RequireApparelDefName;
    }

    [StaticConstructorOnStartup]
    public static class CERequiredApparelPatchBootstrap
    {
        static CERequiredApparelPatchBootstrap()
        {
            try
            {
                var harmony = new Harmony("korossa.ce.requiredapparel.patch");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log.Error("[Korossa CE Patch by starlellok] EXO weapon required apparel Harmony patch initialization failed:\n" + ex);
            }
        }
    }

    [HarmonyPatch(typeof(Verb_ShootCE), nameof(Verb_ShootCE.Available))]
    public static class Patch_VerbShootCE_Available
    {
        static void Postfix(Verb __instance, ref bool __result)
        {
            if (!__result)
                return;

            if (!RequiredApparelUtility.CanFire(__instance.CasterPawn, __instance.EquipmentSource))
                __result = false;
        }
    }

    /// <summary>
    /// 대상: CE <see cref="Verb_ShootCE.TryCastShot"/> (Prefix).
    /// Available 체크(위)를 우회해서 발사가 시도되는 경우에 대한 이중 방어선.
    /// 필수 아머 미착용이면 원본 발사 로직 자체를 실행하지 않고(false 반환)
    /// __result를 false로 강제, 플레이어 소속 폰에게는 안내 메시지를 표시한다.
    /// </summary>
    [HarmonyPatch(typeof(Verb_ShootCE), nameof(Verb_ShootCE.TryCastShot))]
    public static class Patch_VerbShootCE_TryCastShot
    {
        static bool Prefix(Verb __instance, ref bool __result)
        {
            Pawn pawn = __instance.CasterPawn;

            if (RequiredApparelUtility.CanFire(pawn, __instance.EquipmentSource))
                return true;

            if (pawn.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "Required exosuit not equipped.",
                    pawn,
                    MessageTypeDefOf.RejectInput,
                    false
                );
            }

            __result = false;
            return false;
        }
    }

    /// <summary>
    /// EXOSUIT 전용 중화기 6종의 "필수 아머 착용" 판정(패치 아님, 순수 헬퍼).
    /// 무기 ThingDef의 CERequiredApparelWeapons 모드확장을 우선 사용하고,
    /// 없으면 무기 defName -> 필요 Exosuit defName의 하드코딩 폴백을 쓴다.
    /// 폴백 표가 곧 "검사 대상 무기 목록"이므로 별도 목록을 두지 않는다.
    /// </summary>
    public static class RequiredApparelUtility
    {
        /// <summary>필수 아머 요구가 없거나, 요구를 만족하면 true.</summary>
        public static bool CanFire(Pawn pawn, ThingWithComps equipment)
        {
            if (pawn?.apparel?.WornApparel == null || equipment?.def == null)
                return true;

            string required =
                equipment.def.GetModExtension<CERequiredApparelWeapons>()?.RequireApparelDefName
                ?? FallbackRequiredApparel(equipment.def.defName);

            if (string.IsNullOrEmpty(required))
                return true;

            for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
            {
                if (pawn.apparel.WornApparel[i]?.def?.defName == required)
                    return true;
            }

            return false;
        }

        private static string FallbackRequiredApparel(string weaponDefName)
        {
            switch (weaponDefName)
            {
                case "KJW_AMAR_Kinetic":
                case "KJW_SG_EXO":
                    return "Korossa_Exosuit_Smoke";

                case "KJW_AMHMG":
                case "KJW_HVMMG":
                    return "Korossa_Exosuit";

                case "KJW_AMR":
                case "KJW_ATM_EXO":
                    return "Korossa_Exosuit_Drifter";

                default:
                    return null;
            }
        }
    }
}
