# 분업 현황

작업이 겹치지 않도록 **누가 무엇을 끝냈고, 하고 있고, 할 예정인지** 적는 문서입니다. git에 올라가므로 `git pull`로 서로의 변경을 받습니다.

- 마지막 갱신: 2026-09-19
- 작업 ID는 기획 문서의 ID(P4-8 등)를 그대로 씁니다. 새 작업은 다음 빈 번호를 씁니다(현재 다음 번호: **P4-30**).

## 사용 규칙

1. **시작하기 전에** 아래 표에서 그 작업의 담당을 자기 이름으로, 상태를 `진행 중`으로 바꾸고 커밋·푸시합니다. 표에 없는 작업이면 행을 추가합니다.
2. **끝나면** 상태를 `완료`로 바꾸고 커밋 해시를 적습니다.
3. 상대가 `진행 중`인 작업의 파일은 고치기 전에 먼저 물어봅니다.
4. 작업을 시작할 때와 푸시하기 전에 `git pull`을 먼저 합니다.
5. 아래 "공용 파일"은 둘 다 자주 고치는 파일입니다. 고쳤다면 커밋 메시지에 적고, 가능하면 짧게 자주 푸시합니다.
6. **푸시하기 전에 테스트를 돌립니다**: Unity 메뉴 Window → General → Test Runner에서 **EditMode**(규칙 계산, 약 5초)와 **PlayMode**(실제 게임을 띄우는 스모크 테스트, 약 30초) 모두 통과해야 합니다. 기능을 추가하면 가능한 한 테스트도 함께 추가합니다.

상태 값: `진행 중` · `예정` · `완료` · `보류`

## 작업 현황 표

| ID | 작업 | 담당 | 상태 | 주요 파일·모듈 | 커밋 |
| --- | --- | --- | --- | --- | --- |
| P4-26 | 전투 지능 등급 재정리(본능 단계 추가, 카이팅·엄폐·분산·재선택을 등급별로) | 이재혁 | 완료 | Units/CombatTier·Combat·TargetSelector, Data/MonsterDefinition·DefaultContent | 9f5ab3a |
| P4-27 | 파티 역할과 대형(전열·후열, 이동 대형, 집중 공격 지정) | 이재혁 | 완료 | Adventurers/PartyTactics(신규), AdventurerStates, Units/TargetSelector·Combat·Unit, Visuals/TargetMarker(신규) | 1419d87 |
| P4-28 | 몬스터 전술(숙련 이상: 포위, 후퇴 후 합류, 무리 목표 지정) | 이재혁 | 완료 | Monsters/PackTactics·MonsterRegroupState(신규)·Monster, Units/Combat | 757850a |
| P4-29 | 사제 직업(치유·광역 치유, 치유 AI, 개구리 스킨, 출현 15%) | 이재혁 | 진행 중 | Adventurers, Units/Skills, UI/World/FloatingTextLayer | |
| P4-22 | 관측된 몬스터 장비 정보(GuildIntel 기록·둥지 위험도·LLM threat_notes) | 미정 | 예정 | Ecosystem/GuildIntel, Quests/QuestBoard, Adventurers/Prompts/DecisionPrompt | |
| P4-23 | 안개 속 마지막 목격 표시(시야에서 사라진 적을 회색 정지 모습으로) | 미정 | 예정 | Fog/FogGhostService(신규), UI/Views/MinimapView, UI/Views/InspectorView | |
| P4-13 | 건물 배치와 감시탑 | 미정 | 예정 | Town, World | |
| P4-19 | 스킬 기반 유인(은신·순간이동) | 미정 | 예정 | Units/Skills, Ecosystem/TerritoryTactics | |
| P5-1 | 밸런스 | 미정 | 예정 | Data/DefaultContent, GameBalance | |
| P5-2 | 성능 | 미정 | 예정 | 전반 | |
| P5-3 | LLM 지연과 예외 처리 | 미정 | 예정 | LLM/LlmGateway | |
| P5-4 | 세이브/로드 | 미정 | 예정 | Save(신규), 모든 Data | |
| P5-5 | Windows 빌드 | 미정 | 예정 | 빌드 설정 | |
| P5-6 | 시연 자료 | 미정 | 예정 | 영상·발표 자료 | |
| P4-9 | 사운드(효과음·BGM) — 가장 마지막에 진행 | 미정 | 예정 | Audio(신규) | |
| P4-12 | 대화 묶음 생성과 미리 받기 | 미정 | 보류 | LLM, Adventurers/BanterService | |
| P4-25 | 전체 코드 점검: 버그 8건 수정과 리팩터링(늑대 울음 버프, 사망자 부활, 귀환 중 후퇴, 안개 속 정보 노출, EventBus 할당 등) | 이재혁 | 완료 | Units/Combat·Skills, Adventurers, Monsters/Monster, UI/Views/InspectorView, Core/EventBus | b3a9183 |
| P4-24 | 플레이 모드 스모크 테스트(부팅·타이틀·ESC·장비 줍기·마을 줍기·토벌 출발·마을 경비) | 이재혁 | 완료 | Tests/PlayMode(신규) | 67ad896 |
| P4-11 | 의뢰 등록 위험 경고 | 이재혁 | 완료 | UI/Views/QuestBoardView, Adventurers/QuestRisk | 50a2c5c |
| P4-10 | 메뉴와 설정(타이틀·일시정지·설정) | 이재혁 | 완료 | UI/Views(신규), UI/UIRoot, Session/GameBootstrap | 15dbac6 |
| P4-21 | 몬스터 장비와 바닥 아이템 순환 | 이재혁 | 완료 | Monsters, Items(신규), Data/ItemDefinition, Data/DefaultContent, Adventurers(줍기) | 27997c4 |
| P4-8 | 전투 연출(타격 파티클, 치명타 히트스톱, 사망·레벨업 연출, 화면 흔들림) | 이재혁 | 완료 | Visuals/HitParticles, Visuals/CombatFeedback, Core/GameClock | f0df622 |
| P4-7 | 안개 비주얼(부드러운 경계, 탐색 지역 톤 다운) | 이재혁 | 완료 | Fog/FogOfWar | d895847 |
| P4-6 | 모험가 상세 창과 툴팁 | 이재혁 | 완료 | UI/Views/AdventurerDetailView, UI/Framework/UITooltip | 60b5bc8 |
| P4-5 | 미니맵 | 이재혁 | 완료 | UI/Views/MinimapView | 6b7c34f |
| P4-4 | 모험가 장비 구매 | 이재혁 | 완료 | Adventurers/GearShopping | 9539ae5 |
| P4-20 | 토벌 위치 지정·대화 중 일시정지·마을 경비 | 이재혁 | 완료 | Quests, Adventurers/TownGuardState | 8967c0e |
| P4-18 | 영역과 3파전(같은 종 다른 무리 포함) | 이재혁 | 완료 | Ecosystem/TerritoryService, Monsters | 9f25ba0 외 |
| P4-17 | 전투 등급 AI | 이재혁 | 완료 | Units/TargetSelector, Units/Combat | |
| P4-16 | NPC 대화 기록 | 이재혁 | 완료 | Adventurers/DialogueLog | |
| P4-15 | 여러 창 동시 사용 | 이재혁 | 완료 | UI/UIRoot, UI/Framework | |
| P4-3 | 행상인 | 이재혁 | 완료 | Town/Merchant*, Economy/MerchantMarket | |
| P4-2 | 제작 | 이재혁 | 완료 | Economy/CraftingService | |
| P4-1 | 건물 업그레이드 | 이재혁 | 완료 | Town/UpgradeService | |
| P3 | LLM 인지(특성·판단·기억·영입·파티·잡담·침공) | 이재혁 | 완료 | LLM, Adventurers, Recruitment | |
| P2·P2.5 | 코어 루프와 전투 AI | 이재혁 | 완료 | Units, Monsters, Economy, Quests | |
| P1 | 맵·카메라·A*·안개·아바타 | 이재혁 | 완료 | World, Fog, Player | |

## 담당자별 요약

### 이재혁
- **진행 중**: P4-29
- **예정**: (정해지면 적기)
- **완료**: P1 ~ P4-8, P4-10, P4-11, P4-15 ~ P4-18, P4-20, P4-21, P4-24 ~ P4-28 (위 표 참고)

### 팀원 (이름을 적어 주세요)
- **진행 중**:
- **예정**:
- **완료**:

## 공용 파일 (충돌 주의)

두 사람이 동시에 고치면 병합 충돌이 나기 쉬운 파일입니다. 새 시스템이나 새 창을 추가하면 거의 항상 이 파일들을 건드립니다.

| 파일 | 왜 자주 고치나 | 조율 방법 |
| --- | --- | --- |
| `Scripts/Gameplay/Session/GameBootstrap.cs` | 새 시스템 등록(합성 루트) | 등록 줄만 추가하고 순서 변경은 미리 말하기 |
| `Scripts/UI/UIRoot.cs` | 새 창·HUD 등록 | 등록 줄만 추가 |
| `Scripts/Gameplay/Session/DebugCommands.cs` | 검증용 디버그 명령 | 파일 끝에 자기 구역을 만들어 추가 |
| `Scripts/Gameplay/Data/DefaultContent.cs` | 몬스터·아이템·레시피·특성 데이터 | 밸런스(P5-1) 담당만 수치 변경 |
| `Scripts/Gameplay/Units/Unit.cs`, `Units/Combat.cs` | 전투 공통 규칙 | 고치기 전에 알리기 |
| `Scripts/Gameplay/Adventurers/AdventurerStates.cs` | 모험가 행동 상태 | 고치기 전에 알리기 |
| `Assets/GuildMaster/Scenes/Main.unity` | 씬(병합이 매우 어려움) | 한 사람만 수정, 가능하면 코드로 처리 |
