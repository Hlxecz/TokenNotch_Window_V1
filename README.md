# TokenNotch for Windows

작업표시줄 위를 돌아다니는 **Clawd(Claude Code 공식 픽셀 게)** 가 Claude Code와 Codex CLI의
남은 사용량과 리셋 시간을 알려주는 데스크톱 위젯입니다.

> macOS용 [TokenNotch](https://github.com/Borelchu/TokenNotch) (© Borel, MIT)를 Windows로 옮긴 포트입니다.
> 원본은 맥북 노치 양옆에 캐릭터를 붙이지만, Windows에는 노치가 없으므로 작업표시줄 위를 순찰하는 방식으로 바꿨습니다.

---

## 요구 사항

| 항목 | 조건 |
|---|---|
| OS | Windows 10 / 11 |
| 런타임 | .NET 9 SDK 또는 .NET 9 Desktop Runtime |
| Claude 데이터 | Claude Code CLI 로그인 상태 |
| Codex 데이터 (선택) | Codex CLI 로그인 상태 |

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

이미 실행 중일 때 exe를 다시 눌러도 두 번째 캐릭터가 생기지 않습니다 — 대신 트레이로
숨겨둔 상태였다면 다시 화면에 나타납니다.

## 조작

| 동작 | 결과 |
|---|---|
| 마우스 올리기 | 확장 패널 (서비스별 HP바, 리셋 카운트다운) |
| 왼쪽 드래그 | 캐릭터를 집어 옮김 — 잡히면 팔다리를 휘적거림 |
| 놓기 | 작업표시줄로 낙하 + 착지 시 살짝 튕김 |
| 오른쪽 클릭 | 위치 고정 / 숨기기 / 종료 |
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

## 사용량 데이터를 어디서 가져오나

이 위젯은 **로그인을 하지 않습니다.** 각 CLI가 로그인하면서 이미 로컬에 저장해둔 자격증명
파일을 **읽기 전용**으로 사용합니다.

| | 자격증명 위치 | 엔드포인트 |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\.credentials.json` | `api.anthropic.com/api/oauth/usage` |
| Codex CLI | `%USERPROFILE%\.codex\auth.json` | `chatgpt.com/backend-api/wham/usage` |

- **토큰 갱신은 하지 않습니다.** 만료되면 안내만 표시하며, 해당 CLI(`claude` 또는 `codex`)를
  한 번 실행해 로그인하면 됩니다. 특히 Codex의 리프레시 토큰은 일회용이라, 위젯이 직접
  갱신하면 CLI 로그인이 깨질 수 있어 의도적으로 읽기 전용입니다.
- 자격증명 파일에 쓰기 작업을 하지 않으며, 토큰을 위 두 서비스 외 어디로도 전송하지 않습니다.
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
| Codex 캐릭터 | OpenAI CDN 공식 스프라이트시트 | 직접 그린 로봇 (WPF가 WebP를 디코딩하지 못함) |
| 휴식 | 없음 | 유휴 시 파라솔 펴고 앉아서 휴식 |

Clawd 스프라이트는 원본과 동일하게 Claude Code CLI에 내장된 쿼드런트 블록 아트
(`▛▜▙▟`)를 2×2 픽셀 격자로 디코딩해 재현하며, 색상도 CLI 테마값 rgb(215,119,87)을 그대로 씁니다.

## 프로젝트 구조

```
App.xaml(.cs)          앱 진입점, 트레이 아이콘
MainWindow.xaml(.cs)   창 배치, 드래그/순찰/휴식 상태 머신, 확장 패널
Controls/              캐릭터 렌더링 (Clawd, Codex 봇, 스프라이트 데이터)
Services/              사용량 API 호출, 설정 저장, 무드/뷰모델
Tools/                 IconGen — 트레이 아이콘(.ico) 굽는 1회성 도구
Resources/             app.ico
```

## 라이선스

MIT — 원본 TokenNotch(© Borel)의 저작권 표시를 유지합니다. [LICENSE](LICENSE) 참고.
