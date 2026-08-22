using UnityEngine;
using Verse;
using CombatExtended;
using System.Collections.Generic;
using RimWorld;
using System.Linq;
using System;
using HarmonyLib;

namespace KorossaCEPatch
{

    public class GuidedMissileExtension : DefModExtension
    {
        public float turnRateDegPerTick = 3f;

        public int guidanceDelayTicks = 3;
        public float aheadDegree = 60f;
        public bool onlyWhenAhead = true;

        //카나드 펼쳐지는 미사일용. ShillelaghMissileVisual 부분 보면 됨.
        public int deploymentTicks = 12;
        public string launchGraphicPath;
        public string flightGraphicPath;
    }

    //유도중에는 움직이지 못하도록 Busy 걸어버림.
    public class Stance_Guiding : Stance_Busy
    {
        public Stance_Guiding() { }
        public Stance_Guiding(int ticks, LocalTargetInfo focus, Verb verb) : base(ticks, focus, verb) { }
    }

    //SACLOS
    public class GuidedMissileTrajectoryWorker : BallisticsTrajectoryWorker
    {
        public static Dictionary<int, Pawn> 캐시된포수폰들 = new Dictionary<int, Pawn>();

        public override bool GuidedProjectile => true; //BaseTrajectoryWorker의 GuidedProjectile 활성화 

        // CE BallisticsTrajectoryWorker의 확장 지점 오버라이드(매틱 호출).
        // 발사체의 속도 벡터를 표적 방향으로 서서히 회전시켜 유도 비행을 구현하고,
        // 그 과정에서 사수의 제압/은폐 상태, 플레이어 조작 여부, 연막 차단 등
        // 다양한 조건에 따라 유도를 일시 중단하거나 완전히 취소한다.
        protected override void ReactiveAcceleration(ProjectileCE projectile)
        {
            if (projectile == null)
                return;

            int projectileId = projectile.thingIDNumber;

            // 사수 캐싱. 키가 있으면(값이 null이어도) 다시 만들지 않는다.
            // CleanupProjectile이 값을 null로 바꿔 "이 발사체는 유도 끝"을 표시하므로,
            // 여기서 무조건 재캐싱하면 매 틱 유도 재개 -> 즉시 정리가 반복돼
            // "Reloading!" 모트가 스팸된다. 실제 Remove는 Thing.Destroy에서 한다.
            if (!캐시된포수폰들.TryGetValue(projectileId, out Pawn 포수폰))
            {
                포수폰 =
                    projectile.launcher as Pawn ??
                    projectile.launcher?.TryGetComp<CompMannable>()?.ManningPawn;

                캐시된포수폰들[projectileId] = 포수폰;
            }

            if (projectile.FlightTicks == 1)
            {
                try
                {
                    SACLOSInterruptSmokeAI.TryReact(
                        projectile.intendedTarget.Thing as Pawn,
                        projectile.launcher);

                    if (포수폰 != null)
                        MoteMaker.ThrowText(
                            포수폰.DrawPos, 포수폰.Map,
                            "Back Blast Area Clear!", Color.white);
                }
                catch (Exception e)
                {
                    Log.WarningOnce("[KorossaCEPatchPatch] SACLOS Launch protocol failed: " + e, projectileId ^ 0x5AC105);
                }
            }

            if (포수폰 == null)
            {
                base.ReactiveAcceleration(projectile);
                return;
            }
                
            if(projectile.FlightTicks % 2 == 0) //최적화의 길은 험난하다
            {
                Thing 포수 = projectile.launcher;
                if (포수 == null || !포수.Spawned || 포수.Map == null)
                {
                    return;
                }

                if (포수폰 == null || !포수폰.Spawned)
                {
                    return;
                }
                
                var suppressComp = 포수폰.TryGetComp<CompSuppressable>();
                if (suppressComp != null)
                {
                    if (suppressComp.IsCrouchWalking)
                    {
                        return;
                    }

                    if (suppressComp.IsHunkering)
                    {
                        CleanupProjectile(projectile.thingIDNumber);
                        MoteMaker.ThrowText(포수폰.DrawPos, 포수폰.Map, "Lost Guidance!", Color.red);
                        return;
                    }
                }

                var curStance = 포수폰.stances.curStance;

                //Opus 이슈 때문에 소집된 폰은 조종을 못하는 개망나니 버그가 있었음. 그거 지움

                // 발사 무기의 원래 쿨다운은 그대로 진행시킨다.
                // 예전에는 ticksLeft를 계속 3으로 덮어써 비행 중 쿨다운을 멈춘 뒤,
                // CleanupProjectile에서 Stance_Mobile로 바꾸면서 남은 쿨다운까지
                // 취소했다. 쿨다운이 끝난 뒤에만 별도 유도 stance로 포수를 묶는다.
                if (curStance is Stance_Guiding guidingStance)
                {
                    guidingStance.ticksLeft = 3; //유도 끝날때까지 기다리기
                }
                else if (!(curStance is Stance_Cooldown) &&
                         !(curStance is Stance_Warmup))
                {
                    포수폰.stances.SetStance(new Stance_Guiding(3, null, null)); // 유도 끝날때까지 기다리게 하기 위해 Init
                }

                GuidedMissileExtension ext = projectile.def.GetModExtension<GuidedMissileExtension>(); //xml 가져오기
                float turnDeg = ext != null ? ext.turnRateDegPerTick : 3f;
                int delay = ext != null ? ext.guidanceDelayTicks : 3;
                bool onlyAhead = ext == null || ext.onlyWhenAhead;
                float aheadDeg = ext != null ? ext.aheadDegree : 60f;

                //발사 직후 관성 비행 구간에서는 조향 안함
                if (turnDeg <= 0f || projectile.FlightTicks < delay)
                {
                    return;
                }

                LocalTargetInfo target = projectile.intendedTarget;
                if (target.ThingDestroyed)
                {
                    CleanupProjectile(projectile.thingIDNumber);
                    return;
                }

                Thing targetThing = target.Thing;
                
                Vector3 targetPos = (targetThing != null)
                ? targetThing.DrawPos
                : target.Cell.ToVector3Shifted();
                targetPos.y = projectile.intendedTargetHeight; //이런 젼처로 값이 서로 사맛디 않아 빗나갈 수도 있음. 가끔 시각적으로는 미사일과 충돌했는데 미사일이 그냥 지나가는 현상이 있는데 이걸로 추정하는 중.

                // 시드가 projectileId 고정이라 어차피 매 틱 같은 값이 나온다.
                // new System.Random을 매 틱 할당하지 않도록 Rand.PushState로 대체.
                float spread = 1f / Mathf.Max(포수폰?.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 10f, 0.1f);
                Rand.PushState(projectileId);
                targetPos.x += Rand.Range(-spread, spread);
                targetPos.y += Rand.Range(-spread, spread);
                Rand.PopState();

                Vector3 toTarVector = targetPos - projectile.ExactPosition;
                if (toTarVector.sqrMagnitude < 0.0001f)
                { 
                    return;
                }

                float speed = projectile.velocity.magnitude;
                    if (speed < 0.00001f)
                    {
                        return;
                    }

                //앞에 있는 얘만 추적하게 하는거 
                if (onlyAhead)
                {
                    float cos = Mathf.Cos(Mathf.Clamp(aheadDeg, 0f, 180f) * Mathf.Deg2Rad);  //시야각

                    float dot = Vector3.Dot(projectile.velocity.normalized, toTarVector.normalized);
                    if (dot < cos)
                    {
                        return;
                    }
                }

                // "사수-표적 시야선(Line of Sight) 상에 연막(BlindSmoke)이 있으면
                // 유도 갱신을 멈춘다 - 연막 회피(SACLOSInterruptSmokeAI)가 실제로
                // 유도를 방해하는 대항수단으로 기능하도록 하기 위함." 라고함. 나는 모르는 소리임
                bool 연기로가로막힘 = false;
                IntVec3 포수위치 = 포수.Position;
                IntVec3 목표위치 = target.Cell; //????????
               
                foreach(IntVec3 점 in GenSight.PointsOnLineOfSight(포수위치, 목표위치)) //이것도 격셀로 하고 싶었는데 그러면 정확도가 너무 낮아질까봐 포기함
                {
                    if (!점.InBounds(포수.Map)) 
                        continue;

                    if (포수.Map.gasGrid.DensityAt(점, GasType.BlindSmoke) > 50)
                    {
                        연기로가로막힘 = true;
                        break;
                    }
                }

                if (연기로가로막힘)
                {
                    return;
                }

                Vector3 desired = toTarVector.normalized * speed;
                float maxRad = turnDeg * Mathf.Deg2Rad * 2;
                projectile.velocity = Vector3.RotateTowards(projectile.velocity, desired, maxRad, 0f);
            }
        }

        // 유도 취소/명중/파괴 시 호출되어 사수 상태를 정리한다. 이 발사체가
        // 마지막으로 유도 중이던 것이었다면(다른 발사체가 같은 사수를 아직 쓰지
        // 않으면) 사수를 Stance_Mobile로 되돌리고 "재장전 중" 텍스트를 표시한다.
        public static void CleanupProjectile(int projectileId)
        {

            // 값이 null이면 이미 정리된 발사체다. 여기서 한 번 더 처리하면
            // 매 틱 "Reloading!"이 다시 뜬다.
            if (!캐시된포수폰들.TryGetValue(projectileId, out Pawn 포수폰) || 포수폰 == null)
                return;

            // Remove가 아니라 null 마킹. Remove하면 다음 틱에 ReactiveAcceleration이
            // 사수를 다시 캐싱해 유도가 부활한다. 실제 삭제는 Thing.Destroy 프리픽스.
            캐시된포수폰들[projectileId] = null;

            bool stillGuiding = 캐시된포수폰들.Values.Any(posupawn => posupawn == 포수폰);

            if (!stillGuiding && 포수폰 != null && !포수폰.Destroyed && 포수폰.stances != null)
            {
                

                // 유도 때문에 생성한 stance만 해제
                if (포수폰.stances.curStance is Stance_Guiding)
                {
                    MoteMaker.ThrowText(포수폰.DrawPos, 포수폰.Map, "Reloading!", Color.white);
                    포수폰.stances.SetStance(new Stance_Mobile());
                }
            }
        }
    }

    //안전망
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class Patch_Thing_Destroy_GuidedMissileCleanup
    {
        static void Prefix(Thing __instance)
        {
            if (__instance is ProjectileCE projectile && GuidedMissileTrajectoryWorker.캐시된포수폰들.ContainsKey(projectile.thingIDNumber))
            {
                GuidedMissileTrajectoryWorker.CleanupProjectile(projectile.thingIDNumber);
                // 발사체가 실제로 사라지는 시점 = 딕셔너리에서 지워도 되는 유일한 시점.
                GuidedMissileTrajectoryWorker.캐시된포수폰들.Remove(projectile.thingIDNumber);
            }
        }
    }
}
