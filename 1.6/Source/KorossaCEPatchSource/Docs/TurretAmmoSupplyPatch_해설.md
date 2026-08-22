# TurretAmmoSupplyPatch.cs 완전 해부

이 문서는 `TurretAmmoSupplyPatch.cs`를 처음부터 끝까지 한 덩어리씩 뜯어보며
RimWorld C# 모딩의 기본기를 익히기 위한 교재입니다.
코드를 외우는 게 목적이 아니라 **"왜 이렇게 쓰는가"** 를 이해하는 게 목적이에요.

---

## 0. 이 파일이 하는 일 (큰 그림)

ADlunchbox의 로켓포드는 폰(메크) 몸에 "터렛"으로 달려 있습니다.
CE(Combat Extended)는 폰이 **손에 든 주무기**한테만 탄약을 챙겨주기 때문에,
몸에 달린 터렛 총은 탄약을 못 받아 발사를 못 합니다.

그래서 이 파일이 CE 대신 세 가지를 직접 합니다:

1. 메크가 스폰될 때 → 로켓포드에 탄창을 채우고 예비 탄약을 가방에 넣어줌
2. CE가 터렛 총을 재장전하려다 에러 내는 걸 → 막음
3. 탄창이 비면 → 10초 뒤 가방의 탄약을 꺼내 자동 재장전

파일 구조는 크게 두 부분입니다.
- **`TurretAmmoSupplyUtility`**: 실제 일을 하는 도구 상자 (static 메서드 모음)
- **`Patch_...` 클래스들**: Harmony가 게임에 끼어드는 "진입점"

---

## 1. using 선언 (1~7줄)

```csharp
using System;
using System.Collections;
using System.Collections.Generic;   // List<>, Dictionary<> 쓰려고
using System.Reflection;            // PropertyInfo, FieldInfo (리플렉션)
using HarmonyLib;                   // Harmony, AccessTools
using RimWorld;                     // CompTurretGun 등 RimWorld 클래스
using Verse;                        // Pawn, Thing, ThingDef 등 엔진 핵심
```

`using`은 "이 네임스페이스의 클래스들을 짧은 이름으로 쓰겠다"는 선언.
RimWorld 모딩에서 거의 항상 등장하는 게 **`Verse`** 와 **`RimWorld`** 예요.
- `Verse` = 게임 엔진 바닥 (Thing, Pawn, Map, Def…)
- `RimWorld` = 그 위에 얹은 게임 규칙 (CompTurretGun, 작업, 무기…)

---

## 2. namespace (9줄)

```csharp
namespace KorossaCEPatch
```

내 코드들을 담는 "성(姓)". 다른 모드의 클래스와 이름이 겹쳐도
`KorossaCEPatch.무엇`으로 구분되니 충돌을 피합니다. 이 모드 C# 파일은 전부 이 성을 씁니다.

---

## 3. Utility 클래스의 머리 (28~49줄)

```csharp
public static class TurretAmmoSupplyUtility
{
    private static readonly string[] TargetPawnDefNames = { "Mech_ADlunchbox" };
    public const int SpareMagazines = 3;
```

- `static class` = 인스턴스를 안 만들고 `TurretAmmoSupplyUtility.메서드()`로 바로 쓰는 도구 상자.
- `TargetPawnDefNames` = **이 패치를 적용할 폰 목록**. 나중에 TOWlunchbox도 넣고 싶으면
  여기 문자열 하나만 추가하면 끝. (코드 로직은 안 건드려도 됨 = 잘 설계된 신호)
- `SpareMagazines = 3` = 예비 탄창 수. `const`는 "절대 안 변하는 값"이라는 뜻.

### 리플렉션용 캐시 필드 (39~49줄)

```csharp
private static Type _ammoUserType;
private static PropertyInfo _propCurMagCount;
private static FieldInfo _fldAmmoSet;
...
```

여기가 초보에겐 낯선 부분인데, **핵심 개념 하나만 잡으면 됩니다.**

우리는 CE라는 **남의 모드**의 클래스(`CompAmmoUser`)를 만져야 합니다.
그런데 우리 프로젝트는 CE를 참조로 넣지 않았어요(일부러). 그래서 `compAmmoUser.CurMagCount`
처럼 점 찍고 바로 못 씁니다. 대신 **리플렉션**으로 "이름표를 보고 더듬어 찾는" 방식을 씁니다.

- `Type` = 클래스 자체에 대한 정보 ("CompAmmoUser라는 설계도")
- `PropertyInfo` = 그 클래스의 속성 하나에 대한 정보 ("CurMagCount라는 서랍")
- `FieldInfo` = 필드(변수) 하나에 대한 정보

이 `_propCurMagCount` 같은 변수들은 매번 찾으면 느리니까 **한 번 찾아서 저장(캐시)** 해두는 그릇이에요.

> 정리: 내 클래스를 다룰 땐 리플렉션이 전혀 필요 없습니다.
> `pawn.Position`처럼 그냥 쓰면 돼요. 리플렉션은 "참조 안 한 남의 코드"를 만질 때만 쓰는
> 우회로입니다. 이 파일이 복잡해 보이는 90%가 이것 때문이에요.

---

## 4. ResolveCE() — 이름표 찾기 (51~92줄)

```csharp
private static bool ResolveCE()
{
    if (_resolved) return !_resolveFailed;   // 이미 한 번 찾았으면 재사용
    _resolved = true;
    try
    {
        _ammoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
        if (_ammoUserType == null) { _resolveFailed = true; return false; }

        _propCurMagCount = AccessTools.Property(_ammoUserType, "CurMagCount");
        ...
```

`AccessTools`는 Harmony가 주는 **리플렉션 도우미**입니다.
- `AccessTools.TypeByName("네임스페이스.클래스")` → 그 클래스의 `Type`을 찾아줌.
  CE가 설치돼 있으면 찾아지고, 없으면 `null`.
- `AccessTools.Property(타입, "속성이름")` → 그 속성의 `PropertyInfo`.

**이 함수의 설계 의도 2가지:**

1. **한 번만 실행** — `_resolved` 플래그로 게이트. 첫 호출 때 다 찾아 캐시하고,
   두 번째부터는 바로 결과 반환. (리플렉션은 비싸니까)
2. **CE 없으면 안전하게 실패** — `null` 나오면 `_resolveFailed = true`로 기록.
   try/catch로 감싸서 뭐가 터져도 게임은 안 죽음.

> 초보 팁: `try { ... } catch { }` 는 "이 안에서 에러 나도 무시하고 넘어가라".
> 모딩에선 남의 코드가 언제 바뀔지 몰라서, **죽으면 안 되는 곳**을 이렇게 감쌉니다.
> 단, 남용하면 버그를 숨기니 "실패해도 괜찮은 곳"에만 쓰세요.

---

## 5. IsTargetPawn() — 대상인지 확인 (94~107줄)

```csharp
public static bool IsTargetPawn(Pawn pawn)
{
    if (pawn == null || pawn.def == null) return false;
    string defName = pawn.def.defName;
    for (int i = 0; i < TargetPawnDefNames.Length; i++)
        if (TargetPawnDefNames[i] == defName) return true;
    return false;
}
```

아주 평범한 C#. **여기엔 리플렉션이 없죠?** `Pawn`은 RimWorld(우리가 참조한) 클래스라
`pawn.def.defName`을 그냥 점 찍고 씁니다. 내 목록(`TargetPawnDefNames`)에 이 폰의
`defName`이 있으면 `true`.

- `pawn == null` 체크를 먼저 하는 이유: null인데 `.def`에 접근하면 게임이 죽어요(NullReference).
  모딩 코드는 이런 방어 체크가 습관이 돼야 합니다.

---

## 6. GetTurretGun() — 터렛에서 총 꺼내기 (109~123줄)

```csharp
public static Thing GetTurretGun(CompTurretGun comp)
{
    if (comp == null) return null;
    try
    {
        FieldInfo gunField = AccessTools.Field(comp.GetType(), "gun");
        return gunField?.GetValue(comp) as Thing;
    }
    catch { return null; }
}
```

`CompTurretGun`(터렛 컴포넌트) 안에는 `gun`이라는 필드가 있어서 실제 총 Thing을 담고 있어요.
그런데 `gun`은 `public`이긴 하지만 여기선 일관성을 위해 리플렉션으로 꺼냅니다.

- `AccessTools.Field(타입, "gun")` → `gun` 필드의 `FieldInfo`.
- `gunField?.GetValue(comp)` → "comp라는 인스턴스의 gun 값을 꺼내라".
  - `?.` = "앞이 null이면 시도조차 말고 null 반환" (null 안전 연산자)
- `as Thing` = "결과를 Thing 타입으로 취급. 아니면 null." (안전한 형변환)

---

## 7. IsPawnMountedTurretGun() — 판정의 핵심 (125~151줄)

```csharp
public static bool IsPawnMountedTurretGun(ThingComp ammoUser)
{
    try
    {
        ThingWithComps gun = ammoUser?.parent;
        if (gun == null || gun.Spawned || gun.ParentHolder != null)
            return false;

        FieldInfo turretField = AccessTools.Field(ammoUser.GetType(), "turret");
        if (turretField != null && turretField.GetValue(ammoUser) != null)
            return false; // CE 건물 터렛 - CE가 알아서 처리

        return true;
    }
    catch { return false; }
}
```

이게 **CE 재장전 에러를 막는 판정 로직**이에요. "이 총이 폰 몸에 달린 터렛 총인가?"

- `ammoUser.parent` = 이 탄약 컴포넌트가 붙어 있는 총(Thing).
- 폰 마운트 터렛 총의 특징:
  - `gun.Spawned == false` → 맵에 독립적으로 놓인 게 아니라 폰 안에 숨어 있음
  - `gun.ParentHolder == null` → 인벤토리/장비창 같은 "그릇"에도 안 들어 있음
  - `turret` 필드가 null → CE 건물 터렛(포탑)도 아님
- 이 세 조건을 다 통과하면 "허공에 뜬 터렛 총" = CE가 재장전 못 시키는 그놈. `true`.

> 이 판정이 왜 필요했냐면, CE는 이 총을 "폰이 든 총"으로 착각해서 재장전 작업을 만드는데,
> 정작 재장전 작업은 장비창/인벤토리에서 총을 찾으려다 못 찾아서 매 틱 에러를 뱉었어요.
> 그래서 "이런 총이면 CE 재장전을 아예 막자"는 게 뒤(9번)에 나옵니다.

---

## 8. GetAmmoUser() — 총에서 CE 탄약 컴포넌트 찾기 (153~170줄)

```csharp
public static ThingComp GetAmmoUser(Thing gun)
{
    if (!ResolveCE()) return null;
    ThingWithComps twc = gun as ThingWithComps;
    if (twc == null || twc.AllComps == null) return null;

    for (int i = 0; i < twc.AllComps.Count; i++)
    {
        ThingComp comp = twc.AllComps[i];
        if (comp != null && _ammoUserType.IsInstanceOfType(comp))
            return comp;
    }
    return null;
}
```

총(Thing)에 붙은 컴포넌트들(`AllComps`)을 훑어서 CE의 `CompAmmoUser`를 찾습니다.

- 보통은 `gun.GetComp<CompAmmoUser>()` 한 줄이면 되는데, 우리는 CE를 참조 안 했으니
  `CompAmmoUser`라는 타입 이름을 코드에 못 씁니다.
- 그래서 `_ammoUserType.IsInstanceOfType(comp)` = "이 comp가 CompAmmoUser 타입이냐?"를
  리플렉션으로 물어봅니다. 맞으면 그 comp 반환.

---

## 9. Get/Set 도우미들 (172~245줄)

```csharp
public static int GetCurMagCount(ThingComp ammoUser)
{
    try { return (int)_propCurMagCount.GetValue(ammoUser, null); }
    catch { return -1; }
}

public static void SetCurMagCount(ThingComp ammoUser, int value)
{
    try { _propCurMagCount.SetValue(ammoUser, value, null); }
    catch { }
}
```

리플렉션으로 속성을 **읽고(Get) 쓰는(Set)** 짝꿍 메서드들. 패턴이 다 똑같아요:

- `_propCurMagCount.GetValue(대상, null)` → 대상의 CurMagCount 값을 꺼냄.
  결과는 `object`라서 `(int)`로 캐스팅.
- `_propCurMagCount.SetValue(대상, 값, null)` → 대상의 CurMagCount에 값을 넣음.

> `GetValue`의 두 번째 인자 `null`은 "인덱서 아님"이라는 뜻. 그냥 속성이면 항상 null.

만약 CE를 참조했다면 이 8개 메서드가 전부 사라지고 `ammoUser.CurMagCount = value;`
한 줄이 됐을 거예요. **이 파일이 긴 진짜 이유가 이겁니다.**

### GetAmmoDef() (202~233줄)

이 총이 쏠 탄약 종류(ThingDef)를 알아내는 함수. 우선순위대로 시도해요:
1. 지금 장전된 탄약(`CurrentAmmo`)이 있으면 그거
2. 없으면 선택된 탄약(`SelectedAmmo`)
3. 그것도 없으면 → ammoSet(탄약 세트)의 **첫 번째** 탄약을 꺼냄
   (`Props → ammoSet → ammoTypes[0] → ammo` 순서로 리플렉션 탐색)

스폰 직후엔 아직 아무 탄약도 안 정해졌으니 3번 경로로 기본 탄약을 잡습니다.

---

## 10. 인벤토리 조작 (247~305줄)

여기는 리플렉션이 거의 없어서 읽기 편할 거예요. `pawn.inventory.innerContainer`가
폰의 가방(아이템 담는 그릇)입니다.

```csharp
public static int CountAmmoInInventory(Pawn pawn, ThingDef ammoDef)
{
    if (pawn?.inventory?.innerContainer == null || ammoDef == null) return 0;
    return pawn.inventory.innerContainer.TotalStackCountOfDef(ammoDef);
}
```
가방에 든 특정 탄약의 총 개수. `TotalStackCountOfDef`는 엔진이 주는 편리한 메서드.

```csharp
public static void AddAmmoToInventory(Pawn pawn, ThingDef ammoDef, int count)
{
    ...
    while (count > 0)
    {
        int stack = Math.Min(count, ammoDef.stackLimit);   // 스택 한도까지만
        Thing ammo = ThingMaker.MakeThing(ammoDef);        // 실제 아이템 생성
        ammo.stackCount = stack;                           // 개수 지정
        pawn.inventory.innerContainer.TryAdd(ammo, true);  // 가방에 넣기
        count -= stack;
    }
}
```
탄약을 **실제 아이템으로 만들어서**(`ThingMaker.MakeThing`) 가방에 넣습니다.
스택 한도(`stackLimit`, 예: 50)를 넘으면 여러 뭉치로 쪼개 넣는 `while` 루프.

```csharp
public static int ConsumeAmmoFromInventory(Pawn pawn, ThingDef ammoDef, int count)
{
    ...
    Thing taken = pawn.inventory.innerContainer.Take(stack, take);  // 꺼내고
    taken.Destroy(DestroyMode.Vanish);                             // 없앰
    ...
    return consumed;  // 실제로 소모한 개수
}
```
재장전할 때 가방에서 탄약을 꺼내(`Take`) 소멸(`Destroy`)시킵니다.
"장전됐다"는 건 결국 "가방의 탄약이 총 안으로 사라진다"는 처리예요.
실제 소모량을 반환해서, 가방에 4발밖에 없으면 4발만 장전되게 합니다.

---

## 11. 패치 ①: 스폰 시 탄약 지급 (308~355줄) ★가장 중요

```csharp
[HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
public static class Patch_Pawn_SpawnSetup_TurretAmmoSupply
{
    static void Postfix(Pawn __instance, bool respawningAfterLoad)
    {
        ...
    }
}
```

**Harmony 패치의 표준 형태입니다. 이 틀만 외우면 나머지 패치가 다 읽혀요.**

- `[HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]`
  = "`Pawn` 클래스의 `SpawnSetup` 메서드를 가로챈다"는 표시(어트리뷰트).
  `nameof(...)`는 그냥 문자열 `"SpawnSetup"`인데, 오타 나면 컴파일러가 잡아줘서 더 안전.
- 메서드 이름을 **`Postfix`** 로 지으면 Harmony가 "원본 실행 **후**에 이걸 실행"해요.
  (`Prefix`로 지으면 "원본 **전**")
- 매개변수 규칙:
  - `Pawn __instance` = 언더스코어 2개. "원본을 호출한 그 Pawn 객체". Harmony 약속어.
  - `bool respawningAfterLoad` = 원본 메서드의 매개변수와 **이름을 똑같이** 맞추면
    그 값이 그대로 넘어옵니다.

### 본문 흐름

```csharp
if (respawningAfterLoad || !TurretAmmoSupplyUtility.IsTargetPawn(__instance))
    return;
```
- 세이브 불러오는 중(`respawningAfterLoad`)이면 이미 탄약이 저장돼 있으니 스킵.
- 대상 메크(ADlunchbox)가 아니어도 스킵.
- **모든 폰의 스폰마다 이 함수가 불립니다.** 그러니 초반에 빠르게 걸러내는 게 중요해요.

```csharp
foreach 컴포넌트 in __instance.AllComps:
    CompTurretGun 이 아니면 skip
    총 = GetTurretGun(터렛컴프)
    ammoUser = GetAmmoUser(총)
    ammoDef = GetAmmoDef(ammoUser)
    magSize = GetMagSize(ammoUser)

    SetCurMagCount(ammoUser, magSize)          // 탄창 가득
    if 가방에 탄약 0발:
        AddAmmoToInventory(..., magSize * 3)   // 예비 3탄창
```

폰의 모든 컴포넌트를 훑어 터렛을 찾고 → 그 안의 총 → 탄약 컴포넌트 → 탄창을 채우고
예비 탄약을 가방에 넣습니다. 앞에서 만든 도구들을 조립하는 단계예요.

> **이 패치 하나가 "적/아군 ADlunchbox가 로켓을 들고 나오게" 만드는 핵심입니다.**

---

## 12. 패치 ②③: CE 재장전 차단 (357~404줄)

```csharp
[HarmonyPatch]
public static class Patch_CompAmmoUser_TryStartReload_TurretGuard
{
    static bool Prepare()      => TargetMethod() != null;
    static MethodBase TargetMethod()
    {
        Type t = TurretAmmoSupplyUtility.AmmoUserType;
        return t == null ? null : AccessTools.Method(t, "TryStartReload");
    }
    static bool Prefix(ThingComp __instance)
        => !TurretAmmoSupplyUtility.IsPawnMountedTurretGun(__instance);
}
```

여기서 **새로운 Harmony 문법 2개**가 나옵니다.

1. **`[HarmonyPatch]` 를 비워두고 `TargetMethod()`로 대상 지정**
   - 위(11번)처럼 `typeof(CompAmmoUser)`라고 못 씁니다. CE를 참조 안 했으니까요.
   - 대신 `TargetMethod()`라는 이름의 메서드에서 "가로챌 메서드"를 리플렉션으로 찾아 반환하면,
     Harmony가 그걸 대상으로 삼습니다.
   - `AccessTools.Method(타입, "TryStartReload")` = 그 메서드의 정보.

2. **`Prepare()` = 이 패치를 적용할지 말지 결정**
   - `false`를 반환하면 Harmony가 이 패치를 **아예 건너뜁니다.**
   - CE가 없어서 `TargetMethod()`가 null이면 → `Prepare()`도 false → 패치 스킵.
   - 덕분에 CE 없이 이 DLL만 켜도 에러가 안 나요.

3. **`Prefix`가 `bool`을 반환**
   - `Prefix`가 `false`를 반환하면 **원본 메서드를 실행하지 않습니다.** (완전 차단)
   - `true`면 원본을 정상 실행.
   - 여기선 `!IsPawnMountedTurretGun(...)` → 터렛 총이면 `false`(차단), 일반 총이면 `true`(통과).

즉 "이 총이 문제의 터렛 총이면 CE 재장전을 못 하게 막고, 아니면 냅둬라".
③번(`DoOutOfAmmoAction`)도 똑같은 패턴으로, 탄 떨어졌을 때 CE가 엉뚱한 짓 하는 걸 막습니다.

> **Prefix + return false = "원본 취소"** 는 모딩에서 자주 쓰는 강력한 무기예요.
> 게임의 특정 행동을 통째로 막고 싶을 때 이 형태를 씁니다.

---

## 13. 패치 ④: 자동 재장전 (406~502줄)

```csharp
[HarmonyPatch]
public static class Patch_CompTurretGun_AutoReload
{
    private const int CheckIntervalTicks = 30;
    private const int RetryIntervalTicks = 250;
    private static readonly Dictionary<Thing, int> ReloadFinishTick = new();
```

- `Dictionary<Thing, int>` = "총 → 재장전이 끝나는 시각(틱)" 대응표.
  여러 ADlunchbox가 동시에 재장전 중일 수 있으니 총마다 따로 타이머를 기억합니다.
- RimWorld는 1초 = **60틱**. `CheckIntervalTicks = 30`은 0.5초마다 검사하겠다는 뜻.

### TargetMethod (424~435줄) — 버전 대응

```csharp
MethodBase m = AccessTools.Method(typeof(CompTurretGun), "CompTick");
if (m != null && !m.IsAbstract && m.DeclaringType == typeof(CompTurretGun))
    return m;
m = AccessTools.Method(typeof(CompTurretGun), "CompTickInterval");
if (m != null) return m;
return AccessTools.Method(typeof(CompTurretGun), "CompTick");
```

RimWorld 버전에 따라 틱 메서드 이름이 `CompTick`이거나 `CompTickInterval`일 수 있어서,
있는 쪽을 골라 잡는 방어 코드입니다. (`CompTurretGun`은 바닐라라 `typeof`로 바로 쓸 수 있어요)

### Postfix 본문 — 상태 기계(state machine)

이 함수가 0.5초마다 불리면서 아래 순서로 판단합니다:

```
1. 이 폰이 대상(ADlunchbox)인가? 아니면 return
2. 터렛 총의 현재 탄창 수(curMag)를 읽음
3. curMag != 0 (탄이 있음) → 타이머 지우고 return   ← 할 일 없음
4. curMag == 0 (빔):
   a. 타이머가 아직 없으면 → "지금부터 reloadTime 뒤에 완료" 예약하고 return
   b. 예약 시각이 아직 안 됐으면 → return (기다림)
   c. 시각이 됐으면:
        가방에서 magSize만큼 탄약 소모 시도
        - 소모 성공 → 탄창 채우고 타이머 삭제 (재장전 완료!)
        - 탄 없음  → 250틱 뒤 재시도 예약 (재보급 기다림)
```

핵심은 **"탄창이 비었다"는 순간을 잡아서, 바로 채우지 않고 타이머를 걸었다가
시간이 지나면 채운다**는 점. 이게 "재장전에 10초 걸린다"는 느낌을 만들어요.

```csharp
if (!pawn.IsHashIntervalTick(CheckIntervalTicks)) return;
```
- 매 틱이 아니라 30틱마다 한 번만 실행하게 하는 최적화. `IsHashIntervalTick`은
  폰마다 다른 시점에 분산 실행되게 해줘서 렉을 줄이는 엔진 기능이에요.

```csharp
int reloadTicks = (int)(GetReloadTime(ammoUser) * 60f);
ReloadFinishTick[gun] = now + reloadTicks;
```
- XML의 `reloadTime`(초)을 읽어 60을 곱해 틱으로 바꾸고, "지금 + 그만큼" 뒤를 완료 시각으로 예약.
- **XML 값을 읽어서 쓰기 때문에**, 나중에 XML에서 재장전 시간을 바꾸면 코드 수정 없이 반영됩니다.

---

## 14. 이 파일에서 꼭 가져갈 5가지

1. **Harmony 패치 = `[HarmonyPatch(대상)]` + `Postfix`/`Prefix` 메서드.**
   `__instance`는 원본 객체, 원본과 이름 같은 매개변수는 값이 자동으로 넘어온다.
2. **Postfix = 원본 뒤에 덧붙이기 / Prefix + `return false` = 원본 통째로 막기.**
3. **내 클래스는 점 찍고 바로 쓴다. 남의(참조 안 한) 클래스만 리플렉션으로 더듬는다.**
   이 파일이 긴 건 CE를 참조 안 해서지, 원래 어려운 게 아니다.
4. **`Prepare()`가 false면 패치 스킵 / `TargetMethod()`로 대상을 동적으로 지정** —
   의존 모드가 없어도 안 죽는 안전장치.
5. **틱 기반 로직은 "매 틱 검사 + Dictionary로 상태 기억"** 패턴으로 짠다.

---

## 15. 다음 연습 제안

이 파일을 이해했다면, 스스로 해볼 수 있는 가장 작은 변형:

- `SpareMagazines`를 3 → 5로 바꿔 예비 탄약을 늘려보기 (숫자 하나)
- `TargetPawnDefNames`에 다른 메크 defName을 추가해 적용 대상 넓히기 (문자열 하나)
- 스폰 시 로그를 찍어보기: 패치 ① 본문에
  `Log.Message($"{__instance.LabelShort} 로켓포드에 탄약 지급");`
  한 줄을 넣고, 게임에서 개발자 로그로 확인. → **Postfix가 실제로 불리는지 눈으로 보는 연습**

`Log.Message(...)`로 "내 코드가 언제 실행되는지" 찍어보는 게 모딩 디버깅의 8할입니다.
