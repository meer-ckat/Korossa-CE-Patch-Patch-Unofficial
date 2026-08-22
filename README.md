# Korossa CE Patch Patch (Unofficial)

RimWorld 모드 **[Korossa: Scorched Brass](https://steamcommunity.com/sharedfiles/filedetails/?id=3429142659)** 를
**[Combat Extended](https://steamcommunity.com/sharedfiles/filedetails/?id=2890901044)** 에 맞춰 패치하는 비공식 패치입니다.

> Steam 워크샵: https://steamcommunity.com/sharedfiles/filedetails/?id=3699697076
> GitHub: https://github.com/meer-ckat/GooGooGaaGaa

**패치할 수 있는 능력자는 직접 고쳐서 Pull Request 보내주세요.** 그게 이 저장소가 존재하는 이유입니다.

---

## 왜 공개하나

혼자 다 못 고칩니다. 밸런스가 이상하거나, 무기 하나가 CE verb를 안 먹거나, 새 DLC/모드 업데이트로 패치가 깨졌을 때
**직접 고칠 수 있는 사람이 고쳐서 PR을 올리는 게 제일 빠릅니다.** 반영되면 워크샵 모드에도 그대로 올라갑니다.

## 설치

1. Korossa: Scorched Brass 와 Combat Extended 를 먼저 구독/설치
2. 이 저장소를 RimWorld 의 `Mods` 폴더 안에 클론 (또는 ZIP 다운로드 후 압축 해제)

   ```
   cd ".../RimWorld/Mods"
   git clone https://github.com/meer-ckat/GooGooGaaGaa.git KORCEpatch
   ```

3. 모드 목록 순서: **Combat Extended → Korossa: Scorched Brass → 이 패치**

> ⚠️ starlellok 의 기존 Korossa CE 패치 모드(`korossa.ce`)와 **같이 쓰면 안 됩니다.** 이 모드는 그것을 대체합니다.

## 필요 모드

| 모드 | packageId |
|---|---|
| Korossa: Scorched Brass | `gyun.0.6.3.1` |
| Combat Extended | `ceteam.combatextended` |

지원 버전: RimWorld **1.6**

## 폴더 구조

```
About/                 모드 메타데이터, 프리뷰 이미지
LoadFolders.xml        DLC별 조건부 로드 설정
1.4/                   구버전 텍스처 (눈 색상)
1.6/
  Assemblies/          빌드된 DLL (게임에서 바로 사용)
  Defs/                새로 추가하는 Def (탄약, 레시피, 어빌리티 등)
  Patches/             기존 Def 를 고치는 XML 패치 (무기, 방어구, 터렛 …)
  Languages/           한국어 번역
  Source/              C# 소스 + 상세 문서(README_KR.txt)
  Textures/            탄약 / 투사체 / 무기 텍스처
1.6_BioTech/           Biotech 활성 시에만 로드 (메크 관련)
1.6_Royalty/           Royalty 활성 시에만 로드 (런처, 탄약)
1.6_Anomaly/           Anomaly 활성 시에만 로드
1.6_Ody/               Odyssey 활성 시에만 로드
```

## 기여하기 (Contributing)

### 1. XML만 고치는 경우 — 빌드 필요 없음

대부분의 밸런스/패치 수정은 XML만 건드리면 됩니다.

1. 저장소를 Fork → 브랜치 생성
2. `1.6/Patches/` 또는 `1.6/Defs/` 아래 XML 수정
3. 게임에서 실제로 켜서 확인 (개발자 모드 로그에 빨간 에러 없는지)
4. PR 생성 — **무엇을, 왜 바꿨는지** 한두 줄로 적어주세요

### 2. C# 을 고치는 경우 — DLL 재빌드 필요

- 프로젝트: `1.6/Source/KorossaCEPatchSource/KorossaCEpatch.csproj`
- 타겟: **.NET Framework 4.7.2** 클래스 라이브러리
- 참조: `Assembly-CSharp.dll`, `UnityEngine.CoreModule.dll`, `0Harmony.dll` (+ CE 어셈블리)
- 빌드 후 결과 DLL 을 `1.6/Assemblies/KorossaCEpatch.dll` 에 덮어쓰고 **함께 커밋**해 주세요

### 밸런스 기준

기본적으로 **현실 수치**를 따르되, 플레이 경험을 위한 아케이드적 조정은 허용합니다.
극단적인 수치 변경은 PR 설명에 근거를 적어주세요.

### 참고 문서

- [`1.6/Source/KorossaCEPatchSource/README_KR.txt`](1.6/Source/KorossaCEPatchSource/README_KR.txt) — 전체 변경 이력과 설계 메모 (필독)
- [`1.6/Source/KorossaCEPatchSource/Docs/`](1.6/Source/KorossaCEPatchSource/Docs/) — 터릿 탄약 공급 패치 해설, XML 밸런스 검증 스크립트

### 버그 제보

Issues 에 올려주세요. 있으면 좋은 것:

- RimWorld / CE / Korossa 버전
- 모드 목록 순서
- `Player.log` 또는 개발자 모드 콘솔 에러 전문
- 재현 방법

## 라이선스

이 저장소의 **패치 코드와 XML** 은 [MIT License](LICENSE) 를 따릅니다.

원본 모드(Korossa: Scorched Brass, Combat Extended)의 에셋·코드에 대한 권리는 각 원저작자에게 있습니다.
이 저장소에 포함된 원본 유래 리소스는 해당 모드와 함께 쓰기 위한 패치 목적으로만 사용됩니다.
원저작자가 삭제를 요청하면 즉시 따릅니다.

---

## English (short)

Unofficial **Combat Extended** compatibility patch for the RimWorld mod **Korossa: Scorched Brass**.

**PRs welcome — if you can fix it, fix it and send a pull request.**

- Requires: Korossa: Scorched Brass (`gyun.0.6.3.1`) + Combat Extended (`ceteam.combatextended`), RimWorld 1.6
- Load order: Combat Extended → Korossa: Scorched Brass → this patch
- **Do not** use together with starlellok's original Korossa CE patch (`korossa.ce`)
- Most fixes are XML only (`1.6/Patches`, `1.6/Defs`) — no build needed.
  C# changes require rebuilding `1.6/Source/KorossaCEPatchSource` (.NET Framework 4.7.2) and committing the updated DLL.
- Patch code/XML is MIT. Original mod assets remain the property of their respective authors.
