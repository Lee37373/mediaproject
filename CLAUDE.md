# 길드 마스터 타이쿤 (Guild Master Tycoon)

Unity 6 (6000.3.24f1) 2D URP 게임입니다. LLM 기반 길드 경영 시뮬레이션이고, 방치형이라 "보는 재미"가 중요합니다.

- 설계: `Assets/GuildMaster/Docs/DESIGN.md`. 원본 기획서와 달라진 결정 사항이 표로 정리돼 있습니다.
- **구현 계획**: `Assets/GuildMaster/Docs/PLAN.md`. 작업 ID별 사양, 파일, 완료 기준, 미결 질문이 있습니다.
- **진행 상황**: `Assets/GuildMaster/Docs/PROGRESS.md`. 체크리스트, 다음 작업, 세션 기록이 있습니다.
- **세션 시작 순서**
  1. PROGRESS.md에서 "다음 작업"을 확인합니다.
  2. PLAN.md에서 해당 작업의 사양을 읽습니다.
  3. 미결 질문(❓)이 있으면 사용자에게 묻습니다.
  4. 구현하고 검증합니다.
  5. PROGRESS.md를 갱신합니다.

## 사용자 선호
- 패키지를 설치하기 전에 **이유와 대안을 설명하고 확인**을 받으세요.
- 기획서에 틀린 내용이 있을 수 있습니다. 모호하거나 이상한 부분은 추측하지 말고 **질문하세요**.
- 순차 개발과 모듈화를 중시합니다. 사용자와의 대화, 그리고 게임 UI와 대사는 한국어입니다.

## 아키텍처 (asmdef, 의존은 한 방향)
```
GuildMaster.Core      EventBus(struct 이벤트), Services(서비스 로케이터), GameClock, PoolService, SpriteFactory, GameRandom
GuildMaster.LLM       LlmGateway(우선순위 큐·타임아웃·중복 제거), ILlmBackend, LLMUnityBackend(#if LLMUNITY_PRESENT), GbnfObject(JSON 문법), LlmJson, LlamaEnvironment
GuildMaster.Gameplay  World(맵·A*·PathFollower), Fog, Units(Unit·Combat·Skills·Tactics·Crowd·Telegraph·Projectile), Monsters, Ecosystem,
                      Economy, Quests, Adventurers, Traits, Town, Player, Visuals, Data(ScriptableObject), Session(GameBootstrap·GameLog·DebugCommands)
GuildMaster.UI        UIRoot(합성 루트), Framework(UIBuilder·UIPanel·UITheme·UIFonts), Views, World(플로팅 텍스트·말풍선)
GuildMaster.Tests.EditMode
```
- **합성 루트**
  - `GameBootstrap`(실행 순서 -1000)이 모든 게임플레이 시스템을 명시적 순서로 만들고 `Services`에 등록합니다.
  - `UIRoot`(-500)가 UI를 만듭니다.
  - 씬 `Assets/GuildMaster/Scenes/Main.unity`에는 GameBootstrap, UI, LLM 오브젝트만 있습니다.
- **모듈 간 통신은 `EventBus` 이벤트(struct)로** 합니다. 이벤트 struct는 발행하는 모듈 파일 상단에 정의합니다.
- **AI는 하이브리드**입니다.
  - 저수준(이동·전투·생존)은 C# FSM(`StateMachine<T>`, `State<T>`)이 맡습니다.
  - 고수준 판단(퀘스트 선택 등)은 `IAdventurerDecisionMaker`가 맡습니다. LLM 구현체와 `RuleBasedDecisionMaker`(항상 쓸 수 있는 대체 경로)가 있습니다.
- **아트**: 캐릭터·몬스터는 Asset Store의 LAYERLAB **2D Characters** 3종(Cute Adventurers, Casual Monsters, Minimal Characters)을 씁니다.
  - 패키지는 `Assets/Layer Lab/`에 import된 원본 그대로 쓰고, **EULA상 재배포 금지라 git에서 제외**합니다.
  - 게임은 `Resources/ArtCatalog.asset`(git 추적, 이미지 없이 GUID 참조만)으로 찾습니다(`Visuals/ArtLibrary.cs`, `Visuals/CharacterArtCatalog.cs`).
  - **새 PC 설정**: Package Manager → My Assets에서 세 패키지를 import하면 끝입니다. 가져오기 설정(발 기준점, PPU)은 `Scripts/Editor/ArtImportSettings.cs`가 자동 적용하고, 카탈로그는 GUID로 자동 연결됩니다(필요하면 메뉴 `GuildMaster/아트 카탈로그 다시 만들기`).
  - 패키지가 없으면 `Visuals/PixelArt.cs`의 코드 생성 그림으로 대체합니다.
  - 맵은 같은 카툰풍으로 코드가 그립니다: 지형 `World/CartoonGroundPainter.cs`(16×16칸 덩어리 텍스처, 1칸 32px, 곡선 경계·외곽선, 시작 시 약 2초), 나무·바위·둥지·건물·건물 내부 `Visuals/CartoonArt.cs`.
- **UI는 uGUI를 코드로 조립**합니다(`UIBuilder`). 한글은 OS 폰트(맑은 고딕)를 씁니다. 레이아웃은 LayoutGroup 기반입니다.
  - 주의: `childForceExpand`가 켜진 LayoutGroup은 스스로 flexible이 됩니다. 필요하면 `Size(..., flexWidth: 0)`로 막으세요.
- **정보 은닉**: 플레이어·NPC·UI·로그·LLM 프롬프트는 **시야로 확인한 정보만** 씁니다. 몬스터·둥지 정보는 `EcosystemManager`가 아니라 `GuildIntel`(관측 기록)에서 읽으세요.
- **JSON은 `JsonUtility`와 snake_case DTO**를 씁니다. 게임 코드에서 Newtonsoft는 쓰지 않습니다(LLMUnity 의존성으로 설치돼 있을 뿐입니다).

## 코딩 규칙
- 코드 주석은 영어, 사용자에게 보이는 문자열은 한국어입니다.
- 파일 하나에 기능 단위 하나를 둡니다. 새 시스템은 `GameBootstrap`에 등록합니다.
- 반복 생성하는 오브젝트는 `PoolService`에 키를 등록해 씁니다(`IPoolable`).
- `.NET Standard 2.1`에는 없는 API가 있습니다(예: `FirstOrDefault(pred, default)`).
- 파일 이름이 `*System.cs`인 스크립트가 컴파일에서 빠지는 현상이 한 번 있었습니다(원인 불명). 피하세요.

## Git
- `main` 브랜치입니다. 커밋은 **작업 단위가 끝나 검증까지 마친 시점에 Claude가 판단해서** 합니다(2026-09-18 사용자 위임). 푸시는 요청할 때만 합니다.
- 원격: `origin` = https://github.com/Lee37373/mediaproject (`main` 추적). 원격의 첫 커밋(Unity .gitignore 템플릿)은 병합해서 이어 붙였습니다.
- `Assets/StreamingAssets/LlamaLib-*`(약 4GB 네이티브 바이너리)와 `*.gguf`는 `.gitignore`로 제외했습니다. LLMUnity가 다시 내려받습니다.

## 테스트와 검증 (Unity MCP)
- 테스트: `run_tests`(assembly `GuildMaster.Tests.EditMode`). 현재 86개가 통과합니다.
- 컴파일: 먼저 `AssetDatabase.Refresh` + `CompilationPipeline.RequestScriptCompilation`을 eval로 실행하고, 그다음 `recompile_status`와 `console`(error)로 확인합니다.
  - 컴파일 뒤 도메인 리로드 중에는 `wait_for`가 네트워크 에러를 낼 수 있습니다. 정상입니다.
- `eval`에서는 `using`을 쓸 수 없습니다. 전체 네임스페이스를 쓰거나 `GuildMaster.Session.DebugCommands`를 쓰세요.
  - 제공 메서드: `Status()`, `PostHunt(id, count, gold, exp, party, x, y)`(위치 생략 시 알려진 둥지), `SetSpeed(i)`
  - 전투 검증: `SpawnSkirmish(speciesId, n)`(마을 밖 교전 생성과 길드원 투입), `SkirmishReport()`(겹침·카이팅·장판·목표 분산 `targets` 줄), `ClearSkirmish()`, `UnitsNearCamera(r)`
  - 영입 검증: `SpawnVisitor()`, `RecruitSay(말, 제안여부)`(첫 방문자와 대화), `RecruitStatus()`
  - 호위·게임 오버 검증: `PostEscort(시간당G, 시간당EXP, 시간, 인원)`, `ForceAccept(퀘스트ID, n)`, `MasterWalkOut(방향0~3, 칸)`, `MasterHome()`, `MasterStatus()`, `SpawnNearMaster(종, n, 거리)`
  - 잡담·호감도: `ForceBanter()`, `BanterStatus()`, `Relate(from, to, delta)`(명단 순번), `Relations()`
  - 영역: `Territories()`(둥지별 개체·반경·세력), `InvadeNest(순번, 종, 수)`(둥지 영역에 다른 종 투입)
  - 행상인: `MerchantVisit()`(지금 방문), `MerchantStatus()`(품목별 공급·수요·가격), `MerchantSell(품목, 수)`, `MerchantClose()`
  - 침공: `RaidNests()`(둥지별 개체 수·위험 단계), `ForceRaid(순번, 채우기)`, `RaidStatus()`(적·건물·길드원), `Hurt(순번, 피해)`
- MCP 도구가 없는 세션에서는 `unity` CLI(unity-cli 스킬)로 같은 일을 합니다: `unity status`, `unity command eval "<C#>"`, `recompile_status`, `console --level error`, `run_tests --mode editor`, `capture_game_view --source screen --save_path ...`
  - `capture_game_view`의 `save_path`는 `Assets/` 기준으로 저장됩니다. 확인한 뒤 지우세요.
- 플레이 중 코드를 바꾸면 반드시 `editor_stop` 후에 컴파일하세요.
  - **LLM이 생성 중일 때는 스크립트 파일도 건드리지 마세요.** 에디터가 포커스를 받아 자동 리로드하면 네이티브 스레드 때문에 "Reloading Domain"에서 멈춥니다(에디터 재시작 필요). 먼저 `editor_stop`한 뒤 파일을 고치세요.
  - 멈추기 전에 `LlmGateway.IsBusy`가 false가 될 때까지 기다리면 안전합니다(2026-09-19 생성 중에 멈추고 컴파일했다가 도메인 리로드가 한 번 멈췄습니다).
- **유닛 기즈모**(에디터 전용): 인스펙터로 고른 유닛(또는 하이어라키 선택)의 시야(노랑)·사거리(빨강)·목표 선·추격 포기한 대상(회색 점선)·몬스터 귀환 반경(파랑)·이름/등급/상태 라벨. **F4**로 모든 유닛과 둥지 영역 표시. Game 뷰는 상단 Gizmos 버튼이 켜져 있어야 보입니다.
- LLM 요청/응답은 플레이 중 **F2**(LLM 디버그 창, 에디터·개발 빌드 전용)로 볼 수 있습니다. 판정(채택/대체 이유), 대기·생성 시간, 전체 프롬프트가 나오고 복사할 수 있습니다.
- `Run In Background`는 켜져 있어야 합니다. 꺼져 있으면 에디터가 비활성일 때 게임이 멈춥니다.

## LLM (LLMUnity v3.0.3, 로컬)
- 씬의 `LLM` 오브젝트는 **Qwen3.5-4B Q4_K_M**(`C:\unity\LLMModels\qwen35-4b-q4km.gguf`)을 씁니다. 2026-09-19에 llama-3.2-1b(판단 약 3초, 같은 폴더)를 시험했지만 한국어·판단 품질이 낮아 되돌렸습니다. **Vulkan으로 Intel Arc 130V 내장 GPU**를 씁니다(`numGPULayers=99`, 2026-09-18). 판단 1회 생성 약 10초(CPU만 쓸 때는 약 20초).
- **Windows 사용자 경로에 한글이 있으면 llama.cpp가 실패합니다.** 사용자명이 "이재혁"이고 `%APPDATA%`에 한글이 들어갑니다.
  - 모델은 ASCII 경로 `C:\unity\LLMModels\qwen35-4b-q4km.gguf`로 복사해 LLMManager에 등록했습니다.
  - `LlamaEnvironment`가 `LLAMA_CACHE`를 ASCII 경로로 지정합니다.
  - 네이티브 DLL은 에디터에 로드된 채로 남기 때문에, **한 번 실패하면 에디터를 재시작해야** 에러 상태가 풀립니다.
- LLM을 쓸 수 없으면 HUD에 "AI: 규칙 기반"이 표시되고, 게임은 규칙 기반 판단으로 정상 동작해야 합니다.
