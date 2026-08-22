using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace KorossaCEPatch
{
    public class TankSmokeGrenadeExtension : DefModExtension
    {
        public int fuseTicks = 70;
        public float launchAngleDegrees = 45f;
        public float shotHeight = 10f;
        public int salvoCount = 4;
        public float salvoWidth = 3f;
        public int salvoIntervalTicks = 1;

        public float muzzleVelocity = 15f;
        public float launchRange = 5f;
    }

    public class TankSmokeGrenadeProjectile : ProjectileCE_Explosive
    {
        private int fuseTicksRemaining = 120;
        private int launchDelayTicksRemaining;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fuseTicksRemaining, "fuseTicksRemaining", 120);
            Scribe_Values.Look( ref launchDelayTicksRemaining, "launchDelayTicksRemaining", 0);
        }

        public override void Launch(
            Thing launcher,
            Vector2 origin,
            float shotAngle,
            float shotRotation,
            float shotHeight = 0f,
            float shotSpeed = -1f,
            Thing equipment = null,
            float distance = -1f)
        {
            base.Launch(
                launcher,
                origin,
                shotAngle,
                shotRotation,
                shotHeight,
                shotSpeed,
                equipment,
                distance);

            TankSmokeGrenadeExtension extension =
                def.GetModExtension<TankSmokeGrenadeExtension>();

            fuseTicksRemaining = Mathf.Max(1, extension?.fuseTicks ?? 120);
        }

        public override void Tick()
        {
            if (launchDelayTicksRemaining > 0)
            {
                launchDelayTicksRemaining--;
                return;
            }

            base.Tick();

            if (Destroyed || landed)
                return;

            fuseTicksRemaining--;
            if (fuseTicksRemaining <= 0)
                Impact(null);
        }

        public void SetLaunchDelay(int ticks)
        {
            launchDelayTicksRemaining = Mathf.Max(0, ticks);
        }

        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (launchDelayTicksRemaining > 0)
                return;
            base.DrawAt(drawLoc, flip);
        }
    }

    /// <summary>
    /// Ability 설정
    /// </summary>
    public class CompProperties_AbilityLaunchTankSmokeGrenade
        : CompProperties_AbilityEffect
    {
        public ThingDef projectileDef;

        public CompProperties_AbilityLaunchTankSmokeGrenade()
        {
            compClass = typeof(CompAbilityEffect_LaunchTankSmokeGrenade);
        }
    }

    /// <summary>
    /// 셰리든 연막 차장 어빌리티의 실제 발동 로직. Apply()에서 캐스터->표적 방향을
    /// 기준으로 좌우로 부채꼴 퍼진 salvoCount(기본4)발의 연막탄을 지연 발사한다.
    /// </summary>
    public class CompAbilityEffect_LaunchTankSmokeGrenade : CompAbilityEffect
    {
        public new CompProperties_AbilityLaunchTankSmokeGrenade Props => (CompProperties_AbilityLaunchTankSmokeGrenade)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent?.pawn;
            if (caster == null || caster.Map == null || Props.projectileDef == null)
                return;

            TankSmokeGrenadeExtension extension =
                Props.projectileDef.GetModExtension<TankSmokeGrenadeExtension>();

            float shotHeight = new CollisionVertical(caster).shotHeight;

            Vector2 origin = new Vector2(caster.DrawPos.x, caster.DrawPos.z);

            Vector3 tp = target.IsValid ? target.CenterVector3 : caster.Position.ToVector3Shifted(); //temp
            Vector2 targetPos = new Vector2(tp.x, tp.z);

            Vector2 delta = targetPos - origin;
            Vector2 forward = delta.sqrMagnitude > 0.01f
                ? delta.normalized
                : new Vector2(0f, 1f);
            Vector2 right = new Vector2(-forward.y, forward.x);
            float centerDistance = Mathf.Max(0.25f, delta.magnitude);
            int salvoCount = Mathf.Max(1, extension?.salvoCount ?? 4);
            float salvoWidth = Mathf.Max(0f, extension?.salvoWidth ?? 3f);
            int salvoIntervalTicks = Mathf.Max(
                0,
                extension?.salvoIntervalTicks ?? 1);


            // 셰리던은 4개의 연막 발사기가 있음
            for (int index = 0; index < salvoCount; index++)
            {
                float normalizedOffset = salvoCount == 1
                    ? 0f
                    : index / (float)(salvoCount - 1) - 0.5f;
                Vector2 salvoTarget = origin + forward * centerDistance +
                    right * (normalizedOffset * salvoWidth);
                LaunchGrenade(
                    caster,
                    target,
                    origin,
                    salvoTarget,
                    shotHeight,
                    extension,
                    index * salvoIntervalTicks);
            }
        }

        private void LaunchGrenade(
            Pawn caster,
            LocalTargetInfo intendedTarget,
            Vector2 origin,
            Vector2 targetHorizontal,
            float shotHeight,
            TankSmokeGrenadeExtension extension,
            int launchDelayTicks)
        {
            TankSmokeGrenadeProjectile projectile = GenSpawn.Spawn(
                Props.projectileDef,
                caster.Position,
                caster.Map) as TankSmokeGrenadeProjectile;

            if (projectile == null)
                return;

            Vector2 delta = (targetHorizontal - origin).normalized * Mathf.Max(0.25f, extension?.launchRange ?? 5f);
            float distance = delta.magnitude;

            Vector3 sourcePosition = new Vector3(origin.x, shotHeight, origin.y);
            Vector3 targetPosition = new Vector3(origin.x + delta.x, 0f, origin.y + delta.y);

            float angleRadians = Mathf.Clamp(extension?.launchAngleDegrees ?? 45f, 1f, 89f) * Mathf.Deg2Rad;
            float shotSpeed = Mathf.Max(0.1f, extension?.muzzleVelocity ?? 15f);

            float shotRotation = projectile.TrajectoryWorker.ShotRotation(
                projectile.Props,
                sourcePosition,
                targetPosition);

            projectile.intendedTarget = intendedTarget;
            projectile.Launch(
                caster,
                origin,
                angleRadians,
                shotRotation,
                shotHeight,
                shotSpeed,
                null,
                distance);
            projectile.SetLaunchDelay(launchDelayTicks);

        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return target.IsValid;
        }
    }

    /// <summary>
    /// PawnKind abilities are assigned when a pawn is generated.  Backfill the
    /// new ability for Sheridans already stored in an existing save.
    /// </summary>
    /// <summary>
    /// 대상: 바닐라 <see cref="Pawn.SpawnSetup"/> (Postfix).
    /// KOR_Sheridan_SmokeDischarger 어빌리티는 §2.17(Korossa_SheridanSmokeAbility.xml)
    /// 패치로 PawnKindDef에 추가되는데, 이는 신규 생성되는 폰에만 적용된다.
    /// 이미 저장된(기존 세이브의) 셰리든 폰에게는 어빌리티가 없으므로, 스폰 시
    /// 아직 이 어빌리티가 없다면 사후에 GainAbility로 보충 지급한다.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    public static class SheridanSmokeAbilityBackfillPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance)
        {
            if (__instance?.def?.defName != "Mech_Sheridan" ||
                __instance.abilities == null)
            {
                return;
            }

            AbilityDef abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail(
                "KOR_Sheridan_SmokeDischarger");

            if (abilityDef != null &&
                __instance.abilities.GetAbility(abilityDef) == null)
            {
                __instance.abilities.GainAbility(abilityDef);
            }
        }
    }
}
