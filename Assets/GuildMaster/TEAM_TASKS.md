# 분업 현황

작업이 겹치지 않도록 **누가 무엇을 끝냈고, 하고 있고, 할 예정인지** 적는 문서입니다. git에 올라가므로 `git pull`로 서로의 변경을 받습니다.

- 마지막 갱신: 2026-09-23
- 작업 ID는 기획 문서의 ID(P4-8 등)를 그대로 씁니다. 새 작업은 다음 빈 번호를 씁니다(현재 다음 번호: **P4-61**).
- `F-*` 번호는 `Assets/GuildMaster/FEATURE_IDEAS.md`의 기능 번호입니다(KMS가 쓰던 번호를 그대로 살렸습니다).

## 사용 규칙

1. **시작하기 전에** 아래 표에서 그 작업의 담당을 자기 이름으로, 상태를 `진행 중`으로 바꾸고 커밋·푸시합니다. 표에 없는 작업이면 행을 추가합니다.
2. **끝나면** 상태를 `완료`로 바꾸고 커밋 해시를 적습니다.
3. 상대가 `진행 중`인 작업의 파일은 고치기 전에 먼저 물어봅니다.
4. 작업을 시작할 때와 푸시하기 전에 `git pull`을 먼저 합니다.
5. 아래 "공용 파일"은 둘 다 자주 고치는 파일입니다. 고쳤다면 커밋 메시지에 적고, 가능하면 짧게 자주 푸시합니다.
6. **푸시하기 전에 테스트를 돌립니다**: Unity 메뉴 Window → General → Test Runner에서 **EditMode**(규칙 계산, 약 5초)와 **PlayMode**(실제 게임을 띄우는 스모크·회귀 테스트, 약 1분, 고정 시드) 모두 통과해야 합니다. 기능을 추가하면 가능한 한 테스트도 함께 추가합니다.

상태 값: `진행 중` · `예정` · `완료` · `보류`

## 작업 현황 표

| ID | 작업 | 담당 | 상태 | 주요 파일·모듈 | 커밋 |
| --- | --- | --- | --- | --- | --- |
| F-1 | 알림 시스템(토스트·알림 목록·HUD 배지) | KMS | 완료 | Gameplay/Notifications(신규), UI/Views/NotificationView·ToastLayer·NotificationActions(신규) | 21a1e5d (머지 38d9838) |
| F-2 | 스트레스·피로의 결과(불만·이탈·면담·휴가·보너스) | KMS | 완료 | Gameplay/Welfare(신규), UI/Views/MemberCareView(신규) | 21a1e5d (머지 38d9838) |
| F-3 | 일일 정산 보고서 | KMS | 완료 | Gameplay/Reports(신규), UI/Views/DailyReportView(신규), Economy/GuildTreasury | 21a1e5d (머지 38d9838) |
| F-6 | 죽음·부상의 결과(유품·구사일생·추모의 벽) | KMS | 완료 | Gameplay/Legacy(신규), UI/Views/MemorialView(신규), Adventurers/Adventurer·AdventurerData | 21a1e5d (머지 38d9838) |
| F-8 | LLM 의뢰 제안(후보 카드에서 고르기) | KMS | 완료 | Gameplay/Proposals(신규), UI/Views/QuestProposalView(신규), Quests/QuestBoard | 21a1e5d (머지 38d9838) |
| P4-59 | 의뢰 설득·협상(보상 인상 요구, 역제안) — KMS 문서에는 `P4-24·P4-25`로 적혀 있으나 그 번호는 아래 다른 작업이 이미 쓰고 있어 새로 부여했습니다 | KMS | 완료 | Gameplay/Persuasion(신규), Quests/QuestBoard·Quest, UI/Views/PersuadeDialogView(신규), Recruitment | 21a1e5d·90b314b (머지 38d9838) |
| P4-60 | dev/KMS 머지: 충돌 2건 해결(ArtCatalog 몬스터 13종 유지, ProposalTests 난이도 눈금), `Assets/Arts`·`Claude outputs` 제외, flaky 스모크 테스트 1건 안정화 | 이재혁 | 완료 | 머지 전반, Tests/EditMode/ProposalTests, Tests/PlayMode/SmokeTests, .gitignore | 38d9838 |
| P4-26 | 전투 지능 등급 재정리(본능 단계 추가, 카이팅·엄폐·분산·재선택을 등급별로) | 이재혁 | 완료 | Units/CombatTier·Combat·TargetSelector, Data/MonsterDefinition·DefaultContent | 9f5ab3a |
| P4-27 | 파티 역할과 대형(전열·후열, 이동 대형, 집중 공격 지정) | 이재혁 | 완료 | Adventurers/PartyTactics(신규), AdventurerStates, Units/TargetSelector·Combat·Unit, Visuals/TargetMarker(신규) | 1419d87 |
| P4-28 | 몬스터 전술(숙련 이상: 포위, 후퇴 후 합류, 무리 목표 지정) | 이재혁 | 완료 | Monsters/PackTactics·MonsterRegroupState(신규)·Monster, Units/Combat | 757850a |
| P4-29 | 사제 직업(치유·광역 치유, 치유 AI, 개구리 스킨, 출현 15%) | 이재혁 | 완료 | Adventurers/AdventurerData·Adventurer, Units/Skills·Unit, UI/World/FloatingTextLayer | 47b6896 |
| P4-22 | 관측된 몬스터 장비 정보(GuildIntel 기록·둥지 위험도·LLM threat_notes) | 이재혁 | 완료 | Ecosystem/GuildIntel, Quests/QuestBoard, Adventurers/Prompts/DecisionPrompt, UI/Views/EcologyView·InspectorView·QuestBoardView, Session/DebugCommands | 5fe074e |
| P4-23 | 안개 속 마지막 목격 표시(시야에서 사라진 적을 회색 정지 모습으로) | 이재혁 | 완료 | Fog/FogGhostService·FogGhost(신규)·FogComponents, Monsters/Monster, Items/GroundItem, Ecosystem/Nest, UI/Views/MinimapView·InspectorView, Session/GameBootstrap·DebugCommands | 9704a34 |
| P4-13 | 건물 배치와 감시탑(배치 시스템·감시탑·철거·시야 교체) | 이재혁 | 완료 | Town/ConstructionService·ConstructionRules(신규)·TownService·Building, Data/BuildingDefinition·DefaultContent, UI/Views/BuildView(신규)·BuildingView·UIRoot, Visuals/CartoonArt, Session/GameBalance·GameBootstrap·DebugCommands | 7ea3d7e |
| P4-30 | 감시탑 주둔(길드원 인원 배치) | 미정 | 보류 | Adventurers, Town | |
| P4-31 | 사용자 보고 수정: 일시정지 중 말풍선, 퀘스트 중복 수락, 수락된 퀘스트 취소 시 보상 지급(어뷰징 차단) | 이재혁 | 완료 | Quests/QuestBoard, Adventurers/PartyService, UI/World/SpeechBubbleLayer, Session/DebugCommands | 6138f7c |
| P4-32 | 둥지 소탕 퀘스트 제거, 토벌 퀘스트가 구역 둥지 비우기로도 완료 | 이재혁 | 완료 | Quests/Quest·QuestBoard, Adventurers/AdventurerStates, UI/Views/QuestBoardView | 4a1e4e3 |
| P4-33 | 전리품 획득 루트(한 번에 돌며 줍기, 중간에 적 보이면 전투 우선) | 이재혁 | 완료 | Adventurers/AdventurerLooting | 00eb604 |
| P4-34 | 스킬 습득: 레벨업 때 LLM이 선택, 최대 3개(초과 시 LLM이 잊을 것 선택), 쿨타임 UI | 이재혁 | 완료 | Adventurers/SkillCatalog·SkillChoiceService·Prompts/SkillPrompt(신규)·Adventurer·AdventurerData, UI/World/SkillBarLayer(신규)·UIRoot·UIBuilder, Session/GameBootstrap | c3c380e |
| P4-35 | LLM 고장 리커버리(stale 마감 시한, 연속 실패 시 재기동, HUD 표시) | 이재혁 | 완료 | LLM/LlmGateway·LLMUnityBackend·LlmTypes, Session/DebugCommands | e7ad181 |
| P4-19 | 스킬 기반 유인(은신·순간이동) | 이재혁 | 완료 | Units/Skills·Unit·Combat, Ecosystem/TerritoryTactics, Adventurers/AdventurerStates·SkillCatalog, Visuals/CharacterVisual | 71dc597 |
| P5-1 | 밸런스 | 이재혁 | 완료 | Data/DefaultContent·MonsterDefinition, Session/GameBalance·BalanceProbe·BalanceAutoPlayer(신규), Ecosystem/EcosystemManager·EcologyWarning(신규)·GuildIntel·Nest, World/MapData·MapGenerator, Town/TownService, Monsters/RaidState, UI/Views | 2750a2e |
| P4-36 | 사용자 보고 5건: 유인해도 안 싸움, 디버그 아닌데 범위 표시, 안 가본 둥지 미니맵 표시, 골드 부족, 영입 대화 문맥 | 이재혁 | 완료 | Ecosystem/TerritoryService·EcosystemManager·GuildIntel·Nest, Session/GameHotkeys·GameBalance, Units/UnitGizmos, Recruitment/RecruitPrompt·RuleBasedRecruiter | 201312e |
| P4-37 | 의뢰가 없을 때 자율 사냥(파티 단위 밴드, 무리 추정, 길드 공유 정보, 생태계 제동) + 몬스터 젠율 상향 | 이재혁 | 완료 | Adventurers/FreeHunt·FreeHuntState·HuntBandService(신규)·PartyTactics·Decisions·LlmDecisionMaker·Prompts/DecisionPrompt, Data/DefaultContent, Ecosystem/EcosystemManager, Session/GameBootstrap | 37b5f32 |
| P4-38 | 전투 밸런스 재조정: 방어 비율 공식, 자율 사냥 판단을 실제 스탯 기반 전투 추정으로·전력에 따라 넓어지는 반경, 파티 결속(리더 기준 줄·기다리기·함께 귀가), 시작 4인·방문 간격·모집 공고 | 이재혁 | 완료 | Units/Combat·Skills, Adventurers/FreeHunt·FreeHuntState·HuntBandService·PartyTactics·Adventurer·AdventurerStates·AdventurerGenerator, Recruitment/VisitorService, UI/Views/RosterView, Session/GameBalance·GameBootstrap | f0e6f14 |
| P4-39 | 성문: 길드는 자유 출입, 몬스터는 성문을 부숴야 진입(성벽은 못 부숨, 쫓던 몬스터도 부숨), 성문 공격 시 침공 경보, 골드로 수리 | 이재혁 | 완료 | World/GridMap·Pathfinder·PathFollower·WorldService, Town/TownGate·GateService(신규)·WaveManager, Monsters/MonsterGateBreakState(신규)·Monster·MonsterStates·RaidState, Units/Unit·CrowdSimulation·Skills, Visuals/CartoonArt, UI/Views/InspectorView, Session/GameBootstrap·GameBalance·GameLog | 0c03114 |
| P4-40 | 사용자 보고: 레벨업 느림(파티 EXP 공유)·초반 교전 부족(사냥터 선택·모집 대기)·약한 몬스터 증식 상향·침공대(벽 너머 사격·성문 앞 몰림·우회)·침공 뒤 전리품 안 줍기 | 이재혁 | 완료 | Adventurers/Adventurer·FreeHuntState·HuntBandService·DefendState·AdventurerStates·TownGuardState, Monsters/RaidState·MonsterGateBreakState, Town/GateService, Data/DefaultContent | 51302ae |
| P4-41 | 사용자 보고: 마을 시야 성벽 밖까지(8→18, 둥지 최소 거리 16→20)·몬스터 증식은 마릿수 대신 시간(약한 세 종 정원 원복·번식 속도 상향)·빠른 파티원 멈칫거림 | 이재혁 | 완료 | Session/GameBalance, Data/DefaultContent, Adventurers/PartyTactics | 0a77c97 |
| P4-42 | 사용자 보고: 초반 돌 슬라임 안 잡음(둥지 남길 수 비례·관측 뒤 번식 추정)·침공 궁수 무시(반격·마을 사거리 원거리 허용)·전투 중 줍기(동료 교전 감지)·전투 시간 3배(유닛 체력·치유 ×3) | 이재혁 | 완료 | Adventurers/FreeHunt·FreeHuntState·DefendState·AdventurerLooting·Adventurer, Units/Combat·Skills, Monsters/Monster | 9855ff8 |
| P4-43 | 사용자 결정: 보호 대상 없앰(둥지 마지막 몇 마리·멸종 위기 종 사냥 중단 삭제, 약한 슬라임을 "건드릴 상대가 아니야"로 피하던 원인)·멸종 개념 삭제(빈 둥지 6시간 뒤 2마리, 경고 표시 삭제) | 이재혁 | 완료 | Ecosystem/EcosystemManager·Nest, Adventurers/FreeHunt·FreeHuntState, UI 생태·게시판·게임 오버 | b859a56 |
| P4-45 | 사용자 보고: 적 종끼리 안 싸움(모든 다른 종끼리 적대, 사용자 결정)·성문 부수는 몬스터 무시(마을 경비가 대응)·점검에서 찾은 성문/미니맵 초기화 전 예외 | 이재혁 | 완료 | Monsters/Monster, Adventurers/TownGuardState, Town/TownGate, UI/MinimapView | dcb1fbf, 4de02d6 |
| P4-46 | 사용자 보고: 유인한 몬스터가 싸우다 둥지로 돌아감(마스터를 미끼로 취급해 싸울 수 있는 적 우선, 몬스터끼리 주고받는 중엔 귀소 안 함) | 이재혁 | 완료 | Units/TargetSelector, Monsters/MonsterStates | da43018 |
| P4-47 | 근접 교전 국소 이동(컨텍스트 스티어링): 사거리 안에서 기다리는 동안 옆걸음·치고 빠지기·장판·겹침 회피(숙련+), 테스트·교전 때 카메라가 싸움을 따라감(FightCamera) | 이재혁 | 완료 | Units/LocalSteering(신규)·Combat·CombatTier, World/PathFollower, Visuals/CharacterVisual, Session/SkirmishMonitor·FightCamera(신규)·DebugCommands, Tests/PlayMode | 1230431 |
| P4-48 | 숲 시야: 숲 밖에서는 숲이 벽처럼 시야를 가림(가장자리 한 칸만 보임), 숲 안에서는 가시거리 1/3. 안개·유닛 AI 모두, 마을·감시탑 시야는 예외 | 이재혁 | 완료 | World/WorldService·GridMap·ForestSight(신규), Fog/FogOfWar, Units/Unit·UnitGizmos, Adventurers/FreeHuntState·AdventurerLooting, Monsters/Monster | 017d6af |
| P4-49 | 길드원 창(R) 파티별 묶음: 같은 의뢰·같은 사냥 무리끼리(리더 먼저), 나머지는 혼자 | 이재혁 | 완료 | UI/Views/RosterView | 261f73d |
| P4-50 | 파티 생애주기(사용자 제안): 길드에서 리더가 모집 → 함께 출발 → 전리품 공동 자루 → 귀환 후 리더가 LLM으로 분배 → 여러 번 유지, 불만이면 이탈(기여도: 가한·받은 피해·치유·처치) | 이재혁 | 완료 | Adventurers/Party·PartySettlement·Prompts/SplitPrompt(신규)·PartyService·HuntBandService·AdventurerStates, Session/GameBootstrap·GameLog | 4877d7a |
| P4-51 | 자율 사냥도 LLM 초대(사용자 요청): 사냥을 정한 리더가 마을의 길드원을 초대(성격·관계·상태로 수락/거절), 답이 모이면 함께 출발 | 이재혁 | 완료 | Adventurers/PartyService·HuntBandService·FreeHuntState·MemoryRecorder·Prompts/PartyPrompt | 8632694 |
| P4-52 | 사용자 보고: 적 무리 한 마리가 맞아도 나머지가 안 싸움(비명: 8칸 안 같은 무리가 공격자에게 합류, 목격: 동료와 싸우는 적도 목표) | 이재혁 | 완료 | Monsters/Monster | 2b66b44 |
| P4-53 | 맵 256×256(사용자 결정), 둥지 밀도 유지(16→64), 맵 전체 기준 거리 조정 | 이재혁 | 완료 | Session/GameBalance, Adventurers/FreeHunt, Tests | 8abbfda |
| P4-54 | 숲을 낀 전투 검증(사용자 요청): 숲 안·가장자리·밖↔안 교전이 끝까지 올바르게 진행되는지 측정·수정 | 이재혁 | 완료 | Adventurers/Adventurer·AdventurerLooting, Session/DebugCommands, Ecosystem/GuildIntel | 8abbfda |
| P4-55 | 지형 지대(사용자 결정): 초원(마을 주변)+사막·설원·늪지·황무지, 방향별 큰 지대, 이동·시야 배율, 색 변경(땅·나무·물·미니맵·몬스터) | 이재혁 | 완료 | World/TerrainRegions(신규)·MapData·MapGenerator·GridMap·ForestSight·CartoonGroundPainter·MapRenderer, UI/MinimapView, Monsters/Monster | 9fa1bcb |
| P4-56 | 무리 이주(사용자 결정): 둥지에서 확률로 떠난 무리가 마을 쪽 둥지 자리로 옮겨 감(싸워서 빼앗음), 가는 길의 적과 싸우고 제자리 회복 후 계속, 마을 가까운 둥지의 무리만 마을 침공 | 이재혁 | 완료 | Ecosystem/MigrationService(신규), Monsters/MigrateState(신규)·Monster, Town/WaveManager, Session/GameBootstrap·DebugCommands | 5c94763 |
| P4-57 | 사용자 보고·결정: 몬스터끼리 싸운 뒤 전리품 줍기(전투 직후·귀소 중), 종마다 보스 개체(둥지마다 낮은 확률, 죽으면 다시 안 생김) | 이재혁 | 완료 | Monsters/Monster·MonsterBoss(신규), Data/MonsterDefinition·DefaultContent, Ecosystem/EcosystemManager·MigrationService, Editor/ArtCatalogBuilder, UI/World/NameTagLayer | de157ed |
| P4-58 | 사용자 결정: 강함은 레벨(종 능력치 차이 축소·태어남 레벨 1~15·위험도는 레벨로), 지형별 서식(지형만으로 배정), 무리는 쪼개지거나 합쳐지지 않음, 쫓겨난 무리는 서식 지형의 빈 땅을 떠돎 | 이재혁 | 완료 | Monsters/MonsterLevel·Monster, Data/DefaultContent·MonsterDefinition, Quests/QuestBoard, Adventurers/FreeHunt·Decisions, Ecosystem/EcosystemManager·TerritoryService·MigrationService·RoamingPackService(신규)·Nest, Monsters/RoamState(신규), UI/Views | d1b67ea, 02084af |
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
- **진행 중**: 없음
- **예정**: (정해지면 적기)
- **완료**: P1 ~ P4-8, P4-10, P4-11, P4-13, P4-15 ~ P4-29, P4-31 ~ P4-43, P4-45 ~ P4-56, P4-58, P4-60, P5-1 (위 표 참고)

### KMS (이름을 적어 주세요)
- **진행 중**: 없음
- **예정**: F-4(명성·길드 등급·승리 목표), F-7(개발용 검증 명령). F-5(의뢰 종류·보상 확장)는 `QuestBoard.cs`를 건드려야 해서 P4-22와 조율이 필요합니다
- **완료**: F-1, F-2, F-3, F-6, F-8, P4-59 (자세한 내용은 `WORK_SUMMARY.md`, `FEATURE_IDEAS.md`, `NEGOTIATION.md`)

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

## 아트는 git에 올리지 않습니다 (2026-09-23)

캐릭터·몬스터 그림은 Asset Store의 LAYERLAB **2D Characters** 3종(Cute Adventurers, Casual Monsters, Minimal Characters)입니다. **EULA가 재배포를 금지하므로 git에서 제외**합니다(`.gitignore`의 `/Assets/Layer Lab/`, `/Assets/Arts/`).

각자 PC에서 **Package Manager → My Assets**로 세 패키지를 import하면 끝입니다. 가져오기 설정(발 기준점, PPU)은 `Scripts/Editor/ArtImportSettings.cs`가 자동 적용하고, `Resources/ArtCatalog.asset`(git 추적, 그림 없이 GUID 참조만)이 자동으로 연결합니다. 필요하면 메뉴 `GuildMaster > 아트 카탈로그 다시 만들기`를 누르세요. 패키지가 없으면 `Visuals/PixelArt.cs`의 코드 생성 그림으로 대체되어 게임은 정상 동작합니다.

- **폴더 이름을 바꾸지 마세요.** `Assets/Arts/`처럼 다른 위치에 같은 그림을 두면 GUID가 겹쳐 충돌합니다(dev/KMS 머지에서 실제로 걸렸던 문제).
- **`ArtCatalog.asset`을 아트 없이 다시 만들지 마세요.** 몬스터 목록이 비어 버리고, 그대로 커밋하면 상대의 그림이 사라집니다.
- 계정에 이 에셋이 없어서 import가 안 되면 먼저 알려 주세요. 배포 방법을 따로 정해야 합니다.
