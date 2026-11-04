# Hyday Talk for Windows

Hyday Talk 的 Windows 版，以 C# / .NET 8 與 WPF 撰寫，功能與 macOS 版對齊中，目前為 MVP

在任何 App 的輸入框按一下**右 Ctrl** 開始說話，再按一下完成，辨識並整理後的文字會貼回原本的游標位置

## 快捷鍵

Windows 鍵盤不會把 fn 鍵傳給應用程式，因此改用右 Ctrl 作為觸發鍵

| 操作 | macOS | Windows |
| --- | --- | --- |
| 開始或完成聽寫 | fn | 右 Ctrl |
| 開始翻譯 | fn ＋ 左 Shift | 右 Ctrl ＋ 左 Shift |
| 文字提問 | fn ＋ Control | 右 Ctrl ＋ 左 Alt |
| 取消錄音或處理 | esc | Esc |

## 目前支援

| 功能 | 狀態 |
| --- | --- |
| 聽寫、翻譯、文字提問（含選取文字） | ✅ |
| 本機辨識：whisper.cpp、transcribe.cpp（Qwen3-ASR、TEA-ASR），App 內下載並驗證模型 | ✅ |
| API 辨識（OpenAI 相容 `POST /audio/transcriptions`） | ✅ |
| 文字整理：基礎整理、本機 Ollama、Claude Code、API | ✅ |
| 聽寫風格、繁體中文字形與全形標點、個人辭典 | ✅ |
| 插入文字：剪貼簿＋Ctrl+V，0.8 秒後還原剪貼簿；拒絕在密碼欄位錄音 | ✅ |
| API Key 存放於 Windows 認證管理員 | ✅ |
| 聽寫紀錄（最多 200 筆）、系統匣常駐、登入時啟動 | ✅ |
| 記到 Hyday、問筆記、Hyday 詞彙 | 尚未提供 |
| 自動學習詞彙（讀回比對）、畫面上下文 | 尚未提供 |
| 洞察統計、失敗錄音重新處理、自動更新 | 尚未提供 |
| Windows 系統語音辨識 | 尚未提供 |

## 系統需求

- Windows 10 2004（19041）以上，x64
- 本機文字 AI（選用）：[Ollama](https://ollama.com/download)
- Claude 整理（選用）：已安裝並登入的 [Claude Code](https://docs.claude.com/en/docs/claude-code/overview)

## 專案結構

```
windows/
├── src/TalkCore/        跨平台核心（net8.0）：文字整理、標點、辭典、API 與 Ollama、Claude、辨識參數、快捷鍵狀態
├── src/HydayTalk/       WPF App：鍵盤掛鉤、錄音（NAudio）、貼上插入、UI Automation、浮動面板、設定視窗、系統匣
├── tests/TalkCore.Tests xUnit 測試，移植自 macOS 版的 TalkCoreTests
└── scripts/             建置辨識引擎與打包
```

`TalkCore` 是 macOS 版 `Sources/TalkCore` 的 C# 移植，提示詞、字面值檢查與標點規則保持一致；設定檔 JSON 欄位名稱也與 macOS 版相同

| macOS | Windows |
| --- | --- |
| fn 鍵（CGEventTap） | 右 Ctrl（`WH_KEYBOARD_LL` 低階鍵盤掛鉤） |
| 輔助使用 AX API | UI Automation |
| ICU Hans-Hant | `LCMapStringEx`（`LCMAP_TRADITIONAL_CHINESE`） |
| 鑰匙圈 | 認證管理員（`CredWrite` / `CredRead`） |
| AVAudioEngine＋afconvert | NAudio，直接錄成 16 kHz 單聲道 WAV |
| `~/Library/Application Support/HydayTalk/` | `%APPDATA%\HydayTalk\` |

## 開發

```powershell
cd windows
dotnet test tests/TalkCore.Tests
dotnet run --project src/HydayTalk
```

核心與測試可在 Linux 或 macOS 執行；WPF App 只能在 Windows 建置與執行

建置本機辨識引擎（需要 Git、CMake 與 Visual Studio C++ 建置工具），會從與 macOS 版相同的固定版本建置 whisper.cpp 與 transcribe.cpp：

```powershell
./scripts/build-runtime.ps1
```

打包成自帶 .NET 執行環境的 zip，輸出到 `windows/dist/`：

```powershell
./scripts/package.ps1
```

開發時也可以在語音辨識頁自行選擇任何 `whisper-cli.exe`

`windows/ci/windows.yml` 是 GitHub Actions 工作流程，會在 `windows-latest` 執行測試、建置辨識引擎並上傳打包好的 zip；複製到 `.github/workflows/` 即可啟用
