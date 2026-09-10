# TokenNotch for Windows

작업표시줄 위를 돌아다니는 **Clawd(Claude Code 공식 픽셀 게)** 가 Claude Code와 Codex CLI의
남은 사용량과 리셋 시간을 알려주는 데스크톱 위젯입니다.

기본 배포에는 Clawd만 포함됩니다. 개인적으로 보유한 픽셀 애니메이션 팩이 로컬에 있으면
캐릭터 선택과 사용량 기반 진화 기능이 추가로 활성화되지만, 해당 그림 파일은 이 저장소에서
실행 가능한 원본 자산으로 배포하지 않습니다.

> macOS용 [TokenNotch](https://github.com/Borelchu/TokenNotch) (© Borel, MIT)를 Windows로 옮긴 포트입니다.
> 원본은 맥북 노치 양옆에 캐릭터를 붙이지만, Windows에는 노치가 없으므로 작업표시줄 위를 순찰하는 방식으로 바꿨습니다.

---

## 화면 미리보기

<p align="center">
  <img src="docs/screenshots/pikachu.png" alt="피카츄 캐릭터와 TokenNotch 사용량 패널" width="31%">
  <img src="docs/screenshots/squirtle.png" alt="꼬부기 캐릭터와 진화 진행도" width="31%">
  <img src="docs/screenshots/snorlax.png" alt="잠만보 캐릭터와 TokenNotch 사용량 패널" width="31%">
</p>

화면 예시는 로컬 픽셀 팩을 적용한 모습입니다. 실행에 필요한 원본 스프라이트와 애니메이션
팩은 저장소에 포함되지 않습니다.

## 요구 사항

| 항목 | 조건 |
|---|---|
| OS | Windows 10 / 11 |
| 런타임 | .NET 9 SDK 또는 .NET 9 Desktop Runtime |
| Claude 데이터 | Claude Code CLI 로그인 상태 |
| Codex 데이터 (선택) | Codex CLI 로그인 상태 |
| 로컬 캐릭터 (선택) | 직접 사용할 권리가 있는 로컬 픽셀 팩 |

## 빌드 & 실행

개발 중 실행:

```bash
dotnet run -c Release
```

배포용 단일 실행 파일 만들기 (.NET 런타임 없는 PC에서도 실행됨, 약 71MB):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

생성된 `dist/TokenNotchWin.exe` 하나만 있으면 어디서든 더블클릭으로 실행됩니다.
작업표시줄 버튼 없이 트레이 아이콘만 생깁니다.

그림이 없는 공개 저장소 상태를 검증할 때는 다음처럼 로컬 픽셀 포함을 명시적으로 끌 수 있습니다.

```bash
dotnet build -c Release -p:IncludeLocalPetAssets=false
```

이미 실행 중일 때 exe를 다시 눌러도 두 번째 캐릭터가 생기지 않습니다 — 대신 트레이로
숨겨둔 상태였다면 다시 화면에 나타납니다.

## 조작

| 동작 | 결과 |
|---|---|
| 마우스 올리기 | 확장 패널 (서비스별 HP바, 리셋 카운트다운) |
| 왼쪽 드래그 | 캐릭터를 집어 옮김 — 잡히면 팔다리를 휘적거림 |
| 놓기 | 작업표시줄로 낙하 + 착지 시 살짝 튕김 |
| 오른쪽 클릭 | 캐릭터·메인 AI 선택 / 위치 고정 / 숨기기 / 종료 |
| 확장 패널 카드 드래그 | Claude와 Codex 카드 순서 변경 |
| 트레이 아이콘 | 보이기·숨기기, 위치 고정, 종료 (더블클릭 = 다시 보이기) |

**위치 고정**을 켜면 순찰과 낙하를 멈추고 놓은 자리에 그대로 머뭅니다. 다중 모니터에서
특정 모니터에 파킹할 때 유용합니다. 고정 위치는 `%APPDATA%\TokenNotch\settings.json`에
저장되어 재시작해도 유지됩니다.

## 캐릭터 상태

남은 사용량에 따라 행동이 바뀝니다.

| 남은 양 | 행동 |
|---|---|
| 50% 초과 | 느긋한 옆걸음 순찰, 반짝임 |
| 20~50% | 땀 흘리며 종종걸음 |
| 20% 미만 | 부들부들 떨며 질주 |
| 오류 / 토큰 만료 | 잠들어서 zzz |

18초 동안 건드리지 않으면 순찰을 멈추고 **파라솔을 펴고 앉아 쉽니다.** 마우스를 올리면
파라솔이 스르륵 접히고 다리를 펴며 일어난 뒤 다시 순찰을 시작합니다(약 0.55초). 집어 올리면
그 전환을 건너뛰고 바로 일어납니다. 쉬기까지의 시간은 `MainWindow.xaml.cs`의 `IdleRestSeconds`,
일어나는 속도는 `StandUpSeconds`로 조정합니다.

앉은 채로 더 오래(1분 유예 후 약 20분에 걸쳐) 방치하면 피부색이 점점 까맣게 타들어가다가,
거의 다 타면 조그맣게 하트가 뜨며 만져달라고 신호를 보냅니다 — 손대주면(집거나 마우스를 올리면)
그 즉시 원래 색으로 돌아옵니다. 색이 완전한 검은색이 되진 않도록 캡을 걸어뒀습니다. 타이밍은
`NeglectGraceSeconds` / `NeglectSpanSeconds`로 조정합니다.

애니메이션만 미리 보려면 `TOKENNOTCH_MOOD` 환경변수를 쓰세요
(`happy` / `worried` / `critical` / `sleeping`).

```bash
TOKENNOTCH_MOOD=happy ./bin/Release/net9.0-windows/TokenNotchWin.exe
```

## 로컬 전용 픽셀 자산

`Resources/app.ico`를 제외한 `Resources` 파일은 `.gitignore`로 차단되어 GitHub에 올라가지
않습니다. 로컬 팩이 없거나 완전하지 않으면 해당 캐릭터 선택지는 자동으로 숨겨지고 Clawd로
안전하게 시작합니다.

앱에서 사용하는 팩은 다음 구조이며, 각 캐릭터 폴더에는 `atlas.png`와 `manifest.json`이
필요합니다.

```text
Resources/
  pixel-*-actions/
    <character>/
      atlas.png
      manifest.json
```

로컬 팩이 있는 상태에서 일반 빌드나 배포를 하면 그림이 결과 실행 파일에 포함됩니다. 코드만
공개하려면 저장소 소스만 올리고, 실행 파일을 배포할 때는 반드시
`-p:IncludeLocalPetAssets=false`로 빌드해야 합니다. 로컬 픽셀 자산과 그 라이선스는 이
프로젝트의 MIT 라이선스 적용 대상이 아닙니다.

## 사용량 데이터를 어디서 가져오나

이 위젯은 **로그인을 하지 않습니다.** 각 CLI가 로그인하면서 이미 로컬에 저장해둔 자격증명
파일을 **읽기 전용**으로 사용합니다.

| | 자격증명 위치 | 엔드포인트 |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\.credentials.json` | `api.anthropic.com/api/oauth/usage` |
| Codex CLI | `%USERPROFILE%\.codex\auth.json` | `chatgpt.com/backend-api/wham/usage` |

- **자격증명 파일에 직접 쓰지 않습니다.** Claude 액세스 토큰은 수명이 약 8시간이라 하루에도
  몇 번씩 만료되는데, 만료를 감지하면 위젯이 `claude mcp list`를 백그라운드로 한 번 실행해
  **CLI가 스스로 갱신하도록** 합니다(보통 3초). 사용자가 터미널을 열 필요가 없습니다.
  - 위젯이 직접 OAuth 리프레시를 호출하지 않는 이유: 리프레시 토큰은 회전식이라, 서버에서는
    갱신됐는데 파일 쓰기가 실패하면 유일한 토큰이 날아가 CLI 로그인 자체가 깨집니다.
    CLI에게 맡기면 위젯은 자격증명에 대해 계속 읽기 전용으로 남습니다.
  - `mcp list`를 쓰는 이유: 인증 경로를 타면서도 사용량을 전혀 소비하지 않는 가장 가벼운 명령입니다.
  - 자동 갱신이 실패하면 10분 쿨다운 후 재시도하며, 카드에 직접 `claude` 실행 안내가 뜹니다.
- Codex 토큰은 수명이 약 10일이라 자동 갱신 대상이 아닙니다. 만료되면 `codex`를 한 번 실행하세요
  (Codex의 리프레시 토큰은 일회용이라 더더욱 위젯이 건드리지 않습니다).
- 토큰을 위 두 서비스 외 어디로도 전송하지 않습니다.
- **5분 주기 폴링**: Claude usage 엔드포인트는 호출이 잦으면 한 번 걸리면 ~10분 지속되는
  sticky 429를 반환합니다. 그래서 5분 주기 + 429 시 15분 쿨다운을 씁니다.

> ⚠️ Windows에서 Claude Code CLI는 자격증명을 **평문 JSON**으로 저장합니다(macOS는 키체인 사용).
> 이는 CLI 자체의 동작이며 이 위젯이 만든 것이 아니지만, 파일 권한에만 의존해 보호된다는 점은
> 알아두시는 게 좋습니다.

## macOS 원본과 다른 점

| | macOS 원본 | Windows 포트 |
|---|---|---|
| 자격 증명 | 로그인 키체인 | 사용자 폴더의 평문 JSON |
| 배치 | 노치 양옆 고정 | 작업표시줄 순찰 + 드래그 이동 + 위치 고정 |
| 표시 기준 | Claude 사용량 중심 | Claude 또는 Codex를 메인 AI로 선택 |
| 휴식 | 없음 | 유휴 시 파라솔 펴고 앉아서 휴식 |

Clawd 스프라이트는 원본과 동일하게 Claude Code CLI에 내장된 쿼드런트 블록 아트
(`▛▜▙▟`)를 2×2 픽셀 격자로 디코딩해 재현하며, 색상도 CLI 테마값 rgb(215,119,87)을 그대로 씁니다.

## 프로젝트 구조

```
App.xaml(.cs)          앱 진입점, 트레이 아이콘
MainWindow.xaml(.cs)   창 배치, 드래그/순찰/휴식 상태 머신, 확장 패널
Controls/              Clawd 및 선택적 로컬 픽셀 캐릭터 렌더링
Services/              사용량 API 호출, 설정 저장, 무드/뷰모델
Tools/                 아이콘 및 로컬 픽셀 팩 생성 도구
Resources/             공개 저장소에는 app.ico만 포함
```

## 라이선스

소스 코드는 MIT이며 원본 TokenNotch(© Borel)의 저작권 표시를 유지합니다. [LICENSE](LICENSE)
참고. 로컬 캐릭터 자산과 화면 예시에 나타나는 캐릭터 그림은 MIT 적용 대상이 아니며, 각
권리자와 제공처의 조건을 별도로 따릅니다.
