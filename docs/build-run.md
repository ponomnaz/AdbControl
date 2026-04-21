# Build And Run

## Debug build

```powershell
dotnet build .\AdbControl.sln -m:1
```

## Run from source

```powershell
dotnet run --project .\src\AdbControl.App\AdbControl.App.csproj
```

## Start built app directly

```powershell
.\src\AdbControl.App\bin\Debug\net10.0-windows\AdbControl.App.exe
```

## Clean build

```powershell
dotnet clean .\AdbControl.sln
dotnet build .\AdbControl.sln -m:1
```

## If build is blocked by a running app

```powershell
Get-Process AdbControl.App -ErrorAction SilentlyContinue | Stop-Process
```

## Local runtime data

The app writes runtime data here:

```text
%LOCALAPPDATA%\AdbControl
```

Useful files:

- `%LOCALAPPDATA%\AdbControl\logs\command-trace.jsonl`
- `%LOCALAPPDATA%\AdbControl\logs\app-crash.log`
