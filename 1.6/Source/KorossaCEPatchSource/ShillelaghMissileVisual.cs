using CombatExtended;
using UnityEngine;
using Verse;

namespace KorossaCEPatch
{
    public class ShillelaghMissileProjectile : BulletCE
    {
        //AI 딸깍딸깍 끼얏호
        private Graphic launchGraphic;
        private Graphic flightGraphic;

        // GuidedMissileExtension.deploymentTicks를 기준으로 발사(launch) 그래픽과
        // 비행(flight) 그래픽 중 어느 것을 그릴지 결정한다.
        public override Graphic Graphic
        {
            get 
            {
                GuidedMissileExtension extension =
                    def.GetModExtension<GuidedMissileExtension>();

                if (extension == null)
                    return base.Graphic;

                bool deployed = FlightTicks >= extension.deploymentTicks;
                string path = deployed
                    ? extension.flightGraphicPath
                    : extension.launchGraphicPath;

                if (string.IsNullOrEmpty(path))
                    return base.Graphic;

                if (deployed)
                    return flightGraphic ?? (flightGraphic = MakeGraphic(path));

                return launchGraphic ?? (launchGraphic = MakeGraphic(path));
            }
        }

        private Graphic MakeGraphic(string path)
        {
            GraphicData data = def.graphicData;
            Shader shader = data.shaderType?.Shader ?? ShaderDatabase.MoteGlow;

            return GraphicDatabase.Get<Graphic_Single>(
                path,
                shader,
                data.drawSize,
                data.color,
                data.colorTwo);
        }

        /// <summary>
        /// ProjectileCE.DrawAt draws def.DrawMatSingle directly, bypassing this
        /// instance's virtual Graphic property. Draw with the phase-specific
        /// material here so launch -> flight can actually be seen in game.
        /// (한글: 베이스 ProjectileCE.DrawAt은 이 클래스의 Graphic 프로퍼티를
        ///  거치지 않고 def.DrawMatSingle을 직접 그리므로, 여기서 DrawAt 자체를
        ///  오버라이드해 위상별(launch/flight) 머티리얼로 다시 그려야 발사->비행
        ///  전환이 실제로 화면에 보인다.)
        /// 
        /// Graphic만 설정하지 말고 그걸 그려야 한다는 뜻인듯? 버퍼 개념
        /// 
        /// </summary>
        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (FlightTicks == 0 && launcher is Pawn)
                return;

            Quaternion shadowRotation = ExactRotation;
            Quaternion projectileRotation = DrawRotation;

            if (def.projectile.spinRate != 0f)
            {
                float spinPeriod = GenTicks.TicksPerRealSecond / def.projectile.spinRate;
                Quaternion spinRotation = Quaternion.AngleAxis(
                    Find.TickManager.TicksGame % spinPeriod / spinPeriod * 360f,
                    Vector3.up);

                shadowRotation *= spinRotation;
                projectileRotation *= spinRotation;
            }

            Graphics.DrawMesh(
                MeshPool.GridPlane(def.graphicData.drawSize),
                drawLoc,
                projectileRotation,
                Graphic.MatSingle,
                0);

            if (castShadow)
            {
                Vector3 shadowPos = new Vector3(
                    ExactPosition.x,
                    def.Altitude - 0.001f,
                    ExactPosition.z);

                Graphics.DrawMesh(
                    MeshPool.GridPlane(def.graphicData.drawSize),
                    shadowPos,
                    shadowRotation,
                    ShadowMaterial,
                    0);
            }

            Comps_PostDraw();
        }
    }

    /// <summary>
    /// CE projectile flecks normally start at launch. The Shillelagh must fly
    /// in its closed launch configuration first, so keep the fleck origin
    /// synchronized without emitting until the deployment tick.
    /// (한글: CompProperties_ProjectileFleck를 상속하는 CompProperties 서브클래스로,
    ///  compClass만 아래 CompProjectileFleckAfterDeployment로 바꿔치기한다. CE의
    ///  일반 확장 지점을 이용한 것이라 Harmony 패치는 아니다.)
    /// </summary>
    public class CompProperties_ProjectileFleckAfterDeployment
        : CompProperties_ProjectileFleck
    {
        public CompProperties_ProjectileFleckAfterDeployment()
        {
            compClass = typeof(CompProjectileFleckAfterDeployment);
        }
    }

    /// <summary>
    /// CE CompProjectileFleck을 상속해 CompTick()만 오버라이드한다.
    /// deploymentTicks 이전(발사관 이탈 전)에는 trail(fleck) 위치만 갱신하고
    /// 실제 방출은 하지 않아, 나중에 base.CompTick()이 첫 실행될 때 그 사이
    /// 구간을 역방향으로 이어 그리는 것을 방지한다.
    /// </summary>
    public class CompProjectileFleckAfterDeployment : CompProjectileFleck
    {
        public override void CompTick()
        {
            ProjectileCE projectile = parent as ProjectileCE;
            GuidedMissileExtension extension =
                parent.def.GetModExtension<GuidedMissileExtension>();

            if (projectile != null &&
                extension != null &&
                projectile.FlightTicks < extension.deploymentTicks)
            {
                // Prevent the first active tick from back-filling a trail over
                // the closed-launch portion of the flight path.
                lastPos = projectile.ExactPosition;
                return;
            }

            base.CompTick();
        }
    }
}
