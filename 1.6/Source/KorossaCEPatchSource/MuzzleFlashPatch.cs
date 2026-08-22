using System;
using CombatExtended;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace KorossaCEPatch
{
    /// <summary>
    /// 무기 ThingDef에 붙여 CE 발사 시 재생할 총구화염 EffecterDef를 지정한다.
    ///
    /// 원본 Korossa는 총구화염을 발사체(Bullet_*) ThingDef의
    /// CompProperties_ProjectileEffecter로 구현했다. 그런데 CE의 ProjectileCE는
    /// 바닐라 Verse.Projectile을 상속하지 않으므로 그 comp가 아예 호출되지 않고,
    /// 게다가 CE 변환은 defaultProjectile을 CE 표준 탄약(Bullet_762x51mmNATO_FMJ 등)으로
    /// 바꾸기 때문에 원본 발사체 자체를 더 이상 쏘지 않는다.
    /// 결과적으로 CE 패치 적용 후 원본 총구화염이 전부 사라졌다.
    ///
    /// 그래서 "탄약에 붙은 연출"을 "무기에 붙은 연출"로 옮긴다. 이게 개념적으로도
    /// 총구화염의 올바른 소속이고, CE 표준 탄약을 공유하는 다른 모드 무기에
    /// 코로사 화염이 묻어나는 부작용도 없다.
    /// </summary>
    public class KorossaMuzzleFlashExtension : DefModExtension
    {
        /// <summary>재생할 Effecter. 원본 KOR_MuzzleFlash / KOR_SLMuzzleFlash 등.</summary>
        public EffecterDef effecter;

        /// <summary>연출 크기 배율. 기본 1.0.</summary>
        public float scale = 1f;

        /// <summary>
        /// 총구까지의 거리(칸)를 이 값으로 덮어쓴다. 음수면 EffecterDef의
        /// offsetTowardsTarget을 그대로 쓴다(기본).
        ///
        /// 원본 EffecterDef의 offsetTowardsTarget은 보병 총기 스프라이트(약 1칸)를
        /// 기준으로 잡혀 있다(1.2 / 1.6 / 2.0 / 3.1). 총기 스프라이트 길이나 렌더링
        /// 방식이 다른 무기 - 특히 CompProperties_TurretGun으로 폰 몸통 중심에
        /// 작게 그려지는 차량 거치형 - 는 화염이 총구에서 한참 떨어져 뜬다.
        /// 그런 무기만 이 값으로 실제 총구 위치에 맞춘다.
        /// </summary>
        public float offsetOverride = -1f;

        /// <summary>조준 방향 기준 좌우 보정(칸). +는 오른쪽, -는 왼쪽. 동축/측면 거치용.</summary>
        public float sideOffset = 0f;
    }

    /// <summary>
    /// CE 사격 1회마다 무기의 KorossaMuzzleFlashExtension에 지정된 Effecter를 재생한다.
    ///
    /// 후크 지점을 Verb_LaunchProjectileCE.TryCastShot으로 잡은 이유:
    ///  - Verb_ShootCE / Verb_ShootCEOneUse / Verb_LaunchProjectileStaticCE 등 CE의
    ///    모든 사격 Verb가 이 클래스를 상속하고 base.TryCastShot()을 호출한다.
    ///    (터렛의 Building_TurretGunCE도 같은 Verb를 쓴다)
    ///  - ProjectileCE.Launch에 걸면 산탄총 펠릿 1발마다 화염이 터진다.
    ///    TryCastShot은 "1발"과 1:1이라 연사 중에도 원본과 같은 빈도가 나온다.
    /// </summary>
    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.TryCastShot))]
    public static class Patch_VerbLaunchProjectileCE_MuzzleFlash
    {
        private static bool errorLogged;

        [HarmonyPostfix]
        public static void Postfix(Verb_LaunchProjectileCE __instance, bool __result)
        {
            // 실제로 발사에 성공한 경우에만. (탄약 부족/사거리 밖 등은 false)
            if (!__result)
            {
                return;
            }

            try
            {
                ThingWithComps equipment = __instance.EquipmentSource;
                if (equipment == null)
                {
                    return;
                }

                KorossaMuzzleFlashExtension ext =
                    equipment.def.GetModExtension<KorossaMuzzleFlashExtension>();

                if (ext == null || ext.effecter == null)
                {
                    return;
                }

                Thing shooter = __instance.Caster;
                if (shooter == null || !shooter.Spawned)
                {
                    return;
                }

                Map map = shooter.Map;
                if (map == null)
                {
                    return;
                }

                // A = 사수(총구 위치의 기준), B = 조준 대상.
                // EffecterDef.offsetTowardsTarget이 A에서 B 방향으로 밀어내므로
                // 원본과 동일하게 총구 앞쪽에 화염이 뜬다.
                TargetInfo source = new TargetInfo(shooter);
                LocalTargetInfo target = __instance.CurrentTarget;
                TargetInfo aim = target.IsValid ? target.ToTargetInfo(map) : source;

                Effecter effecter = ext.effecter.Spawn();
                effecter.scale = ext.scale > 0f ? ext.scale : 1f;

                // SubEffecter_Sprayer는 스폰 위치를 다음처럼 계산한다.
                //     A.CenterVector3
                //   + (B.Center - A.Center).normalized * def.offsetTowardsTarget
                //   + parent.offset
                // 즉 Effecter.offset에 원하는 보정을 직접 실어 주면 된다.
                if (ext.offsetOverride >= 0f || ext.sideOffset != 0f)
                {
                    Vector3 dir = aim.CenterVector3 - source.CenterVector3;
                    dir.y = 0f;

                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        dir = dir.normalized;

                        // EffecterDef가 이미 offsetTowardsTarget만큼 밀어내므로 그 차이만 보정한다.
                        float forward = (ext.offsetOverride >= 0f)
                            ? ext.offsetOverride - ext.effecter.offsetTowardsTarget.Average
                            : 0f;

                        // RimWorld 평면은 x=동, z=북. 조준 방향 기준 오른쪽 벡터.
                        Vector3 right = new Vector3(dir.z, 0f, -dir.x);

                        effecter.offset = dir * forward + right * ext.sideOffset;
                    }
                }

                // SubEffecter_SprayerTriggered는 1회성이므로 Trigger 후 바로 Cleanup.
                effecter.Trigger(source, aim);
                effecter.Cleanup();
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    errorLogged = true;
                    Log.Error(
                        "[Korossa CE Patch by starlellok] muzzle flash effecter failed (이 메시지는 1회만 표시됨):\n" + ex);
                }
            }
        }
    }
}
