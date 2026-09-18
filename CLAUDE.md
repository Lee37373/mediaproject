# 길드 마스터 타이쿤 (Guild Master Tycoon)

Unity 6 (6000.0.60f1) 2D URP 게임입니다. LLM 기반 길드 경영 시뮬레이션이고, 방치형이라 "보는 재미"가 중요합니다.

- 설계: `Assets/GuildMaster/Docs/DESIGN.md`. 원본 기획서와 달라진 결정 사항이 표로 정리돼 있습니다.
- 진행 상황과 다음 할 일: `Assets/GuildMaster/Docs/PROGRESS.md`. **작업을 시작하기 전에 먼저 읽고, 끝나면 갱신하세요.**

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
- **아트 에셋은 없습니다.** 모든 스프라이트는 `Visuals/PixelArt.cs`에서 코드로 생성합니다. 교체할 때는 이 파일만 고치면 됩니다.
- **UI는 uGUI를 코드로 조립**합니다(`UIBuilder`). 한글은 OS 폰트(맑은 고딕)를 씁니다. 레이아웃은 LayoutGroup 기반입니다.
  - 주의: `childForceExpand`가 켜진 LayoutGroup은 스스로 flexible이 됩니다. 필요하면 `Size(..., flexWidth: 0)`로 막으세요.
- **JSON은 `JsonUtility`와 snake_case DTO**를 씁니다. 게임 코드에서 Newtonsoft는 쓰지 않습니다(LLMUnity 의존성으로 설치돼 있을 뿐입니다).

## 코딩 규칙
- 코드 주석은 영어, 사용자에게 보이는 문자열은 한국어입니다.
- 파일 하나에 기능 단위 하나를 둡니다. 새 시스템은 `GameBootstrap`에 등록합니다.
- 반복 생성하는 오브젝트는 `PoolService`에 키를 등록해 씁니다(`IPoolable`).
- `.NET Standard 2.1`에는 없는 API가 있습니다(예: `FirstOrDefault(pred, default)`).
- 파일 이름이 `*System.cs`인 스크립트가 컴파일에서 빠지는 현상이 한 번 있었습니다(원인 불명). 피하세요.

## Git
- `main` 브랜치입니다. 커밋은 사용자가 요청할 때만 합니다.
- `Assets/StreamingAssets/LlamaLib-*`(약 4GB 네이티브 바이너리)와 `*.gguf`는 `.gitignore`로 제외했습니다. LLMUnity가 다시 내려받습니다.

## 테스트와 검증 (Unity MCP)
- 테스트: `run_tests`(assembly `GuildMaster.Tests.EditMode`). 현재 13개가 통과합니다.
- 컴파일: 먼저 `AssetDatabase.Refresh` + `CompilationPipeline.RequestScriptCompilation`을 eval로 실행하고, 그다음 `recompile_status`와 `console`(error)로 확인합니다.
  - 컴파일 뒤 도메인 리로드 중에는 `wait_for`가 네트워크 에러를 낼 수 있습니다. 정상입니다.
- `eval`에서는 `using`을 쓸 수 없습니다. 전체 네임스페이스를 쓰거나 `GuildMaster.Session.DebugCommands`를 쓰세요.
  - 제공 메서드: `Status()`, `PostHunt(id, count, gold, exp, party)`, `SetSpeed(i)`
- 플레이 중 코드를 바꾸면 반드시 `editor_stop` 후에 컴파일하세요.
- `Run In Background`는 켜져 있어야 합니다. 꺼져 있으면 에디터가 비활성일 때 게임이 멈춥니다.

## LLM (LLMUnity v3.0.3, 로컬)
- 씬의 `LLM` 오브젝트는 사용자가 다운로드한 **Qwen3.5-4B Q4_K_M** 모델을 씁니다. CPU만 쓰고 `numGPULayers=0`입니다.
- **Windows 사용자 경로에 한글이 있으면 llama.cpp가 실패합니다.** 사용자명이 "이재혁"이고 `%APPDATA%`에 한글이 들어갑니다.
  - 모델은 ASCII 경로 `C:\unity\LLMModels\qwen35-4b-q4km.gguf`로 복사해 LLMManager에 등록했습니다.
  - `LlamaEnvironment`가 `LLAMA_CACHE`를 ASCII 경로로 지정합니다.
  - 네이티브 DLL은 에디터에 로드된 채로 남기 때문에, **한 번 실패하면 에디터를 재시작해야** 에러 상태가 풀립니다.
- LLM을 쓸 수 없으면 HUD에 "AI: 규칙 기반"이 표시되고, 게임은 규칙 기반 판단으로 정상 동작해야 합니다.
