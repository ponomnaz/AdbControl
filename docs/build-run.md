# Build And Run

## Требования

- Windows
- `.NET 10 SDK`
- `adb.exe` доступен в `PATH`

Если `adb` не найден, приложение соберётся и запустится, но device-команды будут падать и это будет видно в `Журнале`.

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

WPF-приложение держит собранные DLL, поэтому перед пересборкой иногда нужно его закрыть:

```powershell
Get-Process AdbControl.App -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Run from Visual Studio

1. открыть `AdbControl.sln`
2. поставить `AdbControl.App` как `Startup Project`
3. нажать `F5` или `Ctrl+F5`

## Local runtime data

Приложение пишет runtime-данные сюда:

```text
%LOCALAPPDATA%\AdbControl
```

Полезные файлы:

- `%LOCALAPPDATA%\AdbControl\logs\command-trace.jsonl`
- `%LOCALAPPDATA%\AdbControl\logs\app-crash.log`
- `%LOCALAPPDATA%\AdbControl\settings\device-aliases.json`
- `%LOCALAPPDATA%\AdbControl\settings\apk-library.json`

## Частые проблемы

### `adb.exe не найден`

Причина:

- `adb` не установлен
- `platform-tools` не добавлены в `PATH`

Что делать:

- установить Android platform-tools
- открыть новый терминал
- проверить `adb version`

### `dotnet build` не может скопировать DLL

Причина:

- приложение уже запущено и держит файлы

Что делать:

```powershell
Get-Process AdbControl.App -ErrorAction SilentlyContinue | Stop-Process -Force
```

### Приложение упало на старте или в рантайме

Смотреть:

- `%LOCALAPPDATA%\AdbControl\logs\app-crash.log`
