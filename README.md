# 🕵️ StealthRAT: C# .NET 10 Remote Administration Platform

**StealthRAT** is a Windows Remote Administration Tool developed in **C#** leveraging the latest **.NET 10.0** runtime. It was designed to demonstrate advanced systems programming, Win32 API / NT Native API interoperability (`P/Invoke`), multi-threaded asynchronous socket communication, evasion techniques, and persistent background operation.

> ⚠️ **Educational & Ethical Disclaimer**: This project is created strictly for academic research, cybersecurity education, and authorized security assessments. Unauthorized access to computer systems is illegal under applicable cybercrime laws.

---

## 🛡️ Six Layers of Protection & Stealth

### Layer 1: Window & Console Invisibility
- **WinExe Subsystem**: Configured with `<OutputType>WinExe</OutputType>` in `.csproj` to prevent console window creation upon execution.
- **Console Detachment**: Dynamically calls `FreeConsole()` and `ShowWindow(SW_HIDE)` to detach from any calling terminal process.

### Layer 2: Persistence & Self-Healing
- **Registry Run Keys**: Automatically registers startup entries under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
- **User Startup Folder**: Copies executable binaries to `shell:startup`.
- **Scheduled Tasks**: Registers periodic tasks via `schtasks` to verify process presence.
- **Guardian Process Architecture**: Spawns a secondary watchdog process to monitor and restart the main agent process if terminated.

### Layer 3: Anti-Termination & Tamper Resistance
- **Critical Process Flag**: Invokes `RtlSetProcessIsCritical` to declare the process critical, causing a Blue Screen of Death (BSOD) if forcibly terminated via Task Manager.
- **Process Termination Hooks**: Leverages `NtSetInformationProcess` (`BreakOnTermination`).
- **Administrative Tool Neutralization**: Periodically checks for and closes process inspection utilities (e.g., ProcessHacker, Process Explorer, Task Manager).

### Layer 4: Anti-Analysis & Detection Evasion
- **Debugger Detection**: Checks for active debuggers using `IsDebuggerPresent()`.
- **AV Exclusion Automation**: Configures Windows Defender path exclusions via PowerShell.
- **Process Camouflage**: Adopts names mimicking legitimate Windows services (e.g., `WindowsSecurityHealth`).

### Layer 5: Network Evasion
- **Firewall Rule Insertion**: Configures outbound/inbound rules via `netsh advfirewall` to prevent connection blocking.
- **Packet Sniffer Termination**: Detects and suppresses network monitoring tools (e.g., Wireshark, TCPView, GlassWire).

### Layer 6: Advanced Screen Capture (DRM / DirectX Bypass)
- **Multi-Method Screen Capture Pipeline**:
  - `BitBlt` with `CAPTUREBLT` flag to capture layered, semi-transparent, and overlaid windows.
  - `PrintWindow` with `RENDERFULLCONTENT` flag to bypass DirectX and DRM black screen protections.
  - Standard GDI fallback capture.
- **Microphone Streaming**: Employs `NAudio` for real-time audio sampling and socket streaming.

---

## 🏗️ Repository Architecture

```text
StealthRAT/
├── Program.cs                   # Application entry point, stealth init & service orchestration
├── RemoteUIManager.cs           # Optional WinForms administrative viewer
├── StealthRAT.csproj            # .NET 10.0 project configuration and NAudio reference
│
├── Interfaces/
│   ├── ILoggerService.cs        # Logging abstraction
│   └── ICommandHandler.cs       # Command pattern interface
│
├── Models/
│   └── ServerConfiguration.cs   # Centralized port and network constants
│
├── Services/
│   ├── CommandService.cs        # TCP command dispatcher (Command Pattern)
│   ├── ScreenCaptureService.cs  # Multi-strategy screen capture engine
│   ├── AudioCaptureService.cs   # Microphone capture via NAudio
│   ├── PersistenceService.cs    # Registry, startup, and task persistence
│   ├── AntiDetectionService.cs  # Process protection and anti-debugging
│   └── NetworkEvasionService.cs # Firewall rule management and sniffer blocking
│
├── Handlers/                    # Command execution handlers
│   ├── LaunchProcessHandler.cs  # Process spawning
│   ├── SystemPowerHandler.cs    # Shutdown, reboot, and sleep controls
│   ├── FileAccessHandler.cs     # Remote file management
│   └── InputControlHandlers.cs  # Mouse and keyboard input simulation
│
└── Utilities/
    └── NativeInputHelper.cs     # Win32 P/Invoke declarations for SendInput
```

---

## 🚀 Building & Running

### Prerequisites
- Windows 10 or Windows 11.
- **[.NET SDK 10.0+](https://dotnet.microsoft.com/download)** installed.

### Step 1: Clone and Navigate
```powershell
cd StealthRAT
```

### Step 2: Restore Dependencies & Build
```powershell
dotnet restore
dotnet build -c Release
```

### Step 3: Publish a Single-File Executable
To produce a completely self-contained binary that does not require .NET to be installed on the destination machine:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
The resulting executable will be generated at:
`bin/Release/net10.0-windows/win-x64/publish/StealthRAT.exe`

### Step 4: Execution
- Run the executable as **Administrator** to enable full persistence, firewall rule creation, and process protection.
- Default listening ports:
  - **9090**: Command and control dispatcher.
  - **9091**: Screen capture streaming and audio transmission.

---

## ⚠️ Legal & Ethical Notice
This software is provided for cybersecurity research, defensive validation, and educational demonstrations only. Misuse of this software is subject to criminal prosecution.
