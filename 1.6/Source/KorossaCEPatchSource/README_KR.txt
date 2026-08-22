CLAUDE OPUS랑 FABLE 딸깍딸깍해서 어떻게 고침. RIDER로 디컴파일 좀 해보니까 xml이랑 cs 감 좀 잡히는듯?

적용 내용
- KJW_AMAR_Kinetic -> .50 BMG
- KJW_ATM_EXO -> 20x102mm NATO
- KJW_AMR -> 20x102mm NATO
- KJW_AMHMG / KJW_HVMMG -> .50 BMG
- KJW_SG_EXO -> 12 Gauge
- KJW_Rsevenhundred -> 7.62x51mm NATO

핵심 수정
- Korossa 원본 커스텀 verb(Verb_ConditionalFire / Verb_ConditionalShotgunFire)를 먼저 제거
- 그 다음 CE Verb_ShootCE / AmmoUser로 다시 강제 패치
- 엑소슈트 착용 제한은 DLL/C# Harmony 패치가 담당

터렛 탄약 공급 (TurretAmmoSupplyPatch.cs)
- Mech_ADlunchbox 로켓포드(CompTurretGun + CE AmmoUser)는 CE가 탄약을 안 채워줌
  (LoadoutPropertiesExtension/CompMechAmmo는 주무기 전용) → C#으로 해결.
- 스폰 시: 탄창(6발) 가득 + 예비 120mm 로켓 18발(3탄창)을 인벤토리에 지급.
- 탄창이 비면 reloadTime(10초) 후 인벤토리의 120mm을 소모해 자동 재장전.
  인벤토리에 탄이 없으면 발사 중지, 재보급(CompAmmoGiver 등)하면 재개.
- 발사 windup: 터렛은 적 포착 즉시 발사함(내부 warmup 1틱 고정). verb warmupTime을
  쓰면 주무기와 충돌하므로, C#으로 burstWarmupTicksLeft를 조작해 교전 시작 시
  1.5초(WarmupTicks=90) 조준 후 첫 발이 나가게 함. 이후 사격은 정상 쿨다운.
- 대상 폰 추가는 TurretAmmoSupplyUtility.TargetPawnDefNames 배열에 defName 추가.

★ 마운트 터렛 warmupTime 규칙 (중요! - 발사준비/취소 무한반복 버그)
- 메크가 "주무기(equipment) + CompTurretGun 마운트 터렛"을 동시에 가지면,
  터렛 무기의 verb warmupTime을 반드시 0으로 둘 것.
  >0이면 터렛의 warmup이 폰 스탠스를 점유해 주무기가 발사준비/취소를 무한반복함.
- 해당 사례: Gun_rocketpod_lunchbox(ADlunchbox 주무기 .50과 충돌),
  Gun_coax_lunchbox(Sheridan 주무기 152mm과 충돌). 둘 다 warmupTime=0으로 수정됨.
- 반대로 HIBIKI/haebyung처럼 터렛 총이 곧 유일한 무기면 warmup>0 OK(충돌 상대 없음).
- 발사 간격은 warmupTime 대신 defaultCooldownTime / RangedWeapon_Cooldown으로 조절.

주의
- XML을 바꿨으면 기존 세이브에서 이미 생성된 무기 대신 새로 스폰한 무기로 테스트하는 게 안전함.
- 포함된 DLL은 C# 착용 제한 패치 + 터렛 탄약 공급 패치를 모두 포함한다.
  C# 파일을 다시 수정했다면 DLL도 다시 빌드해서 1.6/Assemblies에 덮어써야 함.
  (net472 클래스 라이브러리, 참조: Assembly-CSharp / UnityEngine.CoreModule / 0Harmony)


========================================================================
변경 이력 (Changelog)
========================================================================

[2026-07-27] (3)
버그 수정 - MechManualReloadGuardPatch.cs 코드 리뷰 지적 사항 2건
- MechPrimaryGunAvailabilityPatch.Postfix: `ammo.CurMagCount <= 0`을 검사하는
  if 블록이 중복으로 두 번 있었음. 첫 번째는 `ammo != null &&`로 null 체크가
  있지만 두 번째(중복)는 null 체크 없이 바로 `ammo.CurMagCount`에 접근해서
  CompAmmoUser가 없는 무기에 이 verb가 걸리면 NullReferenceException 위험이
  있었음. 중복 블록 제거하고 첫 번째 블록 안에서 로그+__result 처리로 통합.
- MechPrimaryGunTickReload.Postfix: "이미 장전됨" 판정을 `CurMagCount != 0`으로
  했는데, 다른 가드(MechManualReloadGuardPatch)는 전부 `CurMagCount > 0`을
  씀. CE가 CurMagCount를 음수로 만들 일은 정상적으론 없지만, 혹시 음수가 되면
  `!= 0`은 "장전됨"으로 오판해 영원히 재장전을 안 하는 조용한 먹통이 될 수
  있었음. `> 0`으로 통일.

[2026-07-27] (2)
버그 수정 - M2HB가 주무기(Carried, baseLayer=90)에 가려 안 보임
- 직전 커밋에서 baseLayer를 20(Diabolus 값)으로 되돌렸지만 여전히 안 보임 -
  실사용자 확인 결과 주무기(152mm, Carried 노드)가 M2HB를 덮고 있었음.
  Carried 기본 baseLayer=90이므로 20으로는 당연히 아래에 깔려 가려짐 -
  Diabolus는 애초에 주무기가 없어 90짜리 경쟁 레이어가 없었을 뿐, 20이라는
  숫자 자체가 Sheridan 상황에 맞는 값이 아니었음.
- 수정: baseLayer 20 -> 95 (Carried=90보다 위, Status overlay=100보다 아래)로
  변경. 이제 M2HB가 주무기보다 앞(위)에 그려져야 함.

[2026-07-27] (1)
정정 - (4)의 baseLayer=8 수정은 오진이었음, Assembly-CSharp.dll 디컴파일로 재확인
- 실제 문제는 계속 "M2HB 자체가 안 보임"이었는데(사용자가 반복 지적),
  전날 "20이 ApparelBody(20)와 겹쳐 주무기가 안 보인다"고 잘못 판단해서
  8로 낮췄던 것. 게임 코드(Assembly-CSharp.dll)를 ilspycmd로 직접 디컴파일해
  확인한 결과, 주무기(Carried) 노드의 실제 기본 baseLayer는 90
  (PawnRenderNodeProperties_Carried 생성자 확인) - 20/8 어느 쪽과도 안 겹침.
  즉 처음부터 주무기가 안 보인 적이 없었고 오진이었음.
- PawnRenderNode_TurretGun / PawnRenderNodeWorker_TurretGun / CompTurretGun
  소스를 모두 디컴파일해 확인했으나 "주무기 장착 시 터렛건 숨김" 같은 코드
  경로는 존재하지 않음. baseLayer=8은 "Wounds - pre apparel"(8)과 같은 낮은
  레벨이라 몸체 실루엣보다 아래에 깔려 오히려 더 안 보이게 됐을 가능성이 큼.
- 수정: baseLayer 8 -> 20(검증된 바닐라 Diabolus 사례값)으로 되돌림.
- 참고: 20으로도 여전히 안 보이면 남은 유력 후보는 (a) 렌더 트리가 캐싱된
  기존 세이브의 낡은 Sheridan 폰 인스턴스를 테스트 중이었을 가능성(README
  기존 경고 "새로 스폰한 것으로 테스트" 참고 - 이번엔 폰 자체를 새로 스폰해야
  comp/렌더노드가 확실히 반영됨), (b) drawData 오프셋 미설정으로 그래픽이
  몸체 그림 뒤 안 보이는 위치에 배치됐을 가능성. 다음 확인은 완전히 새로
  스폰한 Sheridan으로 재테스트 요청.

[2026-07-26] (4)
버그 수정 - KOR_Sheridan_M2HB 추가 후 주무기(152mm) 그래픽이 안 보임
- 원인: 직전 수정에서 M2HB의 PawnRenderNode_TurretGun baseLayer를 Diabolus
  예시 그대로 20으로 뒀는데, Core PawnRenderTreeDefs.xml 기준 ApparelBody
  루트 노드가 정확히 baseLayer=20이라 겹침. Diabolus/Apocriton은 별도
  주무기(Carried 노드)가 없는 메크라 이 충돌 자체가 없었던 것.
  Sheridan은 152mm 주무기(Carried, 전역 바닐라 노드라 baseLayer 미지정)가
  같이 그려져야 하는데, M2HB가 그 위/근처 레이어를 차지하면서 가려짐.
- 수정: 바닐라 전역 Carried 노드 레이어는 모든 폰에 영향을 주므로 손대지 않고,
  M2HB 쪽 baseLayer만 20 -> 8로 낮춰 body/apparel보다 먼저(뒤에) 그려지도록
  변경. 이제 주무기가 M2HB에 가려지지 않아야 함.
- 참고: 8이 최적값인지는 실제 인게임에서 확인 필요. 여전히 어긋나면 baseLayer를
  더 낮추거나(예: 3~5), 반대로 주무기가 M2HB를 완전히 가려버리면 살짝 올리는
  식으로 미세조정 예정.

[2026-07-26] (3)
버그 수정 - KOR_Sheridan_M2HB 그래픽 미표시 (renderNodeProperties 누락)
- 증상: texPath/텍스처 파일 자체는 정상인데 게임 내에서 M2HB 마운트 총 그래픽이
  전혀 안 보임.
- 원인: CompTurretGun의 gun은 pawn.equipment가 아니라 comp 내부 필드로만 존재해
  표준 PawnRenderer 경로를 타지 않음 - renderNodeProperties(PawnRenderNode_TurretGun
  / PawnRenderNodeWorker_TurretGun)를 명시적으로 넣어줘야 폰 위에 실제로 그려짐.
  바닐라 Biotech Diabolus/Apocriton의 Gun_ChargeBlasterTurret 마운트 방식 확인함
  (RimWorld/Data/Biotech/Defs/ThingDefs_Races/Races_Mechanoids_SuperHeavy.xml).
  Korossa 원본 Gun_coax_lunchbox는 애초에 이 블록이 없어서 렌더 노드 자체가
  없고, 그래서 blank 텍스처(Things/Blanks/Blank_south)를 써도 티가 안 나는
  것뿐 - "마운트 총은 안 보이게 설계됨"이 아니라 원작자가 렌더 노드를 안 넣은
  것이었음.
- 수정: Weapons_MechGuns.xml의 Mech_Sheridan comps 추가 Operation에서
  KOR_Sheridan_M2HB CompProperties_TurretGun에 renderNodeProperties
  (nodeClass=PawnRenderNode_TurretGun, workerClass=PawnRenderNodeWorker_TurretGun,
  parentTagDef=Body, baseLayer=20, pawnType=Any) 추가.
- 참고: drawData(회전별 오프셋)는 기본값만 넣어뒀음. 실제 인게임에서 Sheridan
  몸체 그림과 위치가 안 맞으면 dataNorth/South/East/West의 rotationOffset/
  offset 값을 시각적으로 맞춰 조정 필요.

[2026-07-26] (2)
버그 수정 - KOR_Sheridan_M2HB 탄약 공급 미등록 (발사직전 재장전 무한시도)
- 증상: 게임 로그에 "[211] already Reloaded."(주무기, 정상) 직후
  "Identified as Korossa Mech using custom ReloadSystem"(M2HB 터렛, 비정상)이
  반복 출력됨. 스택트레이스가 CompTurretGun.CompTick -> Verb_ShootCE.Available
  -> CompAmmoUser.TryStartReload 경로로, 매 버스트 워밍업 직전마다 M2HB가
  재장전을 시도했다가 조용히 실패하는 무한루프였음.
- 원인: TurretAmmoSupplyPatch.cs의 탄약 공급 시스템(스폰 시 탄창+예비탄 지급,
  CE Job 재장전 차단, 틱 기반 자동 재장전)이 전부
  TargetPawnDefNames={"Mech_ADlunchbox"} / gun.def.defName=="Gun_rocketpod_lunchbox"
  로 하드코딩돼 있어 Mech_Sheridan/KOR_Sheridan_M2HB는 이 시스템 대상이 아니었음.
  M2HB는 스폰 시 탄창도 안 채워지고 예비 .50BMG도 지급받지 못해 CurMagCount가
  항상 0 -> MechManualReloadGuardPatch의 폴백 안전망이 인벤토리에서 호환탄을
  찾다가 매번 실패해 재장전을 계속 거부(false)하는 상태로 고정됨.
- 수정: TurretAmmoSupplyUtility.TargetPawnDefNames에 "Mech_Sheridan" 추가,
  IsPawnMountedTurretGun의 단일 gun defName 하드코딩을 TargetGunDefNames 배열
  ("Gun_rocketpod_lunchbox", "KOR_Sheridan_M2HB")로 일반화.
  이제 스폰 시 M2HB 탄창(100발)이 가득 채워지고 예비 탄창(SpareMagazines=2 ->
  .50BMG 200발)이 인벤토리에 지급되며, 틱 기반 자동 재장전도 동일하게 적용됨.
- 참고(후속 확인 필요): SpareMagazines 상수(2)는 로켓포드(4발 탄창 x2=8발)와
  M2HB(100발 탄창 x2=200발)가 공유하는 값이라 M2HB 예비탄이 상당히 많음
  (부피/무게 부담 큼). 밸런스상 과하다 싶으면 M2HB 전용 예비탄 수량을
  분리하는 방향으로 조정 가능.
- ※ .cs 파일만 수정된 상태이므로 1.6/Assemblies의 DLL을 다시 빌드해서
  덮어써야 실제 게임에 반영됨(Rider 등에서 재빌드 필요).

[2026-07-26] (1)
버그 수정 - KOR_Sheridan_M2HB (차장용 M2HB 터렛) 마운트 터렛 warmupTime/cooldown
- Weapons_MechGuns.xml에 새로 추가된 KOR_Sheridan_M2HB(Mech_Sheridan에
  CompProperties_TurretGun으로 마운트되는 차장용 M2HB)가 "★ 마운트 터렛
  warmupTime 규칙"(주무기+터렛 동시 보유 시 터렛 warmupTime=0 필수)과
  "터렛은 defaultCooldownTime 필요"(없으면 쿨다운 0 → 버스트 후 무한 연사)
  두 가지를 모두 위반한 상태였음 - Gun_rocketpod_lunchbox/Gun_coax_lunchbox에서
  이미 겪었던 발사준비/취소 무한반복·무한연사 버그와 동일 원인.
- warmupTime 1.0 -> 0으로, defaultCooldownTime 없음 -> 1.5로 수정
  (기존에 넣어뒀던 RangedWeapon_Cooldown=1.5 값을 그대로 옮김. 터렛 방식에서는
  RangedWeapon_Cooldown이 적용되지 않으므로 defaultCooldownTime으로 대체 지정).

[2026-07-24]
장갑차량형 메크 근접(충각) 공격 강화 - Korossa_CE_MechMeleePatch.xml
- 대상: Mech_Sheridan / Mech_ADlunchbox / Mech_TOWlunchbox / Mech_LUNCHBOX
  (보병형 Mech_krsgoliath / Mech_haebyung / Mech_HIBIKI는 이번 개편 제외, 기존 값 유지)
- 기존 관례였던 "power x 0.344" RHA mm 관통 환산 대신, MPa(재질 압축/항복 강도)
  기준으로 재설계. 사람 피질골 파괴강도(~130MPa), 경량 장갑판 항복강도(~250~300MPa)
  대비 수 톤급 질량이 유압/서보 램으로 충돌할 때의 국소응력은 인체/경장갑에는
  사실상 확정 관통이나, 전차급 RHA·복합장갑까지 뚫는 수준은 아니라고 보고 설계함.
- 질량 가정: Sheridan은 M551 실차 중량 ≈15.2t. lunchbox 계열(ADlunchbox/
  TOWlunchbox/LUNCHBOX) 3종은 전부 M113 장갑차 실전투중량 ≈12.3t으로 통일
  (최초 커밋에서는 6t/3.5t 추정치로 나눠 잡았으나, 사용자 요청으로 lunchbox
  계열 전체를 동일 실차 기준값으로 재조정함).
  스케일링 기준점(최초 LUNCHBOX 추정 3.5t/power 26)은 유지한 채
  power = 26 * sqrt(질량/3.5t) 공식으로 재계산.
  (선형 적용 시 수치가 과도해 밸런스가 붕괴하므로 제곱근으로 완화).
- 수치 변경 (power / armorPenetrationBlunt / cooldownTime):
  Sheridan: 12/4.13/2.0 -> 54/48/3.0 (변경 없음)
  ADlunchbox·TOWlunchbox·LUNCHBOX(M113 12.3t 통일): 12/4.13/2.0 -> 49/45/2.8
- 툴 label을 "head" -> "충각(ram)"으로 변경(캡슐화된 개념 명확화). capacities(Demolish),
  linkedBodyPartsGroup(HeadAttackTool), chanceFactor(그룹별 0.02/0.2)는 기존 값 유지
  (ADlunchbox/TOWlunchbox=0.02, LUNCHBOX=0.2 — 근접 사용 빈도는 질량이 아닌
  전투 역할에 따른 값이라 그대로 둠).
- 참고: krsgoliath/haebyung 그룹은 기존 하나의 PatchOperationReplace에서
  LUNCHBOX와 함께 처리되던 것을 분리 — lunchbox 3종만 별도 Operation으로 buff 적용.

[2026-07-18]
120mm 로켓 / ADlunchbox
- Gun_rocketpod_lunchbox: CE 패치(Verb_ShootCE + AmmoUser, AmmoSet_120mmrocket).
- Bullet_120mmrocket_HE 재작성: HE 폭압(55뎀/r3.0) + CE 파편 + EMP(30뎀).
- AmmoSet_120mmrocket ammoTypes를 발사체(Bullet_120mmrocket_HE)로 교정
  (기존엔 탄약 def를 가리켜 CE 탄약↔발사체 매핑이 깨져 발사 불가였음).
- 텍스처 미싱 수정: Ammo_120mmrocket_HE graphicClass를 단일 png에 맞게
  Graphic_StackCount 유지하되 texPath/폴더 정합성 확인.
- C# 터렛 탄약 공급(TurretAmmoSupplyPatch.cs) 추가:
  스폰 시 탄창 장전 + 예비 탄약 지급 / 자동 재장전 / 발사 windup 1.5초.
- CE 재장전 차단: 터렛 총에 CE가 JobDriver_Reload를 걸어
  "Unable to find the weapon to be reloaded" 예외를 무한 발생시키던 것 차단.
- CompMechAmmo 확장(MechTurretAmmoLoadoutPatch.cs): 아군 메크가 터렛 탄약도
  로드아웃 수량만큼 자동 보급, '터렛 탄창 설정' 기즈모 추가.

버그 수정 - 마운트 터렛 warmupTime
- Gun_rocketpod_lunchbox warmupTime 0으로 (주무기 .50 발사준비/취소 반복 해결).
- Gun_coax_lunchbox warmupTime 0.5 -> 0으로 (Sheridan 주무기 152mm 동일 버그 해결).

밸런스
- 120mm 로켓: HE 25/r3.0, 소형 파편 45, EMP 30/r2.0, 탄체 중량 12kg.
  제작 1회당 4발로 제한해 대인 특화 화력과 보급 부담을 함께 유지.
- 코로사 시그니처 탄약을 CE 동급탄 기준으로 정상화:
  7.56x40 FMJ 16/6.5mm, AP 10/13mm, HP 20/3mm, Sabot 8/21mm.
  7.8x50 AP는 13/18mm이며 제작비는 7.62 NATO보다 약간 높게 조정.

번역
- 120mm 로켓 계열을 다른 탄약과 동일 방식으로 전환:
  base def는 영어, 한국어는 DefInjected로 주입
  (ThingDef/Ammo_KOR, AmmoSetDefs/AmmoSets_KOR, ThingCategoryDef/Categories_KOR).

Sheridan 포발사 미사일
- M551의 MGM-51 운용 방식을 반영해 미사일을 별도 ability에서 제거.
- AmmoSet_KOR_Sheridan152mm에 CE 152mm 직사 포탄 3종(HEAT/HE/소이)과
  MGM_51 시레일러 포발사 미사일을 함께 등록해 주포 탄종 선택으로 발사.
- 공용 152mm AmmoSet은 수정하지 않아 다른 152mm 화포에는 미사일이 노출되지 않음.
- 주포는 1발 장전/7초 재장전을 공유하므로 포탄과 미사일이 같은 약실을 사용함.

Sheridan 기동전 밸런스
- 실차의 정찰/공수 경전차 역할과 약 43~45mph 도로속도를 반영.
- MoveSpeed 3.0 -> 4.6, CaravanRidingSpeedFactor 1.35 추가.
- 152mm 주포 조준 4초 -> 2초, 발사 후 쿨다운 9초 -> 3초,
  단발 재장전 12초 -> 7초로 변경해 사격 후 이탈 운용이 가능하게 함.
- 포탄은 짧은 쿨다운 뒤 바로 이탈할 수 있지만, 127mm 미사일은 SACLOS이므로
  비행 중 조준선을 유지해야 하는 차이는 그대로 보존.

착용형 LAW / DRAGON 발사 오류
- 바닐라 Verb_LaunchProjectileStatic은 CE ProjectileCE를 Verse.Projectile로
  캐스팅하다 InvalidCastException을 발생시킴.
- 두 발사기의 verb를 CE 공식 apparel pack 방식인
  Verb_ShootCEOneUseStatic + VerbPropertiesCE로 교체.
- 기존 ApparelReloadable 충전 수, DR_rocket 재장전, LAW 일회용 파괴 동작은 유지.

7.8x50mm 탄약
- AP/HP에서 부모와 중복되던 stackLimit / cookOffSpeed 제거.

[2026-07-27] (4)
메크 총기 근접 툴(Weapons_MechMelee.xml) 재구성 + M2HB 근접 스탯 공란 수정
- 기존 단일 PatchOperationConditional(8종 총기 동일 stock/barrel/muzzle)을
  PatchOperationRemove + 그룹별 PatchOperationAdd 2개로 분리.
- 그룹A(버프): KOR_Sheridan_gun/Gun_LUNCHBOX/KOR_ADvulcan/Gun_haebyung -
  차체 충각(ram) 버프와 동일한 MPa 기준으로 stock/barrel/muzzle 상향
  (power 52/30/45, cooldownTime 2.55/3.02/2.55, armorPenetrationBlunt
  10.755/10.630/10.755).
- 그룹B(원본 유지): mechmg3_krs/mechsmg_krs/mechAR_krs/Gun_HIBIKI - 기존값 그대로.
- KOR_Sheridan_M2HB를 그룹A에 신규 편입: 종전엔 이 무기에 <tools>가 아예
  없어서 CE 스탯창의 "근접공격 보호구 관통"이 공란으로 표시되던 문제였음.
  CombatExtended.dll 디컴파일로 StatWorker_MeleeArmorPenetration.
  GetFinalDisplayValue가 GetThingDefTools()로 tools가 없으면 빈 문자열("")을
  반환하는 것을 확인해 원인 확정.
- 참고(미해결/설계상 이슈): 화면에 같이 보이던 "사격 정확도/근접공격 반격
  확률/치명타 확률/회피 확률" 0%는 위 tools 패치와 무관한 별개 원인.
  CE StatDefs(Stats_Pawns_Combat.xml)의 MeleeCritChance/MeleeParryChance
  등은 category=PawnCombat로 pawn 전용 스탯이며, capacityFactors로
  Manipulation(가중치1)/Sight(가중치0.7) 캐패시티를 곱해서 최종값을 낸다.
  메크 바디에 Manipulation/Sight로 태그된 파츠가 없으면(=캐패시티 0) 스킬이나
  무기값과 무관하게 결과가 통째로 0%가 됨 - ToolCE 필드로는 고칠 수 없고,
  메크 바디/헤디프 쪽에 Manipulation·Sight 캐패시티를 부여해야 하는 별도 작업.

[2026-07-27] (5) - 긴급 수정
Korossa_CE_MechMeleePatch.xml 유실 복구 (스탯창 크래시 원인)
- 로그: "Trying to get stat MeleeArmorPenetration from Mech_Sheridan which has
  no support for Combat Extended." + InfoCard InvalidCastException.
- 원인: 메크 본체(Mech_Sheridan 등)의 근접 'head' 툴을 CombatExtended.ToolCE로
  바꿔주던 패치 파일(Korossa_CE_MechMeleePatch.xml)이 폴더에서 통째로 사라져
  있었음(7/24 작업분 유실 추정). 그 결과 메크 폰이 바닐라 Tool(비-ToolCE)을
  그대로 갖게 되어 CE가 관통 스탯을 못 읽고 에러 → 스탯창 InfoCard가 그대로
  크래시.
- 세션 로컬 캐시에 남아있던 7/24 원본(power12/blunt4.13 균일값) 백업을 근거로,
  이후 대화에서 확정했던 버프값을 반영해 새로 작성/복구:
  Mech_Sheridan: power 54 / armorPenetrationBlunt 48 / cooldownTime 3.0 /
    chanceFactor 0.02, 라벨 "충각(ram)".
  Mech_ADlunchbox·TOWlunchbox·LUNCHBOX(M113 중량 통일): power 49 /
    armorPenetrationBlunt 45 / cooldownTime 2.8, chanceFactor는 ADlunchbox·
    TOWlunchbox 0.02 / LUNCHBOX 0.2.
  Mech_krsgoliath·haebyung(보병형, 버프 제외): power 12 / armorPenetrationBlunt
    4.13 / cooldownTime 2 / chanceFactor 0.2, 라벨 "head" 그대로.
- Mech_HIBIKI는 기존과 동일하게 제외(외부 베이스 LightMechanoid 툴 상속, CE가
  별도 처리).

[2026-07-27] (6)
Sheridan/lunchbox 계열 근접 명중률 보정 (Korossa_CE_MechMeleeAccuracy.xml 신설)
- 사용자 보고: "sheridan 근접 정확도가 너무 떨어짐".
- CE 근접 명중 판정(Verb_MeleeAttackCE.GetHitChance)은 바닐라 StatDef
  'MeleeHitChance'(defaultBaseValue 0)를 그대로 씀. 이 스탯은
  noSkillOffset(스킬트래커 없는 폰=+4)과 Manipulation/Sight 캐패시티
  오프셋(각 scale12/max1.5)을 더해 postProcessCurve로 0~1 확률 변환.
  메크는 skills가 없어 +4만 보장되고 캐패시티 상태에 따라 원시값이
  낮게 나오면 명중률이 확 떨어짐 (raw 4≈62%, raw 20≈90%).
  ※ ToolCE의 chanceFactor는 "AI가 이 공격을 선택할 확률"일 뿐 명중률과는
  무관 - 착각하기 쉬운 부분이라 명시.
- 조치: Mech_Sheridan/ADlunchbox/TOWlunchbox/LUNCHBOX statBases에
  MeleeHitChance 20을 직접 얹어 캐패시티/스킬 상태와 무관하게 근접 명중률
  바닥을 약 90% 선으로 보장.

[2026-07-28]
로열티 허가서 드랍 품목 교체 (RoyaltyAmmuniationPatch.xml 신설)
- KOR_ATweapons_Tier2: PF_rocket(5) -> Ammo_127mmMissile_HEAT(5, 127mm ATGM).
- KOR_ATweapons_Tier3: DR_rocket(2) -> MGM_51_Missile(2, 152mm ATGM/MGM-51X).
- permitPointCost/cooldownDays/favorCost 등 가격은 전혀 변경하지 않음.

BSD/Juggernaut 방어구 강철(1x) 기준 상향 (Korossa_CE_ArmorPatch.xml)
- 사용자 요청: "방어구들이 다 약해... bsd랑 저거넛 방어구를 강철 기준(1x)
  몸통 22mm 목 18mm 사지 20mm로 상향해".
- CE의 ArmorRating_Sharp/Blunt/Heat는 방어구 하나당 부위 구분 없는 flat
  값이라(StuffEffectMultiplierArmor x 재료별 StuffPower_Armor_* 배율),
  세 부위 값을 한 아이템에 그대로 못 넣음. 사용자에게 AskUserQuestion으로
  매핑 방식 확인 후 "부위 평균" 방식 선택받음.
  - Korossa_BSD_Vest: Torso+Neck+Shoulders 커버(사지 없음) ->
    (몸통22+목18)/2 = 20mm.
  - Korossa_Juggernaut: Torso+Neck+Shoulders+Arms 전부 커버 ->
    (몸통22+목18+사지20)/3 = 20mm.
  - 강철의 StuffPower_Armor_* 배율이 바닐라 기준 1.0(기준 재료)이므로
    StuffEffectMultiplierArmor 값을 목표 mm와 동일하게 설정하면 강철 제작 시
    그대로 목표 mm가 나옴. 기존엔 각각 12/14(플라스틸 기준 추정치)였던 것을
    20/20으로 상향.

BSD 셔츠/헬멧 고정 RHA·MPa 전환 + BSD_Vest/Juggernaut 추가 상향
- 사용자 요청: "코로사 BSD 셔츠를 고정 RHA랑 MPA로 설정해. RHA 4mm에 MPA 8
  적용해. 코로사 BSD 헬멧도 고정 RHA랑 MPA로 설정하고 RHA 34MM에 MPA 40
  적용해. BSD VEST랑 저거넛을 40%p 더 세게 해".
- Korossa_BSD_shirts(Korossa_CE_ClothingPatch.xml): 기존엔
  StuffEffectMultiplierArmor(재료 배율 방식, 5.5)였던 것을 0으로 꺼서 재료와
  무관하게 만들고, ArmorRating_Sharp 4 / ArmorRating_Blunt 8을 statBases에
  직접 고정값으로 추가. 이제 재료(강철/천 등)를 뭘로 만들든 4mm/8MPa 고정.
- Korossa_BSD_helmetA(Korossa_CE_HeadgearPatch.xml): base ThingDef에
  애초에 StuffEffectMultiplierArmor가 없어 이미 고정값 방식이었음 - 값만
  ArmorRating_Sharp 12->34, ArmorRating_Blunt 28->40으로 상향.
- Korossa_BSD_Vest/Korossa_Juggernaut(Korossa_CE_ArmorPatch.xml):
  직전에 강철 1x 기준으로 맞춘 20mm(StuffEffectMultiplierArmor)에서 40%p
  추가 상향 -> 20*1.4 = 28로 변경(둘 다 동일).

Blackdown/Blackout 탄약 상향 (RHA/MPa/데미지 + 2차 폭발효과)
- 사용자 요청: "blackdown이랑 blackout rha 7mm, mpa 10 상향하고 데미지 올려.
  blackout fmj,철갑탄 폭발효과 70%p 상향. blackdown은 6데미지 상향, blackout은
  16데미지 상향".
- 도중 발견: KJW_Blackdown의 AmmoUser.ammoSet이 CE 기본 내장
  AmmoSet_300AACBlackout(.300 AAC Blackout, 다른 CE 호환 무기/모드와 공유하는
  칼리버)을 그대로 참조하고 있었음. 이걸 직접 수정하면 Korossa 밖의 다른
  무기에도 영향을 주는 전역 변경이 되어버림. AskUserQuestion으로 확인 -
  사용자가 두 선택지 대신 "그러면 secondary로 flame이랑 emp를 줘"로 답변,
  즉 전용 AmmoSet 신설 + 404VL과 동일한 FMJ=Flame/AP=EMP 2차 폭발 부여로
  진행.
- Blackdown(R.300) 전용 AmmoSet_Blackdown_KOR 신설(Defs/AmmoDefs/Ammo_Blackdown.xml):
  CE 기본 .300 AAC Blackout FMJ/AP 수치(damage 16/10, armorPenetrationSharp
  5.5/11, armorPenetrationBlunt 36.46 공통)를 베이스로 요청 상향치(+6dmg,
  +7mm RHA, +10 MPa)를 더함.
  - Bullet_Blackdown_FMJ: damage 16->22, armorPenetrationSharp 5.5->12.5,
    armorPenetrationBlunt 36.46->46.46. 2차 폭발(Flame) damageAmountBase 15
    신설(404VL과 동일 패턴).
  - Bullet_Blackdown_AP: damage 10->16, armorPenetrationSharp 11->18,
    armorPenetrationBlunt 36.46->46.46. 2차 폭발(EMP) damageAmountBase 15
    신설.
  - KJW_Blackdown(Weapons_MERC.xml)의 ammoSet을 AmmoSet_300AACBlackout ->
    AmmoSet_Blackdown_KOR로 교체(전역 칼리버 영향 차단).
  - 신규 탄약 조달 수단으로 Recipes_Blackdown.xml 신설
    (MakeAmmo_Blackdown_FMJ/AP, AmmoBench 제작, Korossa_weapon_MERC 연구
    필요).
  - 한국어 번역 추가: DefInjected/ThingDef/Ammo_KOR.xml,
    DefInjected/CombatExtended.AmmoSetDefs/AmmoSets_KOR.xml,
    DefInjected/ThingCategoryDef/Categories_KOR.xml.
- Blackout(.404VL) 탄약 상향(Defs/AmmoDefs/Ammo_404VL.xml):
  +16dmg, +7mm RHA, +10 MPa, 2차 폭발효과(FMJ=Flame/AP=EMP) 70%p 상향
  (damageAmountBase 15 -> 15*1.7 = 25.5).
  - Bullet_404VL_FMJ: damage 35->51, armorPenetrationSharp 10->17,
    armorPenetrationBlunt 240->250(카탈로그 상한 근접치라 실질 +10은
    240->250로 반영), 2차 폭발 damageAmountBase 15->25.5.
  - Bullet_404VL_AP: damage 22->38, armorPenetrationSharp 24->31,
    armorPenetrationBlunt 240->250, 2차 폭발 damageAmountBase 15->25.5.

[2026-07-28] (2)
전면 밸런스 상향 1차 - "경쟁 CE패치 대비 방어구/화력 전면전"
- 사용자 방향: 경쟁 모드(Korossa Scorched Brass 타 CE패치)가 더 강하다고 판단,
  방어구 전체 상향 + 탄약 트랙 이원화(커스텀탄=데미지/RHA/MPa, CE기본탄=쿨다운/정확도).
- 방어구(Korossa_CE_ArmorPatch.xml): FlakVest/platecarrier도 전체 상향 방향에
  합류(기존엔 "하향" 주석이 남아있던 상태) - AskUserQuestion으로 확인 후 진행.
  - Korossa_Basic_FlakVest: StuffEffectMultiplierArmor 7(하향값) -> 14(강철 1x
    기준 14mm, 소프트아머 상한).
  - Korossa_platecarrier: StuffEffectMultiplierArmor 6(하향값) -> 20(강철 1x
    기준 20mm, 경질 플레이트 소총탄 저지 수준).
- 커스텀 탄약 상향(실탄도 기반 + 경쟁 대응, Claude 초안 - 추후 실측 대응 수치로 교체 가능):
  - 756x40Kor(TARX): FMJ 16/6.5/43 -> 20/9/49, AP 10/13/43 -> 13/17/49,
    HP 20/3/43 -> 24/4/49, Sabot 8/21/56 -> 10/27/64 (dmg/AP_sharp/AP_blunt).
  - 762x42mmSub(WRAITH): FMJ 14/4.5/18 -> 18/6/23, AP 9/9/18 -> 12/12/23,
    HP 17/2.5/18 -> 22/3/23.
  - 78x50mm(VOLK 계열): FMJ 21/8/70 -> 26/11/78, AP 13/18/70 -> 17/24/78,
    HP 26/4/70 -> 32/5/78.
  - 120mm 로켓(ADlunchbox)은 2026-07-18에 이미 별도 밸런스(제작 4발 제한 등)로
    확정된 상태라 이번 1차 상향에서는 제외 - 필요시 후속 검토.
- CE 기본탄 무기 상향(쿨다운↓/정확도↑, Claude 초안):
  - 대상: KJW_Ravenhawk/AMGE/AJM/DBS/OICW(Weapons_MERC.xml),
    KJW_AKU/Aliolio/Alioliowood(Weapons_AK.xml). Blackdown/TARX/WRAITH는
    커스텀탄 트랙이라 이번 항목에서 제외.
  - 공통 방향: RangedWeapon_Cooldown 약 -15%, ShotSpread 약 -13~15%,
    SightsEfficiency 약 +8~10%.
- 남은 작업(미착수): Weapons_MG/Pistol/Shotgun/Sniper/Special/Submachine/
  Chimera/Ody.xml 등 나머지 CE기본탄 무기군은 이번 1차에서 손 안 댐 -
  동일한 쿨다운↓/정확도↑ 방향으로 2차 패스 필요(제안안은 별도 문서로 전달).

[2026-07-28] (3)
방어구 보호 범위(bodyPartGroups) 점검 - 원본 모드(Korossa Scorched Brass,
workshop/294100/3429142659) 1.6/Defs/Apparel/*.xml과 CE패치 대상 전수 대조.
- 발견: Korossa_Exosuit/Exosuit_Smoke/Exosuit_Drifter 원본 bodyPartGroups가
  Legs/Arms/Neck뿐이라 Torso(몸통)가 아예 미보호 상태였음. CE패치에서
  ArmorRating_Sharp 20/Blunt 45로 올려놔도 몸통엔 적용이 안 되고 있었음.
- 나머지(FlakVest/platecarrier/BSD_Vest/Juggernaut/Basic_Alice/BSD_shirts/
  BSD_helmetA/KRS_vacpack 등)는 원본과 커버리지 일치하거나 의도된 변경
  (FlakVest Neck 제거 등)이라 문제없음을 확인.
- 사용자 지시: BSD 셔츠는 전신 보호구 컨셉이므로 손/발까지 전신 커버로
  확장, 저거넛도 동일하게 확장. Exosuit 계열은 위에서 발견한 몸통 누락과
  함께 전신 커버로 확장.
- 반영(bodyPartGroups PatchOperationAdd로 부위 추가, ArmorRating 등 스탯
  값 자체는 변경 없음 - CE는 부위별이 아니라 아이템당 단일 flat 값이라
  커버 부위가 늘어도 그대로 적용됨):
  - Korossa_BSD_shirts(Korossa_CE_ClothingPatch.xml): 원본 Torso/Neck/Legs
    -> Shoulders/Arms/Hands/Feet 추가, 전신 커버.
  - Korossa_Juggernaut(Korossa_CE_ArmorPatch.xml): 원본 Torso/Neck/
    Shoulders/Arms -> Hands/Legs/Feet 추가, 전신 커버.
  - Korossa_Exosuit/Exosuit_Smoke/Exosuit_Drifter(Korossa_CE_ArmorPatch.xml):
    원본 Legs/Arms/Neck -> Torso/Shoulders/Hands/Feet 추가, 전신 커버
    (몸통 미보호 버그 동시 해결).
  - FlakVest/platecarrier/BSD_Vest/Basic_Alice는 이번 전신화 대상에서 제외
    (조끼/체스트리그 컨셉 유지) - 필요하면 후속 확인.

[2026-07-28] (4)
경쟁 CE패치(Wolfein 종족모드 CE패치, workshop/294100/3485371294) 최상위
파워아머 세트와 BSD/저거넛 방어력 비교 - 사용자 지적: "BSD 풀세트 17000$인데
Wolfein 방어구 풀세트가 4200$인데 성능도 더 좋음, 이게 맞음?"
- 실측 비교 (Wolfein 1.6/Patches/Apparel/*.xml, 전부 재료 무관 고정값):
  - Wolfein_GuardPowerArmor(28/60) + Wolfein_PowerArmorHelmet(16/36)
    = 44 Sharp / 96 Blunt 합산.
  - Wolfein_ImperialGuardPowerArmor(44/68) + Mask(15.8/27.8)
    = 59.8 Sharp / 95.8 Blunt 합산.
  - 기존 BSD_Vest(강철 1x 기준 28, 재료 배율 방식이라 Blunt는 강철의
    Blunt 배율이 낮아 28보다 훨씬 낮게 실현) + BSD_helmetA(34/40)
    = 확인 결과 특히 Blunt에서 경쟁 세트(95.8~96)에 크게 못 미침.
    17000$ 대 4200$ 가격 차이를 감안하면 명백히 불합리.
- 조치: BSD_Vest를 재료 배율 방식에서 BSD_shirts/helmetA와 동일한 고정
  RHA/MPa 방식으로 전환하고 상향.
  - Korossa_BSD_Vest: StuffEffectMultiplierArmor 폐기 -> ArmorRating_Sharp
    50 / ArmorRating_Blunt 75 고정 부여. BSD_helmetA(34/40)와 합산하면
    84 Sharp / 115 Blunt - 경쟁 최상위 세트(59.8/95.8, 44/96) 대비 확실히 상회.
  - Korossa_Juggernaut: 마찬가지로 StuffEffectMultiplierArmor 0으로 폐기,
    ArmorRating_Sharp 70 / ArmorRating_Blunt 100 고정 부여(BSD Vest보다도
    높게 - 저거넛이 코로사 최고급 풀바디 아머이므로). 이미 전신 커버(위 항목)
    상태라 별도 헬멧 없이 단일 아이템으로 경쟁 세트를 상회.
  - 패치 순서 주의: 고정값 Add는 반드시 원본 ArmorRating_Sharp/Blunt/Heat
    Remove 3종 뒤에 배치해야 함(먼저 두면 뒤의 Remove가 방금 넣은 값까지
    같이 지워버림 - 실제로 최초 작성 시 이 순서 실수를 했다가 바로잡음).

[2026-07-28] (5)
플레이트 캐리어류/기본 헬멧류도 동일한 경쟁 벤치마크 방식으로 확장 적용
(사용자 요청: "그 플레이트 캐리어들이랑 헬멧들도 패치해야함").
- 참고 실측(Wolfein 하위/중위 티어, 전부 고정값):
  - Wolfein_FlakJacket: 7 Sharp / 12 Blunt (엔트리 방탄조끼).
  - Wolfein_HeavyFlakJacket: 14 Sharp / 20 Blunt (중급).
  - Wolfein_SafetyHelmet: 2.5 Sharp / 1 Blunt (기본 안전모).
- 조치 (Korossa_CE_ArmorPatch.xml / Korossa_CE_HeadgearPatch.xml, 전부
  StuffEffectMultiplierArmor 0으로 폐기 후 고정 ArmorRating_Sharp/Blunt 부여,
  기존 Remove 3종 뒤에 배치):
  - Korossa_Basic_FlakVest: 고정 12 Sharp / 18 Blunt (FlakJacket 7/12 대비 상회).
  - Korossa_platecarrier: 고정 20 Sharp / 30 Blunt (HeavyFlakJacket 14/20 대비 상회).
  - Korossa_BasicFlakHelmet / Korossa_Visorhelmet / Korossa_gasFlakHelmet:
    각각 고정 8 Sharp / 10 Blunt (SafetyHelmet 2.5/1 대비 큰 폭 상회, BSD_helmetA
    34/40보다는 낮은 하위 티어로 유지).
- 이제 코로사 방어구 라인업 전체(FlakVest/platecarrier/BSD_Vest/Juggernaut/
  기본 헬멧 3종/BSD_helmetA/BSD_shirts)가 재료 배율 방식이 아닌 고정 RHA/MPa
  방식으로 통일되었고, 각 티어별로 Wolfein CE패치의 대응 티어를 상회하도록
  재설계됨.

[2026-07-28] (6)
BSD_Vest 하향 - "이러면 코로사가 OP 되는거 아닌가?" 사용자 지적으로 재점검.
- 문제: BSD_Vest 50/75 + BSD_helmetA 34/40 = 84/115 조합이 우리 자체 최강
  소화기 AP탄(404VL AP Sharp 31mm)조차 못 뚫는 수준이었음. 게다가 BSD_Vest는
  Mass 11로 Wolfein 동급 파워아머(Mass 80)보다 훨씬 가벼워 방어력+기동성을
  동시에 가져가는 트레이드오프 없는 구성 - 명백한 오버튠으로 판단.
- 조치: Korossa_BSD_Vest 고정값 50/75 -> 35/52로 하향.
  - Wolfein_GuardPowerArmor(28 Sharp)은 여전히 상회.
  - 404VL AP(Sharp 31mm)가 근접/부분 관통 가능한 선으로 재조정(완전 무적
    상태 해소, 대신 최상위 AP탄으로만 겨우 위협 가능한 수준은 유지).
  - Blunt는 Sharp 대비 비율(35:52 ≈ 기존 50:75와 동일 비율)을 유지해 52로 조정.
  - BSD_helmetA(34/40)는 이번엔 건드리지 않음 - 필요하면 후속 조정.

[2026-07-28] (7)
Blackdown 2차폭발(Flame/EMP) 하향 - 사용자 지적: "flame이랑 emp는 대인/대메카
최강인데 blackdown이 너무 op 아닐까".
- 문제: KJW_Blackdown이 burstShotCount 15의 연사 화기인데 FMJ/AP 둘 다 맞을
  때마다(=버스트당 최대 15발) 2차폭발(Flame/EMP)이 개별로 터짐. 낱발 위력은
  15로 404VL(25.5)보다 낮아 보였지만, 버스트 15회 중첩되면 총 위력이 훨씬
  큼 - 화염은 사실상 이동형 화염방사기, EMP는 버스트 한 번에 메카노이드/실드
  스턴을 도배하는 수준이라 오버파워 판정.
- 조치 (사용자가 두 방향 다 채택):
  1) 낱발 위력 하향(Ammo_Blackdown.xml): 2차폭발 damageAmountBase 15->9,
     explosiveRadius 1.2->0.9 (FMJ Flame / AP EMP 둘 다 동일 적용).
  2) 버스트 수 하향(Weapons_MERC.xml KJW_Blackdown): burstShotCount 15->8.
  - 두 조치 병행으로 버스트당 총 2차폭발 피해량이 기존 대비 대폭 축소
    (15발×15dmg=225 -> 8발×9dmg=72, 약 68% 감소), 무기 정체성(연사형 R.300)은
    유지.

[2026-07-28] (8)
Blackdown 탄약 설정(lore) 추가 + 전체 미번역 텍스트 전수 점검/수정
("얘네 번역 안된거 다 번역해줘" 요청에 따른 전면 감사).
- Ammo_Blackdown_KORBase(Ammo_Blackdown.xml)의 placeholder 설명을 사용자가
  제시한 설정 텍스트로 교체: "약쟁이 여우들과 옆집 늑대들과는..." (MERC가
  왜 R.300을 개발했는지에 대한 배경 서술, 옆집 늑대=Wolfein 종족 저격).
- 모드 전체(1.6/1.6_Royalty/1.6_Ody/1.6_Anomaly, Defs+Patches+Languages)를
  훑어 label/description/jobString/reportString 중 한국어 대응이 없는
  항목을 찾아 수정:
  - Categories_KOR.xml: Ammo_756x40Kor/762x42mmSub/78x50mm의
    ThingCategoryDef.label 3종이 누락되어 있었음(정작 개별 탄약/AmmoSet은
    번역되어 있었는데 카테고리 트리 이름만 빠짐) - 다른 파일에 이미 쓰인
    표기(7.56x40mm VEK / 7.62x42mm 아음속 / 7.8x50mm 고압)로 통일 추가.
  - Ammo_RoyaltyLaunchers_CE.xml(Royalty): Bullet_KOR_Dragon_HEAT_CE
    "140mm DRAGON anti-tank guided missile" -> "140mm 드래곤 대전차
    유도미사일", Bullet_KOR_PF_HEAT_CE "110mm tandem-warhead anti-tank
    rocket" -> "110mm 텐덤탄두 대전차로켓"로 직접 번역(주변 코드 전체가
    이미 한국어 주석 위주라 DefInjected 대신 base def 직접 수정 방식 채택).
  - Abilities_SheridanSmoke.xml: 능력(KOR_Sheridan_SmokeDischarger) 자체는
    이미 한국어였으나, 내부에서 쓰는 발사체 ThingDef
    KOR_Sheridan_SmokeGrenade의 label만 "Sheridan smoke grenade"로 남아있어
    "셰리든 연막탄"으로 수정.
  - Job_FlareFlareFlare.xml: KOR_SACLOSSmoke JobDef의 reportString
    "Alerted" -> "경계 중"으로 수정(SACLOS 유도미사일 피격 시 연막 대응
    잡의 상태 표시 문구).
  - SACLOS_missiles.xml의 ThingCategoryDef/AmmoDef/AmmoSetDef들은 base def
    라벨이 영어였지만 DefInjected(Categories_KOR/Ammo_KOR/AmmoSets_KOR)에
    전부 이미 번역이 존재해 실제 게임 내 표시는 정상 - 조치 불필요로 확인.
  - 나머지 점검 대상 패치 파일(TurretPatch/UtilityPatch/MechRAM/
    MechMeleePatch/MechMeleeAccuracy/EXOMeleePatch/EXOWeapons/scenPatch/
    PawnkindDefPatch/MissileFlameLifetime/Dominion_EyeColorPatch/
    MechThinkTreeFix/RoyaltyAmmuniationPatch/CE_Launchers/CE_remove/
    CE_gun(Ody)/turretody/gravScenPatch/Mech_flamethrower 등)은 스탯/코드성
    xpath 조작만 있어 플레이어 노출 텍스트가 없거나 이미 한국어라 별도
    수정 불필요.

[2026-07-28] (9)
description 필드 및 근접 툴(stock/barrel/muzzle 등) 라벨 추가 번역
(사용자 지적: "한국어 Description은 한국어 번역으로 옮겨야지" - label만 보고
description을 놓쳤던 것을 재점검).
- grep으로 <description> 태그 전수 재점검 결과, 커스텀 탄약 4종의 abstract
  베이스 ThingDef 자체 description이 영어로 남아있었음(자식 ThingDef들의
  실제 표시 description은 DefInjected(Ammo_KOR.xml)로 이미 번역되어 있어
  인게임에 영어가 노출되진 않았지만, 죽은 텍스트를 방치하는 건 위생상
  좋지 않다고 판단해 베이스도 동일한 한국어 문구로 통일):
  - Ammo_756x40Kor.xml: Ammo_756x40KorBase description 번역.
  - Ammo_762x42mmSub.xml: Ammo_762x42mmSubBase description 번역.
  - Ammo_78x50mm.xml: Ammo_78x50mmBase description 번역.
  - Ammo_120mmrocket.xml: Ammo_120mmrocketBase description 번역.
- 근접 파츠 라벨(<label>stock/barrel/muzzle/grip/left fist/right fist/
  Teeth</label>)도 전부 영어로 남아있던 것을 확인해 통일 번역
  (개머리판/총열/총구/그립/왼주먹/오른주먹/이빨):
  Weapons_MERC.xml, Weapons_Shotgun.xml, Weapons_Special.xml,
  Weapons_AK.xml, Weapons_MG.xml, Weapons_Sniper.xml, Weapons_Submachine.xml,
  Weapons_Pistol.xml, Korossa_CE_EXOMeleePatch.xml, Mech_flamethrower.xml,
  Korossa_CE_UtilityPatch.xml(종족 기본 주먹/이빨 tools).
- 제외: Korossa_CE_MechMeleePatch.xml의 Mech_krsgoliath/haebyung "head"
  라벨은 (7)에서 이미 "충각(ram)"과 구분해 의도적으로 "head" 그대로 유지하기로
  확정한 값이라 이번에도 손대지 않음.
- 확인 후 유지(번역 불필요, 브랜드/모델명 성격): Ammo_Blackdown.xml의
  "Blackdown R.300" 라벨과 SACLOS_missiles.xml의 "MGM-51X" 라벨은 전부
  자식 defName에서 DefInjected로 이미 한국어 오버라이드되는 abstract
  베이스/모델명 표기라 실제 노출되지 않음 - TARX/WRAITH/VOLK 등 기존에도
  영문 모델명을 그대로 쓰는 모드 컨벤션과 일치하므로 유지.

[2026-07-28] (10) - 긴급 수정
(8)/(9)의 방법론 오류 시정: base def에 한국어 하드코딩 금지, DefInjected로만 번역
- 사용자 지적: "이걸 영어로 냅두고 한국어 definjection을 만들어야지... 영미권은
  모드 쓰지 마라는 소리냐?" - 전적으로 옳은 지적.
- 문제: (8)에서 Ammo_RoyaltyLaunchers_CE.xml/Abilities_SheridanSmoke.xml/
  Job_FlareFlareFlare.xml의 label/reportString을, (9)에서 근접 툴
  stock/barrel/muzzle/grip/left fist/right fist/Teeth 라벨 11개 파일을
  base(기본 언어, 영어) def 파일에 직접 한국어로 덮어써버렸음. base def는
  RimWorld에서 "언어 무관 기본값"이 아니라 사실상 영어 로케일 그 자체이므로,
  이렇게 하면 영어(및 다른 모든 비-한국어) 사용자에게도 한국어 텍스트가
  그대로 노출되어 모드가 사실상 한국어 전용이 되어버림 - 명백한 실수.
- 조치 1: Ammo_RoyaltyLaunchers_CE.xml/Abilities_SheridanSmoke.xml/
  Job_FlareFlareFlare.xml의 label/reportString을 전부 원래 영어 원문으로
  되돌림. 대신 신규 DefInjected 파일 3종을 추가해 정상적인 방식으로 번역:
  - DefInjected/ThingDef/RoyaltyLaunchers_KOR.xml (신설):
    Bullet_KOR_Dragon_HEAT_CE.label / Bullet_KOR_PF_HEAT_CE.label.
  - DefInjected/ThingDef/SheridanSmoke_KOR.xml (신설):
    KOR_Sheridan_SmokeGrenade.label.
  - DefInjected/JobDef/Jobs_KOR.xml (신설, JobDef 폴더 최초 생성):
    KOR_SACLOSSmoke.reportString.
- 조치 2: (9)에서 건드린 근접 툴 라벨(stock/barrel/muzzle/grip/left fist/
  right fist/Teeth) 11개 파일은 전부 원문 영어로 되돌림. 이 라벨들은
  ThingDef/AlienRace 정의 안 tools 리스트 항목이라 DefInjected로 넣으려면
  <defName.tools.N.label> 형태의 인덱스 기반 키가 필요하고, 무기마다 tools
  리스트 구성이 달라 인덱스를 하나하나 정확히 맞춰야 함 - 확신 없이 또
  손대다 잘못된 인덱스로 엉뚱한 파츠에 라벨이 붙거나 조용히 무시되는 위험이
  있어, 이번엔 손대지 않고 원상복구만 함. 필요하면 파일별로 tools 인덱스를
  확인해가며 후속 작업으로 진행 가능(우선순위 낮음 - 전투 로그에나 노출되는
  부수적 텍스트).
- (8)에서 건드린 커스텀 탄약 4종의 abstract 베이스 description(756x40Kor/
  762x42mmSub/78x50mm/120mmrocket)도 동일한 이유로 영어 원문으로 되돌림
  (다만 이건 애초에 모든 구체 자식이 DefInjected로 자기 description을
  덮어써서 실제로 노출되지도 않는 죽은 텍스트였으므로 피해는 없었음).
- 앞으로 원칙: 이 모드에 새로 추가되는 텍스트(label/description/
  reportString 등)는 base def에 영어로 유지하고, 한국어는 반드시
  1.6/Languages/Korean (한국어)/DefInjected/<DefType>/*.xml에 별도로
  추가한다. base def에 직접 한국어를 넣는 건(기존에 이미 존재하던
  KOR_Sheridan_M2HB.xml/Korossa_CIWS_CE.xml/Abilities_SheridanSmoke.xml의
  능력 본문 등 구식 관행) 새로 반복하지 않는다.

[2026-07-28] (11)
탄약(Ammo) 잔여 미번역 항목 DefInjected로 보강 (사용자 요청: "ammo도 번역해").
- base def는 전부 영어 그대로 유지, DefInjected/ThingDef/Ammo_KOR.xml에만
  아래 항목 추가(하드코딩 아님, (10)에서 확정한 원칙 준수):
  - 4개 탄약군 abstract 베이스 description 추가: Ammo_756x40KorBase /
    Ammo_762x42mmSubBase / Ammo_78x50mmBase / Ammo_404VLBase /
    Ammo_120mmrocketBase.description. 원래 각 concrete 자식이 이미
    description을 개별 오버라이드하고 있어 게임 내 표시엔 변화 없지만,
    abstract 베이스 자체도 번역 완전성을 위해 채워둠.
  - 지금까지 라벨만 없던 Bullet_* 투사체 라벨 전부 추가:
    Bullet_756x40Kor_FMJ/AP/HP/Sabot, Bullet_762x42mmSub_FMJ/AP/HP,
    Bullet_78x50mm_FMJ/AP/HP, Bullet_404VL_FMJ/AP. 인게임에서 투사체
    자체가 UI에 노출되는 경우(전투 로그 상세, 디버그 등)를 위해 각 탄약군
    라벨 패턴에 맞춰 "~ 탄두"로 통일.

[2026-07-28] (12)
DefInjected가 base def의 최신 lore 텍스트를 가리고 있던 문제 수정
(사용자 지적: "Ammo 중에 desc가 한국어로 되어있는것들이 있을텐데 그게 최신
desc니까 그걸 기준으로 번역해줘").
- 원인: MGM_51_Missile(SACLOS_missiles.xml)과 Ammo_Blackdown_FMJ/AP
  (Ammo_Blackdown.xml, Ammo_Blackdown_KORBase 통해 상속)는 base def에
  사용자가 직접 써넣은 최신 lore성 한국어 description이 있는데, 정작
  DefInjected/ThingDef/Ammo_KOR.xml에 그보다 먼저 등록된 짧고 밋밋한
  구식 description이 남아있어서, DefInjected 우선순위 때문에 실제
  인게임에는 lore 대신 그 구식 문구가 표시되고 있었음.
- 조치: Ammo_KOR.xml의 두 항목을 base def의 최신 lore 텍스트 기준으로
  교체.
  - MGM_51_Missile.description: base 그대로 반영.
  - Ammo_Blackdown_FMJ/AP.description: base의 lore 문단 + 탄약별 2차효과
    한 줄(소이/EMP)을 이어붙여 lore와 기능 설명을 모두 보존.
- 참고: base def 쪽의 이 두 lore 텍스트 자체는 (10) 원칙(base는 영어,
  한국어는 DefInjected)의 예외로 남아있음 - 사용자가 직접 이 세션에서
  요청해 넣은 콘텐츠라 되돌리지 않음. 새로 추가되는 lore/flavor 텍스트는
  이번처럼 base에 먼저 쓰더라도, 반드시 DefInjected 쪽도 같은 내용으로
  동기화해야 실제로 노출된다는 점을 앞으로 유의.
