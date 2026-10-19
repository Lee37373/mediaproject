# 분업 현황

작업이 겹치지 않도록 **누가 무엇을 끝냈고, 하고 있고, 할 예정인지** 적는 문서입니다. git에 올라가므로 `git pull`로 서로의 변경을 받습니다.

- 마지막 갱신: 2026-10-19
- 작업 ID는 기획 문서의 ID(P4-8 등)를 그대로 씁니다. 새 작업은 다음 빈 번호를 씁니다(현재 다음 번호: **P4-92**).
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
| P4-61 | 머지 뒤 버그 5건: `N` 키 충돌(이름표를 **F5**로), 단축키 도움말 오표기(`B`는 건설·`L`이 정산), 둥지 종족 정보 은닉(칸만 봐도 종이 밝혀지던 것), 목줄 귀소(다른 무리에 맞는 중에 집에 가던 것), 귀갓길 전리품 무시. 속도 **x8**(`4` 키)과 테스트용 `SeeAtCamera` 추가 | 이재혁 | 완료 | UI/World/NameTagLayer·UIRoot·Views/KeyHelpView·InspectorView, Ecosystem/GuildIntel·Nest·TerritoryTactics, Quests/QuestBoard, Monsters/MonsterLeash·Monster, Core/GameClock, Session/GameHotkeys·CameraVision(신규)·DebugCommands, UI/Views/HudView | 0115642, 2154bd6, a2a424a, e924f17 |
| P4-62 | 몬스터가 전장의 **재료를 먹고 등급 경험치**를 얻음(사용자 결정): 모든 줍는 종이 재료를 먹고, 재료당 값만큼 등급 경험치, 15마다 전투 등급 +1단계(최대 +2). 관측한 등급이 둥지 위험도를 올려 게시판 표시가 실제와 어긋나지 않게 함. 목줄 유예 6→12초(숙련 이상은 간격을 두고 싸움). 유인 스모크 테스트 안정화(엉뚱한 개체와 싸우던 것·귀소 한 프레임 латч) | 이재혁 | 완료 | Monsters/MonsterLevel·MonsterEquipment·Monster·MonsterLeash, Ecosystem/GuildIntel, Quests/QuestBoard, Tests/EditMode/MonsterItemTests, Tests/PlayMode/SmokeTests | 7920a44, d702f87, 18d2f35 |
| P4-63 | **파티 단위 경로 선택**(사용자 요청): 위험 = 시야 나쁜 땅(숲 1/3 × 지대 배율)을 새 위험 층으로 만들고, 그 가중치를 파티가 정함. 모험가는 **리더 성격**(용감함↔겁많음), **급하면 최단**(후퇴·귀환·침공 방어), 몬스터는 무리 단위로 숲 서식 종만 숲 선호. 가중치는 `SetDestination(..., caution)`으로만 걸려 전투·줍기 경로는 그대로 | 이재혁 | 완료 | World/RouteRisk(신규)·Pathfinder·PathFollower·WorldService, Units/RouteChoice(신규), Adventurers/AdventurerStates·FreeHuntState, Monsters/MigrateState·RoamState·RaidState, Session/DebugCommands, Tests/EditMode/RouteTests(신규), Tests/PlayMode/SmokeTests | f5a8936 |
| P4-64 | **자유탐험 실시간 경로 수정**(사용자 요청): 미탐색 지형은 열린 초원으로 가정해 계획하고, 본 뒤 막힌 곳이면 0.25초 안에 경로를 다시 그림. 목적지까지 한 번에 가지 않고 18칸 구간씩 부채꼴에서 고르며, 경로가 수정되면 구간을 다시 잡음. 정찰은 덜 탐색된 곳으로. 덤: 성문 공격 중 목표 재탐색으로 성문이 안 부서지던 버그 | 이재혁 | 완료 | World/Pathfinder·PathFollower·WorldService, Adventurers/ExplorationRoute(신규)·FreeHuntState, Monsters/Monster, Session/DebugCommands, Tests/EditMode/ExplorationTests(신규), Tests/PlayMode/SmokeTests | 921014d |
| P4-65 | **5일 밸런스 게임 오버 원인**: 게시 중에 대상 둥지가 전멸한 토벌 의뢰가 영원히 남아 게시판이 4일간 막힘(완료 1건, 수입·장비 0). 게시 때 대상 둥지를 기억하고, 길드가 그 둥지가 비었거나 다른 종 차지를 보면 아무도 안 받은 의뢰는 철회·환불, 받은 의뢰는 완료(사용자 결정, 이주·배회로 빈 경우 포함). 덤: 몬스터가 전리품 하나를 줍고 나머지를 두고 가던 버그(불안정 테스트 원인) | 이재혁 | 완료 | Quests/Quest·QuestBoard, Session/GameLog, Monsters/Monster·MonsterPickupState, Tests/PlayMode/SmokeTests | 6033f05 |
| P4-66 | **모닥불 야영과 습격**(사용자 제안): 마을이 멀 때 다치거나 지치면, 또는 밤이면(리더 성격) 파티가 모닥불을 피우고 쉼. 불빛을 본 몬스터가 습격, 파티는 불침번 교대, 불가 LLM 잡담 | 이재혁 | 완료 | Adventurers/CampRules·CampService·CampState·Campfire(신규)·AdventurerStates·BanterService, Monsters/Monster, Visuals/CartoonArt·CharacterVisual |  4caa223 |
| P4-67 | dev/KMS 머지(bc9cc2d "UI 특성 가림"): 방문자가 마을 잡담으로 길드원과 친해지면 호감도 85에서 스스로 가입, 영입 대화 6턴 제한 없앰·호감도 10 이하면 제안 불가, 잡담 1시간마다 50%·6칸. 충돌 없음 | 이재혁 | 완료 | 머지 전반(Adventurers/BanterService·Prompts/BanterPrompt, Recruitment/*, UI/Views/InspectorView·RecruitDialogView) | c9fde9c |
| P4-68 | **남은 밸런스 문제**(사용자 결정): 길드원 생활비(매일 자정 식비 10G + 마을 휴식 시간당 숙박비 2G, 못 내면 빚으로 쌓여 다음 수입에서 먼저 갚음, 야영은 무료), 방문 간격 8~12 → 6~8시간, 자동 플레이어가 영입 대화·침공 위험 종 우선 토벌. 5일 재측정: 길드 골드 800→495(무작위 맵)·800→1245(시드 20260920), 인원 6 유지, 침공 1회 방어 | 이재혁 | 완료 | Adventurers/LivingCosts·LivingCostRules(신규)·AdventurerData·AdventurerStates·PartySettlement·Prompts/DecisionPrompt, Economy/GuildTreasury, Recruitment/VisitorService, Reports/DailyReport, Session/BalanceAutoPlayer·BalanceProbe·GameBootstrap·GameLog, UI/Views/InspectorView·RosterView·AdventurerDetailView | 72526c2 |
| P4-69 | **전체 코드 점검 2차**(사용자 요청): 버그 7건 — 떠돌이가 된 침공 개체의 배회 예외·복귀, 휴가·중상 길드원의 자율 사냥·초대, 안개 속 유품 소식 누설, 의뢰 제안 위험도(P4-58 이전 방식), 정산 대기 중 죽은 멤버에게 몫, 정식 빌드의 F3, 마을회관 업그레이드 세금 무변화(1레벨 값 단일화: 세금 90→120→160, EXP 5→11→19, 사용자 결정). 리팩터링: 마을 상태 판정·전투 중 판정·"N시간 전" 중복 통합 | 이재혁 | 완료 | Monsters/Monster·MonsterStates·MigrateState, Ecosystem/*, Town/WaveManager, Adventurers/Adventurer·Decisions·PartyService·HuntBandService·PartySettlement, Welfare/GuildWelfare, Legacy/*, Proposals/QuestProposalService, Persuasion/PersuasionService, Data/DefaultContent, Session/GameBalance·GameBootstrap·GameHotkeys, Core/GameClock, Units/Unit, UI/Views/KeyHelpView | b8885f7, fc18f5e |
| P4-70 | 사용자 보고: 교전 중 숲으로 숨은 상대를 계속 봄(마지막 본 곳 수색 후 포기) | 이재혁 | 완료 | Units/Combat | e33eb98 |
| P4-71 | 사용자 보고: 파티원 혼자 후퇴(파티가 멈춰 회복·전체 후퇴), 마을에서 6시간 뒤 상설 파티 해산 | 이재혁 | 완료 | Adventurers/Party·PartyTactics·Adventurer·AdventurerStates·CampService | 7daa3f4 |
| P4-72 | 사용자 요청: 자연재해 5종(산불·독안개·눈사태·모래폭풍·지진)과 재해 몬스터 5종, 마을 자동 게시 해결 의뢰 | 이재혁 | 완료 | Disasters(신규), Data/DefaultContent, Quests, World, Monsters | 4c9fd66 |
| P4-73 | 사용자 요청: 전체 검증·리팩터링 3차(기획서 대조) | 이재혁 | 완료 | 전반 | 949d866, 50fb3e5, 51f9683, cd696df, d3f91d7 |
| P4-74 | 사용자 결정: 무너진·손상된 기본 건물을 골드로 수리(성문과 같은 방식) | 이재혁 | 완료 | Town/TownService·Building, UI/Views/BuildingView | 0dfb7aa |
| P4-75 | 사용자 결정: 재해 몬스터 희귀도 높음(배정 가중치 0.6→0.15) | 이재혁 | 완료 | Data/DefaultContent | 0dfb7aa |
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
| P5-2 | 성능(유닛 200·투사체 100에서 60FPS): 유닛 공간 색인(목표 고르기 O(N²) 제거), 안개 바뀐 곳만 갱신, 영역 검사 반경 질의, GC 30KB→약 5KB/프레임. 부하 p99 48.7→13.2ms | 이재혁 | 완료 | Units/UnitRegistry·TargetSelector·CrowdSimulation·Unit·Combat, Fog/FogOfWar, Ecosystem/TerritoryService·Nest, Core/ListView(신규), Monsters, Adventurers, UI/World/NameTagLayer, Session/PerfProbe(신규)·DebugCommands, Editor/ProfilerDigest(신규) | 3a78069 |
| P5-3 | LLM 예외 처리(실패해도 게임 진행·대기 상한·기아 방지·요청 통계). 속도 최적화는 제외(사용자 결정 2026-10-04) | 이재혁 | 완료 | LLM/LlmGateway·LlmStats(신규)·LlmTypes, UI/Views/LlmDebugView, Tests/PlayMode/FakeLlmBackend·LlmFaultTests(신규) | 9cc2c19 |
| P5-4 | 세이브/로드(사용자 결정: 완전 스냅숏·수동 여러 슬롯·오프라인 진행 없음): 시스템별 저장 섹션 33개, 일시정지 메뉴·타이틀의 저장/불러오기 창. 새 시스템을 만들면 `ISaveable`을 구현하고 `GameBootstrap`에서 `save.Register`할 것(CODEMAP 1.10절) | 이재혁 | 완료 | Core/Saving(신규), Save/*(신규), 거의 모든 시스템, UI/Views/SaveLoadView(신규)·PauseMenuView·TitleView, Tests/PlayMode/SaveTests(신규) | f8d5831, 1d30311, 37c1d30, 3040302 |
| P4-76 | 전체 코드 점검 4차(사용자 요청 2026-10-10): 구현한 기능을 코드로 역추적해 버그 12건 수정 — 저장(ID 카운터·불러온 침공/이주·호위 시간), 침공 뒤 파티 교착, 옛 세션 LLM 답, 정보 은닉(둥지), 경로 재검사, 스킬 대기시간 등. 측정 3회 | 이재혁 | 완료 | 전반(Save·Party·DefenseCoordinator·WaveManager·MigrationService·QuestBoard·PathFollower·GameLog·Nest·AnomalyWatch 등) | 3743884, c8a7751, ca57d24 |
| P4-77 | 전체 코드 점검 5차(사용자 요청 2026-10-10): P1~P4 기능을 동작 흐름(호출 순서)대로 따라가며 버그 7건 수정 — 자율 사냥 왕복, 다른 파티 리더 초대, 완수 호감 한쪽만, 파티 후퇴 감점, 야영 경보 12시간, 유품 상한, 몬스터 귀소 정체. 측정 2회 | 이재혁 | 완료 | FreeHuntState·PartyService·RelationshipRecorder·Adventurer·CampService·GroundItem(Service)·GuildLegacy·MonsterStates, Tests/PlayMode/SmokeTests | 1ac9bf6 |
| P4-78 | 사용자 결정(P4-76·77 관찰): 무너진 길드 홀은 시간당 EXP를 멈춤(수리하면 재개), 야영은 같은 파티(의뢰·상설 파티·사냥 무리)면 거리와 상관없이 함께 모임 | 이재혁 | 완료 | Economy/GuildTreasury, Adventurers/CampService·CampState·CampRules, UI/Views/HudView | 3156b38 |
| P4-79 | 사용자 결정 2026-10-10: 파티 리더는 휴식 중·길드 카운터에 있는 파티원을 기다렸다가 함께 나감(휴가·중상·한계 스트레스는 기다리지 않음). 리더가 서 있던 사람만 데리고 나가 남은 파티원이 14시간 마을 대기하던 문제 | 이재혁 | 완료 | Adventurers/Party(GuildParties)·AdventurerStates(TownIdleState) | 18b04fb |
| P4-80 | 버그 수정 이어서(사용자 요청 2026-10-11): 긴 측정 7회로 추적 — 자율 사냥이 본 바위에 갇힌 안개로 2시간 정지, 균열 속 전리품·줍기 포기 무효, 저장 반올림·일시정지 중 행동(저장 왕복 테스트 흔들림), 지도의 바위 속 구멍(레인저 열흘 갇힘), 못 걷는 칸 진입·부딪히면 재계획. AnomalyWatch 보강(이력·교전 집계) | 이재혁 | 완료 | Adventurers/ExplorationRoute·FreeHuntState·AdventurerLooting·Adventurer·TownGuardState, Monsters/Monster, Core/Saving, Units/Unit, World/MapGenerator·PathFollower, Session/AnomalyWatch, Tests | c096e07, 3e579f8, 6f29c9d, 6eeefb1, 489bdd0 |
| P4-81 | 맵 512×512(사용자 결정 2026-10-16): 땅 그림 메모리(지금 방식이면 약 1GB)·그리기 시간, 몬스터 수, 맵 크기를 따르는 거리 값 조정 | 이재혁 | 완료 | World/MapRenderer·MapGenerator·TerrainRegions·Pathfinder, Core/Bc1Encoder(신규), Fog/FogOfWar, Ecosystem/EcosystemManager, Monsters/MonsterLevel·MigrateState·Monster, Adventurers/FreeHunt, UI/Views/MinimapView, Session/GameBalance·GameBootstrap | c9a5cdf |
| P4-82 | 의뢰 구조 개편(사용자 결정 2026-10-16): 플레이어가 직접 올리는 의뢰를 없애고, 의뢰는 마을 자동 게시·돌발 이벤트·LLM 제안·호위로만. 보상은 의뢰처(시스템)가 내고 길드는 수수료를 받음. **KMS의 F-8(제안 보상 조절)·P4-59(협상 보상 인상)·F-5(의뢰 확장)에 영향(2026-10-17 사용자: 다른 사람 작업은 참고만 — 결정대로 바꿈)** | 이재혁 | 완료 | Quests/Quest·QuestBoard·QuestPricing(신규)·TownQuestPoster(신규), UI/Views/QuestBoardView·QuestProposalView·HudView, Proposals, Economy/GuildTreasury, Reports, Town/UpgradeService, Session/BalanceAutoPlayer | c7b8e68 |
| P4-83 | 지대 최종 보스 4종(사막·설원·늪지·황무지, 보스 전용 새 종): 잡으면 그 지대에 새 몬스터가 안 생기고, 넷 다 잡으면 승리 화면(계속 플레이 가능). KMS의 F-4(승리 목표)와 겹침(참고만, 2026-10-17 사용자) | 이재혁 | 완료 | Data/DefaultContent·MonsterDefinition, Monsters/RegionLords(신규)·Monster, Ecosystem/RegionLordService(신규)·EcosystemManager·Nest·MigrationService·TerritoryService·GuildIntel, Units/Skills, Quests, Disasters, UI/Views/VictoryView(신규)·EcologyView | 8d091f6 |
| P4-84 | 다른 마을(지도 아이콘): 지대마다 듬성듬성, 길드 마스터가 직접 가서 영입, 몬스터가 공격하면 그 마을이 방어 의뢰를 올림, 무너지면 영구 소멸하고 확률로 다른 곳에 새 마을 | 이재혁 | 완료 | Villages(신규), World/MapGenerator, Recruitment, Quests, Monsters, UI | 966d454 (정렬 버그 1e3bb65) |
| P4-85 | 돌발 이벤트 4종(여행자·상인 구출, 희귀 몬스터 출현, 유적·보물 발견, 대규모 무리 접근) → 의뢰. **F-5와 겹침** | 이재혁 | 완료 | Incidents(신규: 희귀 몬스터·대규모 무리·여행자 구출·유적 지하 미궁), World/Underground(지하 띠·입구 연결), Quests, Monsters, Ecosystem/MigrationService | f0fb857·0c26b14·6faae39·6703cf7·d397601 |
| P4-86 | 마을 넓히기: 골드·재료로 성벽을 단계별로 밀어냄(성문 이동, 땅 다시 그리기). 23 → 31 → 39 → 47칸, 즉시, 아는 둥지는 먼저 토벌, 건설 창(B) 맨 위 | 이재혁 | 완료 | Town/TownExpansion·TownExpansionRules(신규)·GateService·TownGate·ConstructionService, World/TownWalls(신규)·MapGenerator·MapRenderer·GridMap·PathFollower, Ecosystem/Nest, UI/BuildView·MinimapView | bfa8588 |
| P4-87 | 내실 건물: 길드원 전원 상시 스탯 버프, 길드원이 때린 적 약화, 정원 제한을 없애는 건물 | 이재혁 | 완료 | Town/FacilityRules·FacilityService(신규)·ConstructionService·ConstructionRules·UpgradeService·TownService, Units/Afflictions(신규)·Unit·Combat·Skills, Adventurers/AdventurerData·Adventurer·GuildRoster·QuestRisk·Decisions, Data/DefaultContent, UI/BuildView·BuildingView, Session/TrialRunner(신규) | 661026d |
| P4-88 | 측정 관찰 후속: 길드보다 강한 후보는 영입이 어려움, 마을 습격 빈도 절반, 약한 신입은 위험 의뢰를 잘 안 받음, 지대 군주 강화(공격 ×4·호위 8·강타 쿨 5초) | 이재혁 | 완료 | Recruitment, Villages, Adventurers/Decisions·RuleBasedDecisionMaker·PartyService, Monsters/RegionLords | db14b11 |
| P4-89 | 귀환 중 정지 조사(성벽·성문 옆에서 이동 중인 채 몇 시간 제자리) | 이재혁 | 완료 | World/PathFollower, Adventurers/ReturnToTownState·PartyTactics | 2f472e4 |
| P4-90 | P4-85 관찰 후속: 위험 낮은 구출 의뢰 최저 보상 올림, 지대 군주 의뢰를 파티 인원으로 위험 평가 | 이재혁 | 완료 | Incidents/IncidentRules, Quests/QuestRisk, Adventurers/Decisions | b27ea28 |
| P4-91 | NPC 행동 판단에서 LLM 제거(사용자 결정 2026-10-19): 의뢰·휴식·사냥 판단, 파티·사냥 초대 수락, 전리품 분배, 스킬 선택, 잡담을 규칙 기반으로. LLM은 영입 대화·설득 대화·의뢰 제안 카드에만 | 이재혁 | 진행 중 | Adventurers/LlmDecisionMaker(삭제)·PartyService·PartySettlement·SkillChoiceService·BanterService·Prompts, Session/GameBootstrap | |
| P5-5 | Windows 빌드 — 개발(사운드 포함)이 모두 끝난 뒤 맨 마지막에 진행(사용자 결정 2026-10-10) | 이재혁 | 예정 | 빌드 설정 | |
| P5-6 | 시연 자료 — 빌드 뒤 맨 마지막에 진행(사용자 결정 2026-10-10) | 미정 | 예정 | 영상·발표 자료 | |
| P4-9 | 사운드(효과음·BGM) — 개발 작업 중 가장 마지막에 진행(그 뒤 P5-5 빌드·P5-6 시연) | 미정 | 예정 | Audio(신규) | |
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
- **진행 중**: P4-91 NPC 행동 판단에서 LLM 제거
- **예정**: P5-5 Windows 빌드(맨 마지막)
- **완료**: P1 ~ P4-8, P4-10, P4-11, P4-13, P4-15 ~ P4-29, P4-31 ~ P4-43, P4-45 ~ P4-56, P4-58, P4-60 ~ P4-90, P5-1 ~ P5-4 (위 표 참고)

### KMS (이름을 적어 주세요)
- **진행 중**: 없음
- **예정**: F-4(명성·길드 등급·승리 목표), F-7(개발용 검증 명령). F-5(의뢰 종류·보상 확장)는 `QuestBoard.cs`를 건드려야 해서 P4-22와 조율이 필요합니다
- **바뀐 점 알림(2026-10-17, P4-82 완료 c7b8e68)**: 사용자 결정으로 의뢰 구조가 바뀌었습니다. 플레이어는 토벌을 직접 올리지 않고, 우리 마을이 길드가 본 둥지마다 토벌을 올립니다. 보상은 의뢰처가 내고 길드는 20%를 수수료로 받으며, 길드 EXP는 없어졌습니다. **F-8 제안 카드**: 게시판에 없는 둥지에 대한 "다른 의뢰인의 요청"이 되었고, 보상 조절 버튼과 금고 잔액 제한을 없앴습니다(고르기만, `ProposalRules.BuildPool(targets, power, members, rng)`). **P4-59 협상**: 그대로 길드 금고에서 얹습니다. F-5(의뢰 확장)를 할 때는 `QuestClient`·`QuestPricing`·`QuestBoard.TryPostRequest`를 쓰면 됩니다. P4-83(8d091f6)으로 지대 군주 넷을 잡으면 승리 화면이 뜹니다(`AllLordsSlainEvent`) — F-4의 승리 목표는 여기에 얹으면 됩니다
- **바뀐 점 알림(2026-10-19, P4-87 완료 661026d)**: 사용자 결정으로 **모든 모험가의 공격·방어·최대 체력이 ×0.75로 시작**하고, 성벽 안에 짓는 내실 건물(무기고·방어구 공방·식당 각 3레벨)로 예전 세기까지 돌아옵니다(`Town/FacilityRules`, `AdventurerStats.Compute(…, StatScale)`). 판단의 힘 `AdventurerStats.Power`도 이 배율을 곱하고(`RawPower`가 예전 값), 위험 낮음 요구치는 0.75입니다. 피해 공식은 `Unit.EffectiveDefense`·`Unit.AttackCooldownNow`(방어 감소·둔화 반영)를 씁니다. 정원은 대숙소가 있으면 없어집니다(`GuildRoster.IsFull`·`CountText`)
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
