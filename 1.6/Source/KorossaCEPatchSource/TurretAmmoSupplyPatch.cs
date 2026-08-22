using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace KorossaCEPatch
{
    // =========================================================================
    // ADlunchbox 로켓포드 / 셰리든 M2HB(CompTurretGun) 탄약 공급 패치
    //
    // 문제: CompTurretGun으로 마운트된 총은 CE AmmoUser가 붙어 있어도 스폰 시
    //       탄약을 공급받지 못함 (CE LoadoutPropertiesExtension은 주무기만 처리,
    //       CompMechAmmo도 주무기 전용).
    //
    // 해결:
    //  1) Pawn.SpawnSetup 후처리 - 대상 메크 스폰 시 터렛 탄창을 가득 채우고
    //     예비 탄약(SpareMagazines 탄창 분량)을 인벤토리에 지급.
    //  2) CE의 Job 기반 재장전 차단 - JobDriver_Reload는 홀더 없는 마운트 총을
    //     찾지 못해 "Unable to find the weapon to be reloaded" 예외를 반복한다.
    //  3) CompTurretGun 틱 후처리 - 탄창이 비면 reloadTime 후 인벤토리 탄약을
    //     소모해 자동 재장전. 인벤토리에 탄이 없으면 침묵(재보급 시 재개).
    //
    // CE는 About.xml의 필수 의존성이므로 CombatExtended.dll을 하드 참조한다.
    // 리플렉션은 공개 접근자가 없는 private 필드(burstWarmupTicksLeft,
    // currentTarget)에 한해서만 쓴다.
    // =========================================================================

    /// <summary>
    /// 아래 Harmony 패치 클래스들이 공유하는 조회/조작 헬퍼(패치 아님).
    /// </summary>
    public static class TurretAmmoSupplyUtility
    {
        // 터렛 탄약 공급을 적용할 폰 defName 목록 (필요 시 추가)
        private static readonly HashSet<string> TargetPawnDefNames = new HashSet<string>
        {
            "Mech_ADlunchbox",
            "Mech_Sheridan" // 2026-07-26: KOR_Sheridan_M2HB 마운트 터렛 탄약 공급용 추가
        };

        // TryStartReload/DoOutOfAmmoAction 차단 대상 마운트 터렛 총 defName 목록.
        // (JobDriver_Reload가 홀더 없는 마운트 총을 찾지 못해 예외를 반복시키므로
        //  등록된 총만 CE Job 재장전을 차단하고, 실제 재장전은 자동재장전 패치가 담당)
        private static readonly HashSet<string> TargetGunDefNames = new HashSet<string>
        {
            "Gun_rocketpod_lunchbox",
            "KOR_Sheridan_M2HB" // 2026-07-26: 차장용 M2HB 터렛 추가
        };

        // 스폰 시 지급할 예비 탄창 수 (탄창 6발 x 2 = 로켓 12발 + 장전된 6발)
        public const int SpareMagazines = 2;

        // CompTurretGun의 private 필드 2종. 공개 접근자가 없어 리플렉션 유지.
        public static readonly FieldInfo FBurstWarmup =
            AccessTools.Field(typeof(CompTurretGun), "burstWarmupTicksLeft");
        public static readonly FieldInfo FCurrentTarget =
            AccessTools.Field(typeof(CompTurretGun), "currentTarget");

        // 1.6은 CompTick / CompTickInterval 중 하나만 존재할 수 있다.
        public static MethodBase TurretGunTickMethod()
        {
            MethodBase m = AccessTools.Method(typeof(CompTurretGun), "CompTick");
            if (m != null && !m.IsAbstract && m.DeclaringType == typeof(CompTurretGun))
                return m;

            return AccessTools.Method(typeof(CompTurretGun), "CompTickInterval")
                ?? AccessTools.Method(typeof(CompTurretGun), "CompTick");
        }

        public static bool IsTargetPawn(Pawn pawn)
        {
            return pawn?.def != null && TargetPawnDefNames.Contains(pawn.def.defName);
        }

        public static Thing GetTurretGun(CompTurretGun comp)
        {
            return comp?.gun;
        }

        public static CompAmmoUser GetAmmoUser(Thing gun)
        {
            return (gun as ThingWithComps)?.TryGetComp<CompAmmoUser>();
        }

        // 폰에 마운트된 터렛 총 판정: 등록된 총이면서, 장비창/인벤토리 어디에도
        // 소속되지 않은(홀더 없는) 미스폰 총 + CE 건물 터렛도 아님.
        // 이런 총은 CE JobDriver_Reload가 절대 찾을 수 없으므로 CE 재장전을 차단해야 함.
        public static bool IsPawnMountedTurretGun(CompAmmoUser ammoUser)
        {
            ThingWithComps gun = ammoUser?.parent;
            if (gun?.def == null || !TargetGunDefNames.Contains(gun.def.defName)) //하드코딩은 신이다.
                return false;

            if (gun.Spawned || gun.ParentHolder != null)
                return false;

            return ammoUser.turret == null;
        }

        /// <summary>
        /// 동축/포드 터렛(FollowPrimaryTarget)인데 차체가 굴러가는 중인가.
        /// 이동 사격은 막고, 정지하면 다시 조준부터 시작하게 만드는 판정.
        /// </summary>
        public static bool MovementBlocked(CompTurretGun turret)
        {
            if (turret?.gun?.def.GetModExtension<FollowPrimaryTarget>() == null)
                return false;

            return (turret.parent as Pawn)?.pather?.MovingNow == true;
        }

        // ammoSet의 모든 탄약 def
        public static List<AmmoDef> GetAmmoSetDefs(CompAmmoUser ammoUser)
        {
            List<AmmoLink> links = ammoUser?.Props?.ammoSet?.ammoTypes;
            if (links == null)
                return new List<AmmoDef>();

            return links.Select(l => l.ammo).Where(a => a != null).Distinct().ToList();
        }

        // CurrentAmmo(장전된 탄)가 있으면 그것을, 없으면 SelectedAmmo,
        // 그래도 없으면 ammoSet의 첫 번째 탄약 def를 반환.
        public static AmmoDef GetAmmoDef(CompAmmoUser ammoUser)
        {
            if (ammoUser == null)
                return null;

            return ammoUser.CurrentAmmo
                ?? ammoUser.SelectedAmmo
                ?? GetAmmoSetDefs(ammoUser).FirstOrDefault();
        }

        // 장전 탄약 지정. CurrentAmmo는 get-only이므로 SelectedAmmo만 세팅한다.
        public static void SetLoadedAmmo(CompAmmoUser ammoUser, ThingDef ammoDef)
        {
            if (ammoUser != null && ammoDef is AmmoDef ammo)
                ammoUser.SelectedAmmo = ammo;
        }

        public static int CountAmmoInInventory(Pawn pawn, ThingDef ammoDef)
        {
            if (pawn?.inventory?.innerContainer == null || ammoDef == null)
                return 0;

            return pawn.inventory.innerContainer.TotalStackCountOfDef(ammoDef);
        }

        public static void AddAmmoToInventory(Pawn pawn, ThingDef ammoDef, int count)
        {
            if (pawn?.inventory?.innerContainer == null || ammoDef == null || count <= 0)
                return;

            while (count > 0)
            {
                int stack = Math.Min(count, ammoDef.stackLimit);
                Thing ammo = ThingMaker.MakeThing(ammoDef);
                ammo.stackCount = stack;
                pawn.inventory.innerContainer.TryAdd(ammo, true);
                count -= stack;
            }
        }

        // 인벤토리에서 최대 count발을 소모하고 실제 소모량을 반환
        public static int ConsumeAmmoFromInventory(Pawn pawn, ThingDef ammoDef, int count)
        {
            if (pawn?.inventory?.innerContainer == null || ammoDef == null || count <= 0)
                return 0;

            List<Thing> stacks = pawn.inventory.innerContainer.Where(t => t?.def == ammoDef).ToList();

            int consumed = 0;
            for (int i = 0; i < stacks.Count && consumed < count; i++)
            {
                Thing taken = pawn.inventory.innerContainer.Take(stacks[i], Math.Min(stacks[i].stackCount, count - consumed));
                if (taken == null)
                    continue;

                consumed += taken.stackCount;
                taken.Destroy(DestroyMode.Vanish);
            }

            return consumed;
        }
    }

    // -------------------------------------------------------------------------
    // 1) 스폰 시 탄창 장전 + 예비 탄약 지급
    // -------------------------------------------------------------------------
    /// <summary>
    /// 대상: 바닐라 <see cref="Pawn.SpawnSetup"/> (Postfix, CE 패치 아님).
    /// 신규 스폰(세이브 로드 후 재스폰은 제외)인 대상 메크에 한해, 마운트된
    /// CompTurretGun의 탄창을 가득 채우고 아직 예비 탄약이 없으면 지급한다.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class Patch_Pawn_SpawnSetup_TurretAmmoSupply
    {
        // respawningAfterLoad가 true면 이미 세이브에 저장된 탄약 상태이므로
        // 여기서 다시 채우면 안 된다(중복 지급 방지).
        static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad || !TurretAmmoSupplyUtility.IsTargetPawn(__instance))
                return;

            List<ThingComp> comps = __instance.AllComps;
            if (comps == null)
                return;

            for (int i = 0; i < comps.Count; i++)
            {
                CompTurretGun turretComp = comps[i] as CompTurretGun;
                if (turretComp == null)
                    continue;

                CompAmmoUser ammoUser =
                    TurretAmmoSupplyUtility.GetAmmoUser(TurretAmmoSupplyUtility.GetTurretGun(turretComp));
                if (ammoUser == null)
                    continue;

                AmmoDef ammoDef = TurretAmmoSupplyUtility.GetAmmoDef(ammoUser);
                int magSize = ammoUser.MagSize;
                if (ammoDef == null || magSize <= 0)
                    continue;

                // 탄창 가득 장전
                ammoUser.SelectedAmmo = ammoDef;
                ammoUser.CurMagCount = magSize;

                // 예비 탄약 지급 (이미 갖고 있으면 중복 지급 안 함)
                if (TurretAmmoSupplyUtility.CountAmmoInInventory(__instance, ammoDef) == 0)
                {
                    TurretAmmoSupplyUtility.AddAmmoToInventory(
                        __instance, ammoDef, magSize * TurretAmmoSupplyUtility.SpareMagazines);
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // 2) CE 재장전 차단: 터렛 총에 대해 CE가 JobDriver_Reload 잡을 만들면
    //    "Unable to find the weapon to be reloaded" 예외가 무한 발생함.
    //    터렛 총 재장전은 아래 3) 자동 재장전이 전담한다.
    // -------------------------------------------------------------------------
    [HarmonyPatch(typeof(CompAmmoUser), nameof(CompAmmoUser.TryStartReload))]
    public static class Patch_CompAmmoUser_TryStartReload_TurretGuard
    {
        static bool Prefix(CompAmmoUser __instance)
        {
            return !TurretAmmoSupplyUtility.IsPawnMountedTurretGun(__instance);
        }
    }

    /// <summary>
    /// 대상: CE CompAmmoUser.DoOutOfAmmoAction (Prefix, private 메서드라 문자열 타겟팅).
    /// 탄약 소진 시 CE가 시도하는 무기 교체/탄약 줍기는 마운트 터렛 총에 의미가 없다.
    /// </summary>
    [HarmonyPatch(typeof(CompAmmoUser), "DoOutOfAmmoAction")]
    public static class Patch_CompAmmoUser_DoOutOfAmmoAction_TurretGuard
    {
        static bool Prefix(CompAmmoUser __instance)
        {
            return !TurretAmmoSupplyUtility.IsPawnMountedTurretGun(__instance);
        }
    }

    /// <summary>
    /// 동축 기관총/로켓 포드용. 터렛의 목표를 주무기가 조준 중인 목표로 강제한다.
    /// </summary>
    public class FollowPrimaryTarget : DefModExtension { }

    // -------------------------------------------------------------------------
    // 3) CompTurretGun 틱 훅 하나로 3가지 처리
    //    Prefix  : FollowPrimaryTarget - 터렛 조준 목표를 주무기와 동기화
    //    Postfix : 자동 재장전 + 교전 시작 시 웜업(조준 시간) 부여
    // -------------------------------------------------------------------------
    [HarmonyPatch]
    public static class Patch_CompTurretGun_Tick
    {
        private const int CheckIntervalTicks = 30;
        private const int RetryIntervalTicks = 250;
        private const int WarmupTicks = 90; // 1.5초 (60틱 = 1초)

        // gun -> 재장전 완료 예정 틱
        private static readonly Dictionary<Thing, int> ReloadFinishTick = new Dictionary<Thing, int>();

        private class WarmupState
        {
            public int readyTick;
            public bool warming;
        }

        private static readonly Dictionary<CompTurretGun, WarmupState> WarmupStates =
            new Dictionary<CompTurretGun, WarmupState>();

        static bool Prepare() => TargetMethod() != null;

        static MethodBase TargetMethod() => TurretAmmoSupplyUtility.TurretGunTickMethod();

        public static void Cleanup(CompTurretGun turret)
        {
            if (turret == null)
                return;

            WarmupStates.Remove(turret);

            Thing gun = TurretAmmoSupplyUtility.GetTurretGun(turret);
            if (gun != null)
                ReloadFinishTick.Remove(gun);
        }

        static void Prefix(CompTurretGun __instance)
        {
            if (__instance.gun?.def.GetModExtension<FollowPrimaryTarget>() == null)
                return;

            // 차체가 이동 중이면 조준(회전)과 사격을 모두 막는다.
            // CompTick은 currentTarget이 무효면 포탑 회전 블록을 통째로 건너뛰고,
            // burstWarmupTicksLeft를 매 틱 0으로 눌러 두면 워밍업이 0에 도달해
            // 발사되는 일 자체가 없다. (VerbTick은 그대로 돌아 버스트/쿨다운은 정상)
            if (TurretAmmoSupplyUtility.MovementBlocked(__instance))
            {
                TurretAmmoSupplyUtility.FCurrentTarget?.SetValue(__instance, LocalTargetInfo.Invalid);
                TurretAmmoSupplyUtility.FBurstWarmup?.SetValue(__instance, 0);
                return;
            }

            Pawn pawn = __instance.parent as Pawn;
            TurretAmmoSupplyUtility.FCurrentTarget?.SetValue(__instance,
                pawn?.equipment?.PrimaryEq?.PrimaryVerb?.CurrentTarget ?? LocalTargetInfo.Invalid);
        }

        static void Postfix(CompTurretGun __instance)
        {
            try
            {
                Pawn pawn = __instance?.parent as Pawn;
                if (pawn == null || !pawn.Spawned || !TurretAmmoSupplyUtility.IsTargetPawn(pawn))
                    return;

                FireWarmupTick(__instance);

                if (pawn.IsHashIntervalTick(CheckIntervalTicks))
                    AutoReloadTick(__instance, pawn);
            }
            catch { }
        }

        /// <summary>
        /// 탄창이 비면 reloadTime(초 x 60틱) 대기 후 인벤토리 탄약을 소모해 장전한다.
        /// CE Job 재장전이 위에서 차단되었으므로 이게 그 대체 수단이다.
        /// </summary>
        private static void AutoReloadTick(CompTurretGun turret, Pawn pawn)
        {
            Thing gun = TurretAmmoSupplyUtility.GetTurretGun(turret);
            CompAmmoUser ammoUser = TurretAmmoSupplyUtility.GetAmmoUser(gun);
            if (ammoUser == null)
                return;

            if (ammoUser.CurMagCount != 0)
            {
                // 장전돼 있으면 재장전 타이머 해제
                if (ammoUser.CurMagCount > 0)
                    ReloadFinishTick.Remove(gun);
                return;
            }

            int now = Find.TickManager.TicksGame;

            int finishTick;
            if (!ReloadFinishTick.TryGetValue(gun, out finishTick))
            {
                // 타이머가 아직 없으면 지금 막 탄창이 비었다는 뜻 -> 대기 시간만
                // 등록하고 종료. 다음 체크에서 finishTick에 도달하면 실제로 장전한다.
                ReloadFinishTick[gun] = now + (int)(ammoUser.Props.reloadTime * 60f);
                return;
            }

            if (now < finishTick)
                return;

            AmmoDef ammoDef = TurretAmmoSupplyUtility.GetAmmoDef(ammoUser);
            int magSize = ammoUser.MagSize;
            if (ammoDef == null || magSize <= 0)
            {
                ReloadFinishTick.Remove(gun);
                return;
            }

            int loaded = TurretAmmoSupplyUtility.ConsumeAmmoFromInventory(pawn, ammoDef, magSize);
            if (loaded > 0)
            {
                ammoUser.SelectedAmmo = ammoDef;
                ammoUser.CurMagCount = loaded;
                ReloadFinishTick.Remove(gun);
            }
            else
            {
                // 탄약 없음 - 나중에 재시도 (재보급 대기)
                ReloadFinishTick[gun] = now + RetryIntervalTicks;
            }
        }

        /// <summary>
        /// 터렛이 새로 교전을 시작하는 순간(idle -> 적 포착) burstWarmupTicksLeft를
        /// 2로 유지해 WarmupTicks 동안 발사를 지연시킨다. 원본 CompTurretGun은
        /// 이 지연이 없어 즉시 발사한다.
        /// </summary>
        private static void FireWarmupTick(CompTurretGun turret)
        {
            if (TurretAmmoSupplyUtility.FBurstWarmup == null || TurretAmmoSupplyUtility.FCurrentTarget == null)
                return;

            LocalTargetInfo target = (LocalTargetInfo)TurretAmmoSupplyUtility.FCurrentTarget.GetValue(turret);

            // 이동 중이거나 타겟 없음(비교전) -> 상태 제거.
            // 이동이 끝나면 다시 처음부터 windup 하므로 "정지 -> 조준 -> 사격"이 된다.
            if (!target.IsValid || TurretAmmoSupplyUtility.MovementBlocked(turret))
            {
                WarmupStates.Remove(turret);
                return;
            }

            int now = Find.TickManager.TicksGame;

            WarmupState st;
            if (!WarmupStates.TryGetValue(turret, out st))
            {
                // 방금 교전 시작(idle -> 적 포착): windup 시작
                st = new WarmupState { warming = true, readyTick = now + WarmupTicks };
                WarmupStates[turret] = st;
            }

            if (!st.warming)
                return;

            if (now < st.readyTick)
            {
                // 아직 조준 중 -> warmup 카운터를 2로 유지해 발사를 막음
                // (바닐라는 이 값을 매틱 1씩 줄여 0이 되면 발사)
                TurretAmmoSupplyUtility.FBurstWarmup.SetValue(turret, 2);
            }
            else
            {
                // 조준 완료 -> 1로 두면 다음 틱에 발사됨. 이후엔 정상 쿨다운.
                st.warming = false;
                TurretAmmoSupplyUtility.FBurstWarmup.SetValue(turret, 1);
            }
        }
    }

    /// <summary>
    /// 대상: 바닐라 <see cref="Pawn.DeSpawn"/> (Prefix).
    /// 디스폰/사망 시 위 패치가 gun/터렛별로 쌓아둔 정적 딕셔너리 상태를 정리한다.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.DeSpawn))]
    public static class Patch_Pawn_DeSpawn_TurretCleanup
    {
        static void Prefix(Pawn __instance)
        {
            if (!TurretAmmoSupplyUtility.IsTargetPawn(__instance) || __instance.AllComps == null)
                return;

            foreach (ThingComp comp in __instance.AllComps)
            {
                if (comp is CompTurretGun turret)
                    Patch_CompTurretGun_Tick.Cleanup(turret);
            }
        }
    }
}
