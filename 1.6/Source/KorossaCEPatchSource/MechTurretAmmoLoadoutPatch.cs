using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KorossaCEPatch
{
    /// <summary>
    /// 메크에 마운트된 터렛 총(로켓포드 등)의 탄약을 CE 로드아웃(터렛별 소지 탄창 수)
    /// 시스템에 통합하기 위한 헬퍼(패치 아님).
    /// CE는 About.xml의 필수 의존성이므로 CompMechAmmo/MTAAmmoUtility를 직접 참조한다.
    /// </summary>
    public static class MechTurretAmmoUtility
    {
        public const int DefaultTurretMagazines = 2;

        public static JobDef TakeAmmoJobDef => DefDatabase<JobDef>.GetNamedSilentFail("MTA_TakeAmmo");

        // 폰에 붙은 터렛 총들의 CE CompAmmoUser 목록
        public static List<CompAmmoUser> GetTurretAmmoUsers(Pawn pawn)
        {
            if (pawn?.AllComps == null)
                return new List<CompAmmoUser>();

            return pawn.AllComps
                .OfType<CompTurretGun>()
                .Select(t => TurretAmmoSupplyUtility.GetAmmoUser(TurretAmmoSupplyUtility.GetTurretGun(t)))
                .Where(au => au != null && au.UseAmmo)
                .ToList();
        }

        // 로드아웃 조회 (미설정이면 기본값 등록 후 반환)
        public static int GetOrRegisterLoadout(CompMechAmmo mechAmmo, AmmoDef ammoDef)
        {
            Dictionary<AmmoDef, int> loadouts = mechAmmo?.Loadouts;
            if (loadouts == null || ammoDef == null)
                return DefaultTurretMagazines;

            int magCount;
            if (loadouts.TryGetValue(ammoDef, out magCount))
                return magCount;

            loadouts[ammoDef] = DefaultTurretMagazines;
            return DefaultTurretMagazines;
        }

        public static void SetLoadout(CompMechAmmo mechAmmo, AmmoDef ammoDef, int magCount)
        {
            Dictionary<AmmoDef, int> loadouts = mechAmmo?.Loadouts;
            if (loadouts == null || ammoDef == null)
                return;

            loadouts[ammoDef] = magCount < 0 ? 0 : magCount;
        }
    }

    // -------------------------------------------------------------------------
    // 1) TryMakeAmmoJob 확장: 터렛 탄약도 로드아웃 수량만큼 가져오기
    // -------------------------------------------------------------------------
    /// <summary>
    /// 대상: CE <see cref="CompMechAmmo.TryMakeAmmoJob"/> (Postfix).
    /// CE의 기본 로직은 폰의 주무기 탄약 보급만 처리하므로, 대상 메크가 마운트
    /// 터렛(로켓포드 등)도 갖고 있다면 그 터렛용 탄약도 등록된 로드아웃 수량만큼
    /// 함께 가져오도록(MTA_TakeAmmo Job 발급) 확장한다.
    /// </summary>
    [HarmonyPatch(typeof(CompMechAmmo), nameof(CompMechAmmo.TryMakeAmmoJob))]
    public static class Patch_CompMechAmmo_TryMakeAmmoJob
    {
        static void Postfix(CompMechAmmo __instance, bool forced)
        {
            Pawn pawn = __instance?.parent as Pawn;
            if (pawn == null || pawn.Faction != Faction.OfPlayer)
                return;

            if (!TurretAmmoSupplyUtility.IsTargetPawn(pawn))
                return;

            if (!forced && pawn.Drafted)
                return;

            JobDef takeAmmoDef = MechTurretAmmoUtility.TakeAmmoJobDef;
            if (takeAmmoDef == null || pawn.CurJobDef == takeAmmoDef)
                return;

            foreach (CompAmmoUser ammoUser in MechTurretAmmoUtility.GetTurretAmmoUsers(pawn))
            {
                int magSize = ammoUser.MagSize;
                if (magSize <= 0)
                    continue;

                foreach (AmmoDef ammoDef in TurretAmmoSupplyUtility.GetAmmoSetDefs(ammoUser))
                {
                    int magCount = MechTurretAmmoUtility.GetOrRegisterLoadout(__instance, ammoDef);
                    if (magCount <= 0)
                        continue;

                    int need = MTAAmmoUtility.NeedAmmo(ammoUser, ammoDef, magSize * magCount);
                    if (need <= 0)
                        continue;

                    Thing best = MTAAmmoUtility.FindBestAmmo(pawn, ammoDef);
                    if (best == null)
                        continue;

                    Job job = JobMaker.MakeJob(takeAmmoDef, best);
                    job.count = need;
                    if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc, true))
                        return;
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // 2) '터렛 탄창 설정' 기즈모 추가
    // -------------------------------------------------------------------------
    /// <summary>
    /// 대상: CE <see cref="CompMechAmmo.CompGetGizmosExtra"/> (Postfix).
    /// 대상 메크가 터렛 탄약을 쓰면 원본 기즈모 목록 끝에 "터렛 탄창 설정" 버튼을
    /// 추가해 Dialog_TurretMagCount를 열 수 있게 한다.
    /// </summary>
    [HarmonyPatch(typeof(CompMechAmmo), nameof(CompMechAmmo.CompGetGizmosExtra))]
    public static class Patch_CompMechAmmo_CompGetGizmosExtra
    {
        static void Postfix(CompMechAmmo __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance?.parent as Pawn;
            if (pawn == null || pawn.Faction != Faction.OfPlayer)
                return;

            if (!TurretAmmoSupplyUtility.IsTargetPawn(pawn))
                return;

            List<CompAmmoUser> turretAmmoUsers = MechTurretAmmoUtility.GetTurretAmmoUsers(pawn);
            if (turretAmmoUsers.Count == 0)
                return;

            CompMechAmmo mechAmmo = __instance;
            Command_Action gizmo = new Command_Action
            {
                defaultLabel = "터렛 탄창 설정",
                defaultDesc = "터렛(로켓포드) 탄약을 몇 탄창 분량 소지할지 설정합니다.",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/SetMagCount", false),
                action = () => Find.WindowStack.Add(new Dialog_TurretMagCount(pawn, mechAmmo, turretAmmoUsers))
            };

            __result = (__result ?? Enumerable.Empty<Gizmo>()).Concat(new[] { gizmo });
        }
    }

    // -------------------------------------------------------------------------
    // 터렛 탄창 설정 다이얼로그 (CE Dialog_SetMagCount와 동일한 조작감)
    // -------------------------------------------------------------------------
    /// <summary>
    /// 터렛별/탄약별 소지할 탄창 수(로드아웃)를 +/- 버튼으로 조정하는 창.
    /// "확인" 클릭 시 각 row의 magCount를 로드아웃에 반영하고 즉시 재보급을 트리거한다.
    /// </summary>
    public class Dialog_TurretMagCount : Window
    {
        private class Row
        {
            public AmmoDef ammoDef;
            public int magSize;
            public int magCount;
        }

        private readonly Pawn pawn;
        private readonly CompMechAmmo mechAmmo;
        private readonly List<Row> rows = new List<Row>();

        public override Vector2 InitialSize => new Vector2(320f, (rows.Count + 3) * 33f + 20f);

        public Dialog_TurretMagCount(Pawn pawn, CompMechAmmo mechAmmo, List<CompAmmoUser> turretAmmoUsers)
        {
            this.pawn = pawn;
            this.mechAmmo = mechAmmo;

            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
            forcePause = false;

            foreach (CompAmmoUser ammoUser in turretAmmoUsers)
            {
                foreach (AmmoDef ammoDef in TurretAmmoSupplyUtility.GetAmmoSetDefs(ammoUser))
                {
                    rows.Add(new Row
                    {
                        ammoDef = ammoDef,
                        magSize = ammoUser.MagSize,
                        magCount = MechTurretAmmoUtility.GetOrRegisterLoadout(mechAmmo, ammoDef)
                    });
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead)
            {
                Close(true);
                return;
            }

            float curY = 0f;

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(inRect.x, curY, inRect.width, 30f), "터렛 탄창 설정 (탄창당 발수는 터렛별)");
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 33f;

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                Widgets.DefIcon(new Rect(inRect.x, curY, 30f, 30f), row.ammoDef);
                Widgets.Label(new Rect(inRect.x + 33f, curY + 7.5f, inRect.width - 153f, 30f),
                    row.ammoDef.label + " x" + row.magSize);

                if (Widgets.ButtonText(new Rect(inRect.x + inRect.width - 120f, curY, 30f, 30f), "-"))
                    row.magCount -= GenUI.CurrentAdjustmentMultiplier();

                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inRect.x + inRect.width - 90f, curY, 60f, 30f), row.magCount.ToString());
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonText(new Rect(inRect.x + inRect.width - 30f, curY, 30f, 30f), "+"))
                    row.magCount += GenUI.CurrentAdjustmentMultiplier();

                if (row.magCount < 0)
                    row.magCount = 0;

                curY += 33f;
            }

            curY += 3f;
            if (Widgets.ButtonText(new Rect(inRect.x, curY, inRect.width, 30f), "확인"))
            {
                for (int i = 0; i < rows.Count; i++)
                    MechTurretAmmoUtility.SetLoadout(mechAmmo, rows[i].ammoDef, rows[i].magCount);

                mechAmmo.TakeAmmoNow();
                Close(true);
            }
        }
    }
}
