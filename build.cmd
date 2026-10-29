@echo off
setlocal
rem Сборка каркаса штатным компилятором .NET Framework 4 (без SDK и без NuGet).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Не найден csc.exe для .NET Framework 4. Поставьте .NET Framework 4.8 Developer Pack,
  echo либо соберите _draft-managed\KsuConfigurator.csproj через .NET SDK.
  exit /b 1
)
if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /win32manifest:app.manifest /out:bin\KsuConfigurator.exe src\NativeHid.cs src\HidDevice.cs src\KsuProtocol.cs src\FormatString.cs src\KsuIni.cs src\MainForm.cs src\Program.cs
if errorlevel 1 exit /b 1

if exist ..\KCY_UM.ini copy /y ..\KCY_UM.ini bin\ >nul
if not exist bin\KCY_UM.ini if exist KCY_UM.ini copy /y KCY_UM.ini bin\ >nul
echo Готово: bin\KsuConfigurator.exe
echo   GUI:  bin\KsuConfigurator.exe
echo   тест: bin\KsuConfigurator.exe --selftest   (отчёт в bin\selftest.log)
