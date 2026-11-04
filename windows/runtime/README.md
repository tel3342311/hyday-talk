# 本機辨識引擎

Hyday Talk 在本機辨識語音時呼叫的程式；建置時由 `src/HydayTalk/HydayTalk.csproj` 複製到 `HydayTalk.exe` 旁的 `runtime\` 資料夾，`dotnet build`、`dotnet run` 與 `dotnet publish` 都會包含

| 檔案 | 來源 | 用途 |
| --- | --- | --- |
| `whisper-cli.exe` 與同資料夾的 DLL | [whisper.cpp](https://github.com/ggml-org/whisper.cpp) v1.9.2（`306c88f4`），MIT | Whisper `.bin` 模型 |
| `transcribe\transcribe-cli.exe` 與同資料夾的 DLL | [transcribe.cpp](https://github.com/handy-computer/transcribe.cpp)（`f1dcd158`），MIT | Qwen3-ASR、TEA-ASR `.gguf` 模型 |

兩者與 macOS 版建置的是同一個固定版本；transcribe.cpp 自帶不同版本的 ggml，因此放在獨立的資料夾

- `ggml-cpu-*.dll`：每種 CPU 指令集一個版本，執行時自動載入這台電腦能用的最快版本，新 CPU 速度快，舊 CPU 也能執行
- `libstdc++-6.dll`、`libgcc_s_seh-1.dll`、`libwinpthread-1.dll`：mingw-w64 的 C++ 執行環境，不需要另外安裝 Visual C++ 可轉散發套件；授權見 `LICENSE-gcc-runtime.txt`（GCC Runtime Library Exception）與 `LICENSE-mingw-w64-winpthreads.txt`

重新建置：在 Linux 執行 `scripts/build-runtime-mingw.sh`（需要 `cmake` 與 `g++-mingw-w64-x86-64-posix`），或在 Windows 以 Visual Studio C++ 建置工具執行 `scripts/build-runtime.ps1`；兩者都會覆寫這個資料夾中的執行檔與 DLL
