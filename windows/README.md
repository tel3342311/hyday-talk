# Hyday Talk for Windows

Hyday Talk 的 Windows 版，以 C# / .NET 8 與 WPF 撰寫，功能與 macOS 版相同

在任何 App 的輸入框按一下**右 Ctrl** 開始說話，再按一下完成，辨識並整理後的文字會貼回原本的游標位置

## 快捷鍵

Windows 鍵盤不會把 fn 鍵傳給應用程式，因此改用右 Ctrl 作為觸發鍵

| 操作 | macOS | Windows |
| --- | --- | --- |
| 開始或完成聽寫 | fn | 右 Ctrl |
| 開始翻譯 | fn ＋ 左 Shift | 右 Ctrl ＋ 左 Shift |
| 文字提問 | fn ＋ Control | 右 Ctrl ＋ 左 Alt |
| 記到 Hyday | fn ＋ Space | 右 Ctrl ＋ Space |
| 取消錄音或處理 | esc | Esc |

## 功能

| 功能 | Windows 版 |
| --- | --- |
| 聽寫、翻譯、文字提問（含選取文字） | ✅ |
| 記到 Hyday（Life Log 標記、待辦、回顧段落）、問筆記、Hyday 詞彙 | ✅ 已安裝 Hyday 時使用 Hyday 內建的搜尋 |
| 語音辨識：Windows 語音辨識、本機模型（whisper.cpp、transcribe.cpp）、API | ✅ 本機模型在 App 內下載並驗證 SHA-256 |
| 文字整理：基礎整理、本機 Ollama、Claude Code、API | ✅ |
| 聽寫風格、繁體中文字形與全形標點、個人辭典 | ✅ |
| 自動學習詞彙（讀回比對） | ✅ 按 Enter、離開輸入框、切換 App、再次聽寫或 2 分鐘後比對 |
| 畫面上下文 | ✅ |
| 插入文字：剪貼簿＋Ctrl+V，0.8 秒後還原剪貼簿；拒絕在密碼欄位錄音 | ✅ |
| 失敗的錄音保留並可重新處理、錄音保留期限 | ✅ |
| 使用統計（今日字數、節省時間、口述速度、連續天數、常用 App） | ✅ |
| 檢查更新、下載後重新啟動並更新 | ✅ |
| API Key 存放於 Windows 認證管理員 | ✅ |
| 系統匣常駐、登入時啟動 | ✅ |

## 系統需求

- Windows 10 2004（19041）以上，x64
- 麥克風：設定 > 隱私權與安全性 > 麥克風，開啟「讓桌面應用程式存取您的麥克風」
- Windows 語音辨識（選用）：開啟線上語音辨識並安裝辨識語言
- 本機文字 AI（選用）：[Ollama](https://ollama.com/download)
- Claude 整理（選用）：已安裝並登入的 [Claude Code](https://docs.claude.com/en/docs/claude-code/overview)

## 安裝

1. 下載 `Hyday-Talk-<版本>-win-x64.zip` 並解壓縮到自己的資料夾，例如 `%LOCALAPPDATA%\Programs\Hyday Talk`
2. 執行 `HydayTalk.exe`，App 會常駐在系統匣
3. 第一次開啟時會顯示設定視窗，在語音辨識頁下載本機模型或設定 API

App 放在使用者可寫入的資料夾時，有新版可以直接重新啟動並更新；放在 Program Files 時，更新檔會下載到「下載」資料夾，需要手動解壓縮取代

## 資料儲存

- 設定、聽寫紀錄（最多 200 筆）、使用統計與錄音存放於 `%APPDATA%\HydayTalk\`，設定檔欄位與 macOS 版相同
- 本機模型存放於 `%APPDATA%\HydayTalk\Models\`
- Hyday 資料夾預設讀取 Hyday 設定檔 `%APPDATA%\Hyday\settings.json` 的 `vault.root`
- 疑難排解：設定環境變數 `HYDAYTALK_LOG=1` 後啟動，事件記錄會寫到 `%APPDATA%\HydayTalk\debug.log`，不包含聽寫內容

## 專案結構

```
windows/
├── src/TalkCore/        跨平台核心（net8.0）：文字整理、標點、辭典、提示詞、API／Ollama／Claude、辨識參數、
│                        快捷鍵狀態、Hyday 日誌與筆記搜尋、讀回比對、畫面上下文、使用統計、錄音保留、更新
├── src/HydayTalk/       WPF App：鍵盤掛鉤、錄音（NAudio）、Windows 語音辨識、貼上插入、輸入框讀取、
│                        浮動面板、設定視窗、系統匣、更新
├── tests/TalkCore.Tests xUnit 測試，移植自 macOS 版的 TalkCoreTests
├── scripts/             建置辨識引擎、打包、產生圖示
└── ci/windows.yml       GitHub Actions 工作流程，複製到 .github/workflows/ 即可啟用
```

`TalkCore` 是 macOS 版 `Sources/TalkCore` 的 C# 移植，提示詞、字面值檢查、標點與日誌格式規則保持一致

| macOS | Windows |
| --- | --- |
| fn 鍵（CGEventTap） | 右 Ctrl（`WH_KEYBOARD_LL` 低階鍵盤掛鉤） |
| 輔助使用 AX API | Edit／RichEdit 控制項用 `WM_GETTEXT`、`EM_GETSEL`，其他 App 用 UI Automation |
| Apple Speech | `Windows.Media.SpeechRecognition` |
| ICU Hans-Hant | 內建 OpenCC 繁簡對照表（`src/TalkCore/Resources/s2t.txt`），各平台結果一致 |
| 鑰匙圈 | 認證管理員（`CredWrite`／`CredRead`） |
| AVAudioEngine＋afconvert | NAudio，直接錄成 16 kHz 單聲道 WAV |
| DMG 更新 | zip 更新，App 結束後由 PowerShell 取代檔案並重新開啟 |

WPF 的 XAML 以內嵌資源在執行時載入（`Xaml.cs`），專案不需要 WPF 標記編譯器，因此 Linux 與 macOS 上的 .NET 8 SDK 也能建置與發佈 Windows 版

## 開發

```bash
cd windows
dotnet test tests/TalkCore.Tests
dotnet build src/HydayTalk
```

在 Windows 上執行：

```powershell
dotnet run --project src/HydayTalk -- --show
```

### 辨識引擎

`runtime/` 收錄已建置好的 whisper-cli.exe 與 transcribe-cli.exe（來源、版本與授權見 `runtime/README.md`），建置時會複製到 `HydayTalk.exe` 旁的 `runtime\`，`dotnet run` 也能直接使用本機模型。引擎為每種 CPU 指令集各附一個 `ggml-cpu-*.dll`，執行時自動選用最快的版本，不需要安裝 Visual C++ 可轉散發套件

更新引擎時，在 Linux 以 mingw-w64 交叉編譯（需要 `cmake` 與 `g++-mingw-w64-x86-64-posix`）：

```bash
./scripts/build-runtime-mingw.sh
```

或在 Windows 以 Visual Studio C++ 建置工具編譯：

```powershell
./scripts/build-runtime.ps1
```

### 打包

輸出自帶 .NET 執行環境與辨識引擎的 `windows/dist/Hyday-Talk-<版本>-win-x64.zip`：

```bash
./scripts/package.sh
```

```powershell
./scripts/package.ps1
```

### 驗證

核心邏輯由 `tests/TalkCore.Tests` 驗證；打包好的 zip 曾在 Linux 的 Wine 9 中以實際流程測試，麥克風由 PulseAudio 虛擬音源提供：

- 右 Ctrl 聽寫並貼進記事本、翻譯、提問、Esc 取消、記到 Hyday 寫入日誌
- 以內建的 whisper-cli.exe 與 whisper.cpp 官方 tiny 模型，辨識真人錄音並插入文字（11 秒錄音約 1.7 秒辨識完成）
- 簡體辨識結果轉為繁體與全形標點後插入
- 辨識失敗後保留錄音並從面板重新處理
- 修改插入文字後按 Enter，自動學習詞彙

Wine 沒有實作 UI Automation 與 Windows 語音辨識，這兩項只確認了無法使用時會正常退回（仍可貼上、顯示錯誤），實際效果需要在 Windows 上確認

## 第三方授權

繁簡轉換資料取自 [OpenCC](https://github.com/BYVoid/OpenCC)，以 Apache License 2.0 授權，授權條款見 `src/TalkCore/Resources/OpenCC-LICENSE.txt`
