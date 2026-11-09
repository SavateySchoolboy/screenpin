@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo .NET Framework compiler not found. & exit /b 1)
if not exist dist mkdir dist
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:dist\ScreenPin.exe /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:lib\zxing.dll ScreenPin.cs QrFeatures.cs
if errorlevel 1 exit /b 1
copy /Y lib\zxing.dll dist\zxing.dll >nul
copy /Y lib\ZXING-LICENSE.txt dist\ZXING-LICENSE.txt >nul
echo Built dist\ScreenPin.exe
