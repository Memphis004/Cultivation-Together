@echo off
setlocal

set WORKSPACE=%~dp0..
set GEN_CLIENT=%WORKSPACE%\Tools\Luban\Luban.dll
set CONF_ROOT=%~dp0

:: ใส่ "" ครอบ path ที่มีโอกาสมีช่องว่างค่ะ
dotnet "%GEN_CLIENT%" ^
    -t client ^
    -c cs-simple-json ^
    -d json ^
    --conf "%CONF_ROOT%luban.conf" ^
    -x outputCodeDir="%WORKSPACE%\UnityProject\Assets\Scripts\Data\Gen" ^
    -x outputDataDir="%WORKSPACE%\UnityProject\Assets\Resources\DataTables"

echo.
echo Done. Regenerated code into UnityProject/Assets/Scripts/Data/Gen
echo and data into UnityProject/Assets/Resources/DataTables.
pause