# TokenNotch for Windows

Claude Code와 Codex CLI의 남은 사용량을 작업표시줄 위 캐릭터로 보여주는 Windows 위젯입니다.

macOS용 [TokenNotch](https://github.com/Borelchu/TokenNotch)를 Windows 방식으로 옮긴
MIT 라이선스 포트입니다.

## 화면 미리보기

<p align="center">
  <img src="docs/screenshots/pikachu.png" alt="피카츄 캐릭터와 TokenNotch 패널" width="31%">
  <img src="docs/screenshots/squirtle.png" alt="꼬부기 캐릭터와 진화 진행도" width="31%">
  <img src="docs/screenshots/snorlax.png" alt="잠만보 캐릭터와 TokenNotch 패널" width="31%">
</p>

> 화면 예시는 개인 로컬 픽셀 팩을 적용한 모습입니다. 원본 스프라이트와 애니메이션 팩은
> 저장소에 포함되지 않습니다.

## 주요 기능

- Claude Code와 Codex 사용량, 리셋 시간 동시 표시
- 메인 AI 선택 및 서비스 카드 순서 변경
- 작업표시줄 순찰, 드래그 이동, 위치 고정, 자동 휴식
- 사용량에 따라 달라지는 캐릭터 표정과 움직임
- 선택적 로컬 픽셀 캐릭터 및 누적 사용량 기반 진화
- 트레이 숨기기와 중복 실행 방지

## 실행

**필요 환경:** Windows 10/11, .NET 9, 로그인된 Claude Code CLI

```bash
dotnet run -c Release
```

단일 실행 파일 만들기:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
```

생성된 `dist/TokenNotchWin.exe`를 실행하면 됩니다. Codex 데이터는 Codex CLI에 로그인되어
있을 때 함께 표시됩니다.

## 조작

| 동작 | 결과 |
|---|---|
| 마우스 올리기 | 사용량 패널 열기 |
| 왼쪽 드래그 | 캐릭터 이동 |
| 오른쪽 클릭 | 캐릭터·메인 AI 선택, 위치 고정, 숨기기, 종료 |
| 패널 카드 드래그 | Claude와 Codex 카드 순서 변경 |
| 트레이 아이콘 | 보이기·숨기기 및 종료 |

설정과 고정 위치는 `%APPDATA%\TokenNotch\settings.json`에 저장됩니다.

## 로컬 픽셀 팩

공개 저장소에는 `Resources/app.ico`만 들어갑니다. 나머지 `Resources` 파일은 Git에서
자동 제외되며, 픽셀 팩이 없으면 Clawd만 표시됩니다.

```text
Resources/
  pixel-*-actions/
    <character>/
      atlas.png
      manifest.json
```

픽셀을 제외한 공개용 실행 파일은 다음 옵션으로 만듭니다.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeLocalPetAssets=false -o dist
```

## 데이터와 보안

TokenNotch는 별도로 로그인하지 않고 각 CLI가 저장한 자격증명을 읽기 전용으로 사용합니다.

| 서비스 | 읽는 파일 | 요청 대상 |
|---|---|---|
| Claude Code | `%USERPROFILE%\.claude\.credentials.json` | `api.anthropic.com` |
| Codex | `%USERPROFILE%\.codex\auth.json` | `chatgpt.com` |

- 자격증명 파일을 수정하거나 다른 서버로 전송하지 않습니다.
- 사용량은 5분마다 갱신합니다.
- Claude 토큰이 만료되면 Claude CLI를 통해 갱신을 시도합니다.

## 라이선스

소스 코드는 [MIT License](LICENSE)이며 원본 TokenNotch의 저작권 표시를 유지합니다.
로컬 캐릭터 자산과 화면 예시의 캐릭터 그림은 MIT 적용 대상이 아닙니다.
