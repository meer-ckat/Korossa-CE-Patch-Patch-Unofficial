using System;
using System.Collections.Generic;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KorossaCEPatch
{
    [StaticConstructorOnStartup]
    public static class KorMechCache
    {
        public static readonly HashSet<ThingDef> KorReloadMechDefs = new HashSet<ThingDef>();

        static KorMechCache()
        {
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.race != null && def.race.thinkTreeMain?.defName == KorMechReloadUtil.ReloadThinkTree)
                {
                    KorReloadMechDefs.Add(def);
                }
            }
        }

        public static bool FastIsKorReloadMech(Pawn pawn)
        {
            return pawn != null && KorReloadMechDefs.Contains(pawn.def);
        }
    }

    internal static class KorMechReloadUtil
    {
        public const string ReloadThinkTree = "Mech_kor_reload"; //하드코딩

        // 폰의 장착된 주무기(equipment.Primary)에 붙은 CE CompAmmoUser. 없으면 null.
        public static CompAmmoUser GetPrimaryAmmoUser(Pawn pawn)
        {
            ThingWithComps primary = pawn?.equipment?.Primary;
            return primary?.TryGetComp<CompAmmoUser>();
        }
    }

    /// <summary>
    /// CE의 Job 재장전/무기치우기 차단.
    /// - Korossa 재장전 메크의 주무기: 틱 재장전이 전담하므로 CE TryStartReload 전면 차단.
    /// - 그 외 메카노이드: 기존 안전망 유지 — 탄창이 비고 탄약도 없을 때만 CE의
    ///   무기 치우기(DoOutOfAmmoAction)를 막는다.
    /// </summary>
    [HarmonyPatch(typeof(CompAmmoUser), nameof(CompAmmoUser.TryStartReload))]
    public static class MechManualReloadGuardPatch
    {
        private static Game cachedGame;
        private static readonly HashSet<int> OutOfAmmoNotifiedWeapons = new HashSet<int>();

        [HarmonyPrefix]
        [HarmonyPriority(3000)]
        public static bool Prefix(CompAmmoUser __instance)
        {
            if (__instance == null || !__instance.UseAmmo)
                return true; //원래 로직 실행

            Pawn wielder = __instance.Wielder;
            if (wielder == null || wielder.RaceProps == null || !wielder.RaceProps.IsMechanoid)
                return true; //원래 로직 실행

            if (KorMechCache.FastIsKorReloadMech(wielder) &&
                ReferenceEquals(wielder.equipment?.Primary, __instance.parent))
            {

                return false;
            }

            if (!ReferenceEquals(cachedGame, Current.Game)) //세션 변경됨
            {
                OutOfAmmoNotifiedWeapons.Clear();
                cachedGame = Current.Game;
            }

            int weaponId = __instance.parent?.thingIDNumber ?? wielder.thingIDNumber;

            // 탄창에 탄이 남았으면 CE의 정상 처리를 방해하지 않는다.
            if (__instance.CurMagCount > 0)
            {
                OutOfAmmoNotifiedWeapons.Remove(weaponId);
                return true;
            }

            // 빈 탄창이라도 호환 탄약이 있으면 CE의 재장전을 허용한다.
            Thing compatibleAmmo;
            if (__instance.TryFindAmmoInInventory(out compatibleAmmo) && compatibleAmmo != null)
            {
                OutOfAmmoNotifiedWeapons.Remove(weaponId);
                return true;
            }

            // 빈 탄창 + 탄약 없음: CE의 무기 치우기를 막고, 연속 빈 상태에 대해 메시지 1회.
            if (wielder.Faction == Faction.OfPlayer && OutOfAmmoNotifiedWeapons.Add(weaponId))
            {
                Messages.Message(
                    "CE_OutOfAmmo".Translate(),
                    wielder,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
            }

            return false;
        }
    }

    /// <summary>
    /// Korossa 재장전 메크의 주무기 탄창이 비어 있으면 발사 자체를 막는다.
    /// - 탄약이 있으면 MechPrimaryGunTickReload가 곧 채워주므로 잠깐 사격을 참는 것.
    /// - 탄약이 없으면 dry-fire와 "탄약 없이 계속 공격 시도"를 막는다.
    /// 마운트 동축/연막 등 다른 verb는 EquipmentSource가 주무기가 아니므로 영향 없음.
    /// </summary>
    [HarmonyPatch(typeof(Verb_ShootCE), nameof(Verb_ShootCE.Available))]
    public static class MechPrimaryGunAvailabilityPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Verb_ShootCE __instance, ref bool __result) //만약 탄약이 0발이고 장전하는 코로사 메카노이드라면 발사 금지
        {
            if (!__result) return;

            Pawn pawn = __instance.CasterPawn;
            if (pawn == null || !pawn.RaceProps.IsMechanoid) return;

            if (!KorMechCache.FastIsKorReloadMech(pawn)) return;

            if (pawn.equipment?.Primary != __instance.EquipmentSource) return;

            CompAmmoUser ammo = __instance.CompAmmo;
            if (ammo != null && ammo.UseAmmo && ammo.CurMagCount <= 0)
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// 주무기 틱 기반 재장전. 탄창이 비면 reloadTime 후 인벤토리 탄약을 소모해 자동 장전한다.
    /// CE Job에 의존하지 않으므로 소집/AI 상태와 무관하게 작동한다.
    /// 로켓포드 자동재장전(TurretAmmoSupplyPatch)과 동일한 방식이며, CE 접근은
    /// TurretAmmoSupplyUtility(리플렉션)를 재사용한다.
    /// </summary>
    [HarmonyPatch]
    public static class MechPrimaryGunTickReload
    {
        private const int CheckIntervalTicks = 30;

        private static Game cachedGame;
        private static readonly Dictionary<int, int> ReloadFinishTick = new Dictionary<int, int>();
        private static readonly HashSet<int> OutOfAmmoNotified = new HashSet<int>();

        public static void Cleanup(int pawnId)
        {
            ReloadFinishTick.Remove(pawnId);
            OutOfAmmoNotified.Remove(pawnId);
        }

        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            MethodBase m = AccessTools.Method(typeof(Pawn), "Tick");
            if (m != null)
                return m;
            return AccessTools.Method(typeof(Pawn), "TickInterval");
        }

        static void Postfix(Pawn __instance)
        {
            if (!KorMechCache.FastIsKorReloadMech(__instance))
                return;
            if (!__instance.Spawned || !__instance.IsHashIntervalTick(CheckIntervalTicks))
                return;

            try
            {
                if (!ReferenceEquals(cachedGame, Current.Game))
                {
                    ReloadFinishTick.Clear();
                    OutOfAmmoNotified.Clear();
                    cachedGame = Current.Game;
                }

                CompAmmoUser ammo = KorMechReloadUtil.GetPrimaryAmmoUser(__instance);
                if (ammo == null || !ammo.UseAmmo)
                    return;

                int pawnId = __instance.thingIDNumber;

                // 이미 장전돼 있으면 타이머/알림 해제.
                if (ammo.CurMagCount > 0)
                {
                    ReloadFinishTick.Remove(pawnId);
                    OutOfAmmoNotified.Remove(pawnId);
                    return;
                }

                // 빈 탄창. 호환 탄약 탐색(선택 탄약 우선, ammoSet 내 대체 탄약 허용).
                Thing ammoThing;
                if (!ammo.TryFindAmmoInInventory(out ammoThing) || ammoThing == null)
                {
                    ReloadFinishTick.Remove(pawnId);
                    if (__instance.Faction == Faction.OfPlayer && OutOfAmmoNotified.Add(pawnId))
                    {
                        Messages.Message(
                            "CE_OutOfAmmo".Translate(),
                            __instance,
                            MessageTypeDefOf.RejectInput,
                            historical: false);
                    }
                    return;
                }
                OutOfAmmoNotified.Remove(pawnId);

                int now = Find.TickManager.TicksGame;

                int finishTick;
                if (!ReloadFinishTick.TryGetValue(pawnId, out finishTick))
                {
                    // 이 폰에 대한 타이머가 없으면 지금 막 탄창이 빈 것 -> 대기 시간만
                    // 등록하고 이번 체크는 종료(TurretAmmoSupplyPatch와 동일 패턴).
                    // 재장전 시작: reloadTime(초) → 틱.
                    int reloadTicks = Mathf.Max(1, (int)(ammo.Props.reloadTime * 60f));
                    ReloadFinishTick[pawnId] = now + reloadTicks;
                    return;
                }

                if (now < finishTick)
                    return;

                // 재장전 완료: 인벤토리 탄약 소모 후 장전.
                int magSize = Mathf.Max(1, ammo.MagSize);

                ThingDef ammoDef = ammoThing.def;
                int loaded = TurretAmmoSupplyUtility.ConsumeAmmoFromInventory(__instance, ammoDef, magSize);
                if (loaded > 0)
                {
                    TurretAmmoSupplyUtility.SetLoadedAmmo(ammo, ammoDef);
                    ammo.CurMagCount = loaded;
                }
                ReloadFinishTick.Remove(pawnId);
            }
            catch(Exception e) { Log.Warning("[Korossa CE PATCH PATCH]: fatal error but I concealed it. " + e); }
        }
    }

    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn),
    new Type[]
    {
        typeof(LocalTargetInfo),
        typeof(LocalTargetInfo),
        typeof(bool),
        typeof(bool),
        typeof(bool),
        typeof(bool)
    })]
    public static class KorMultiTurretWarmupGuard
    {
        [HarmonyPrefix]
        [HarmonyPriority(4000)]
        public static bool Prefix(Verb __instance, ref bool __result)
        {
            Pawn pawn = __instance.CasterPawn;

            if (pawn == null ||
                !KorMechCache.FastIsKorReloadMech(pawn))
                return true;

            if (__instance.EquipmentSource == null)
                return true;

            if (!(pawn.stances?.curStance is Stance_Warmup warmup))
                return true;

            Verb currentVerb = warmup.verb;

            if (currentVerb == null ||
                ReferenceEquals(currentVerb, __instance))
                return true;

            __result = false;

            return false;
        }
    }

    /// <summary>
    /// CE의 무기 치우기 버그로 주무기가 인벤토리에 들어간 옛 세이브를 복구한다.
    /// 인벤토리에 CompAmmoUser 총이 정확히 하나뿐일 때만(멀티무기 오판 방지) 재장착.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class MechStowedPrimaryRecoveryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (!KorMechCache.FastIsKorReloadMech(__instance)
                || __instance.equipment == null
                || __instance.equipment.Primary != null
                || __instance.inventory?.innerContainer == null)
            {
                return;
            }

            ThingWithComps onlyGun = null;
            foreach (Thing thing in __instance.inventory.innerContainer)
            {
                ThingWithComps gun = thing as ThingWithComps;
                if (gun == null || gun.TryGetComp<CompAmmoUser>() == null)
                    continue;

                if (onlyGun != null)
                    return;

                onlyGun = gun;
            }

            if (onlyGun == null)
                return;

            if (__instance.inventory.innerContainer.Remove(onlyGun))
            {
                __instance.equipment.AddEquipment(onlyGun);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class MechPrimaryGunReloadCleanup
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            if (__instance == null || !KorMechCache.FastIsKorReloadMech(__instance))
                return;
                
            MechPrimaryGunTickReload.Cleanup(__instance.thingIDNumber);
        }
    }
}
