@echo off
chcp 65001 >nul
echo ============================================
echo   ELEGIDO MANAGER - Crear app de escritorio
echo ============================================
echo.
cd /d "%~dp0"
dotnet publish "ElegidoManager.Escritorio\ElegidoManager.Escritorio.csproj" -c Release -o "AppEscritorio"
if errorlevel 1 (
  echo.
  echo Hubo un error al crear el ejecutable.
  pause
  exit /b 1
)
echo.
echo Listo: AppEscritorio\ElegidoManager.exe
explorer "AppEscritorio"
pause
