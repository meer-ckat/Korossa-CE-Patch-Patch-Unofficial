using System;
using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using CombatExtended.Compatibility;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace KorossaCEPatch
{
    /// <summary>
    /// [DefOf] 바인딩: Defs/JobDefs/Job_FlareFlareFlare.xml에 정의된
    /// KOR_SACLOSSmoke JobDef를 정적 필드로 참조하기 위한 표준 RimWorld 패턴.
    /// Harmony 패치는 아니다.
    /// </summary>
    [DefOf]
    public static class KOR_JobDefOf
    {
        public static JobDef KOR_SACLOSSmoke;
        static KOR_JobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(KOR_JobDefOf));
    }

    public class SACLOSInterrupter : DefModExtension
    {
        public bool canInterrupt = false;
    }

    /// <summary>
    /// KOR_SACLOSSmoke JobDef의 driverClass. 유도미사일(SACLOS) 피격 위협을 받은
    /// 폰이 연막으로 대응하는 행동을 여러 Toil로 구성한다:
    /// (1) 주변에 이미 깔린 연막으로 전력 질주, (2) 착용 중인 연막 벨트 사용,
    /// (3) 그마저 없으면 인벤토리/장비의 연막발사기로 무기를 임시 교체해 발사.
    /// 잡 종료 시(성공/실패 무관) 원래 무기를 되돌려주는 정리 로직을 포함한다.
    /// </summary>
    public class JobDriver_SACLOSInterruptSmoke : JobDriver
    {
        public static bool canInterruptCompThing(object thing)
        {
            if (thing == null)
                return false;

            if (thing is Apparel apparel)
            {
                var reloadable = apparel.TryGetComp<CompApparelReloadable>();
                if (reloadable == null)
                    return false;
                if (!reloadable.CanBeUsed(out _))
                    return false;
                if (reloadable.VerbTracker?.AllVerbs?.Any(v => v is Verb_SmokePop) != true)
                    return false;
                return true;
            }

            if (thing is ThingWithComps weapon)
            {
                if (weapon.def == null)
                    return false;

                if (InterruptWeapon.Contains(weapon.def.defName))
                    return true;

                SACLOSInterrupter extension = weapon.def.GetModExtension<SACLOSInterrupter>();
                return extension != null && extension.canInterrupt;
            }

            return false;
        }

        public static readonly string[] InterruptWeapon = //하드코딩 fallback
        {
            "Weapon_SmokeLauncher",
            "CE_Weapon_GrenadeSmoke"
        };

        private ThingWithComps oldWeapon;
        private ThingWithComps smokeLauncher;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref oldWeapon, "oldWeapon");
            Scribe_References.Look(ref smokeLauncher, "smokeLauncher");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFinishAction((jobCondition) =>
            {
                RestoreWeaponIfSwapped();
            });


            Toil gotoDodgeCell = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell); //LEEEEEROOOOOOOOY
            gotoDodgeCell.AddPreInitAction(() => 
            {
                if (pawn.jobs.curJob != null)
                {
                    pawn.jobs.curJob.locomotionUrgency = LocomotionUrgency.Sprint;
                }
            });

            // 이 Toil의 initAction 안에서 A→B→C 순서로 대응 수단을 탐색하고,
            // 찾으면 즉시 JumpToToil로 회피 이동 Toil로 건너뛴다(우선순위: 이미 있는
            // 연막으로 도피 > 연막벨트 사용 > 연막발사기로 무기 교체 후 발사).
            Toil decisionToil = new Toil();
            decisionToil.initAction = () =>
            {
                // A. 주변 연막 스캔
                float maxRadius = 15f;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, maxRadius, useCenter: true))
                {
                    if (!cell.InBounds(pawn.Map)) continue;
                    if (pawn.Map.gasGrid.DensityAt(cell, GasType.BlindSmoke) > 0 && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some))
                    {
                        this.job.SetTarget(TargetIndex.A, cell);
                        this.JumpToToil(gotoDodgeCell);
                        return;
                    }
                }

                if (pawn.apparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (!canInterruptCompThing(apparel))
                            continue;

                        var reloadable = apparel.TryGetComp<CompApparelReloadable>();
                        Verb_SmokePop.Pop(reloadable);

                        IntVec3 dodge = CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 3);
                        this.job.SetTarget(TargetIndex.A, dodge);
                        this.JumpToToil(gotoDodgeCell);
                        return;
                    }
                }

                ////////인벤토리를 뒤질 차례

                pawn.inventory.TryGetAllWeaponsInInventory(out var weapons);
                smokeLauncher = weapons.FirstOrDefault(canInterruptCompThing) as ThingWithComps;

                if (pawn.equipment.Primary != null && canInterruptCompThing(pawn.equipment.Primary))
                {
                    smokeLauncher = pawn.equipment.Primary;
                }

                if (smokeLauncher == null)
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
            };
            decisionToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return decisionToil;

            Toil swapWeaponToil = new Toil
            {
                initAction = () =>
                {
                    if (pawn.equipment.Primary != smokeLauncher)
                    {
                        oldWeapon = pawn.equipment.Primary;

                        if (oldWeapon != null)
                        {
                            bool transferred =
                                pawn.equipment.TryTransferEquipmentToContainer(
                                    oldWeapon,
                                    pawn.inventory.innerContainer
                                );

                            if (!transferred)
                            {
                                oldWeapon = null;
                                EndJobWith(JobCondition.Incompletable);
                                return;
                            }
                        }

                        if (!pawn.inventory.innerContainer.Contains(smokeLauncher))
                        {
                            RestoreWeaponIfSwapped();
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }

                        if (!pawn.inventory.innerContainer.Remove(smokeLauncher))
                        {
                            RestoreWeaponIfSwapped();
                            EndJobWith(JobCondition.Incompletable);
                            return;
                        }

                        pawn.equipment.AddEquipment(smokeLauncher);

                        if (pawn.equipment.Primary != smokeLauncher)
                        {
                            // AddEquipment 실패 또는 예상치 못한 상태
                            if (smokeLauncher.ParentHolder == null)
                                pawn.inventory.innerContainer.TryAdd(smokeLauncher);

                            RestoreWeaponIfSwapped();
                            EndJobWith(JobCondition.Incompletable);
                        }
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return swapWeaponToil;

            Toil fireToil = new Toil
            {
                initAction = () =>
                {
                    Verb v = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                    v?.TryStartCastOn(new LocalTargetInfo(pawn.Position)); // 원래 님이 짜셨던 코드 복구! //gemini 개지랄;;
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return fireToil;

            Toil waitWarmupToil = new Toil();
            waitWarmupToil.initAction = () =>
            {
                int warmupTicks = 60;
                Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                if (verb != null) warmupTicks = Mathf.CeilToInt(verb.verbProps.warmupTime * 60f) + 30;
                waitWarmupToil.defaultDuration = warmupTicks;
            };
            waitWarmupToil.defaultCompleteMode = ToilCompleteMode.Delay;
            yield return waitWarmupToil;

            Toil setDodgeTargetToil = new Toil
            {
                initAction = () =>
                {
                    IntVec3 dodge = CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 3);
                    this.job.SetTarget(TargetIndex.A, dodge);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
            yield return setDodgeTargetToil;

            yield return gotoDodgeCell;

            yield return Toils_General.Wait(60); 
        }

        private void RestoreWeaponIfSwapped()
        {
            if (oldWeapon == null || smokeLauncher == null || oldWeapon == smokeLauncher) 
                return;
            if (pawn == null || pawn.equipment == null || pawn.inventory == null || pawn.Dead) 
                return;

            if (pawn.equipment.Primary == smokeLauncher)
            {
                pawn.equipment.TryTransferEquipmentToContainer(smokeLauncher, pawn.inventory.innerContainer);
            }
            if (pawn.inventory.innerContainer.Contains(oldWeapon))
            {
                pawn.inventory.innerContainer.Remove(oldWeapon);
                pawn.equipment.AddEquipment(oldWeapon);
            }

            
        }
    }

    /// <summary>
    /// 유도미사일(SACLOS) 조향 로직(GuidedMissileTrajectoryWorker)이 매틱 호출하는
    /// 정적 진입점. Harmony 패치가 아니라 일반 정적 유틸 클래스이며, 피격 대상이
    /// 사용 가능한 연막 대응 수단(어빌리티 우선, 없으면 Job)을 찾아 실행시킨다.
    /// 대상별 쿨다운(_cd, 30초)을 두어 매틱 재호출로 인한 중복 반응을 막는다.
    /// </summary>
    public class SACLOSInterruptSmokeAI
    {
        private static readonly HashSet<string> SmokeAbilityDefs = new HashSet<string> //분명 연막을 쓰게하는 어빌리티가 있을텐데 도저히 모르겠어서 걍...
        {
            // 코로사 해병 런치박스의 투사식 연막탄
            "KRS_mechsmoke",
            // Royalty smokepop psycast
            "Smokepop",
            // Biotech 메카노이드 자체 연막
            "SmokepopMech",
            // Sheridan 차체 고각 연막탄 발사기
            "KOR_Sheridan_SmokeDischarger"
        };

        private static Game cachedGame;
        private static readonly Dictionary<int, int> _cd = new Dictionary<int, int>();
        private const int CooldownTicks = 1800; // 30초

        // GuidedMissileTrajectoryWorker.ReactiveAcceleration에서 FlightTicks==1일 때
        // 딱 한 번 호출된다. target(피격 예정자)이 반응할 수 있는 상태인지 여러
        // 조건으로 걸러낸 뒤, 어빌리티 기반 연막 -> Job 기반 연막 순으로 시도한다.
        public static void TryReact(Pawn target, Thing launcher)
        {
            if (!ReferenceEquals(cachedGame, Current.Game))
            {
                _cd.Clear();
                cachedGame = Current.Game;
            }

            if (target == null || !target.Spawned || target.Dead || target.Downed) return;
            // 코로사 해병 런치박스도 자체 연막 능력을 사용하므로 메카노이드는 제외하지 않는다.
            if (target.RaceProps.Animal) return;
            // if (target.Faction == Faction.OfPlayer) return;
            if (launcher != null && !target.HostileTo(launcher)) return;
            if (target.CurJobDef == KOR_JobDefOf.KOR_SACLOSSmoke) return; 

            if (target.Map.gasGrid.DensityAt(target.Position,GasType.BlindSmoke) > 0)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            int pawnId = target.thingIDNumber;

            // _cd는 "SACLOS에 맞아본 폰"당 엔트리 1개고 게임 전환 시 통째로 비우므로
            // 별도 주기 청소가 필요할 만큼 자라지 않는다.
            if (_cd.TryGetValue(pawnId, out int until) && now < until) return;

            // 장비 교체 Job보다 능력을 우선한다. Ability.GetJob을 사용해야
            // 워밍업, 쿨다운, psyfocus/entropy 같은 바닐라 비용 처리가 보존된다.
            if (TryStartSmokeAbility(target, launcher))
            {
                _cd[pawnId] = now + CooldownTicks;
                return;
            }

            // 대응 수단 사전 검사는 하지 않는다. JobDriver의 decisionToil이 같은
            // A/B/C 탐색을 다시 하고, 없으면 EndJobWith(Incompletable)로 즉시 끝낸다.
            Job job = JobMaker.MakeJob(KOR_JobDefOf.KOR_SACLOSSmoke);
            target.jobs.StartJob(job, JobCondition.InterruptForced);

            _cd[pawnId] = now + CooldownTicks;
        }

        // 대상이 보유한 연막 관련 어빌리티(KRS_mechsmoke/Smokepop/SmokepopMech/
        // KOR_Sheridan_SmokeDischarger) 중 사용 가능한 것을 Ability.GetJob으로
        // 시작한다. Job을 직접 만들지 않고 Ability API를 거치는 이유는 워밍업/
        // 쿨다운/정신력 소모 같은 바닐라 비용 처리를 그대로 보존하기 위함.
        private static bool TryStartSmokeAbility(Pawn pawn, Thing launcher)
        {
            if (pawn?.abilities?.AllAbilitiesForReading == null)
                return false;

            foreach (Ability ability in pawn.abilities.AllAbilitiesForReading)
            {
                if (ability?.def == null || !SmokeAbilityDefs.Contains(ability.def.defName))
                    continue;

                if (!ability.CanCast)
                    continue;

                LocalTargetInfo castTarget;

                if (ability.def.defName == "SmokepopMech")
                {
                    // 자기 자신에게 사용하는 연막
                    castTarget = new LocalTargetInfo(pawn);
                }
                else if (launcher != null)
                {
                    // Pawn -> 미사일 발사자 방향
                    Vector3 dir = (launcher.DrawPos - pawn.DrawPos).normalized;

                    IntVec3 targetDir = new IntVec3(
                        Mathf.RoundToInt(dir.x),
                        0,
                        Mathf.RoundToInt(dir.z)
                    );

                    // 너무 작은 벡터가 반올림되어 (0,0,0)이 되는 것 방지
                    if (targetDir == IntVec3.Zero)
                    {
                        targetDir = (launcher.Position - pawn.Position);
                        
                        if (targetDir.x != 0)
                            targetDir.x = targetDir.x > 0 ? 1 : -1;

                        if (targetDir.z != 0)
                            targetDir.z = targetDir.z > 0 ? 1 : -1;
                    }

                    castTarget = new LocalTargetInfo(
                        pawn.Position + targetDir * 3
                    );
                }
                else
                {
                    // launcher 정보가 없으면 자기 위치 fallback
                    castTarget = new LocalTargetInfo(pawn.Position);
                }

                if (!ability.CanApplyOn(castTarget))
                    continue;

                Job abilityJob = ability.GetJob(
                    castTarget,
                    LocalTargetInfo.Invalid
                );

                if (abilityJob == null || !abilityJob.CanBeginNow(pawn))
                    continue;

                pawn.jobs.StartJob(
                    abilityJob,
                    JobCondition.InterruptForced
                );

                return true;
            }

            return false;
        }
    }
}
