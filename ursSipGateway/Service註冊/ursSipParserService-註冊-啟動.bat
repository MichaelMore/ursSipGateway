@ECHO OFF
net session >nul 2>&1
IF NOT %ERRORLEVEL% EQU 0 (
   ECHO 錯誤: 請以 Administrator 權限執行.
   PAUSE
   EXIT /B 1
)

set batdir=%~dp0

ECHO 開始安裝 ursSipParser 服務...
ECHO ---------------------------------------------------
sc create "ursSipParser" binPath= "%batdir%..\ursSipParser.exe" DisplayName= "ursSipParser" Start=delayed-auto
ECHO ---------------------------------------------------

ECHO 正在設定 ursSipParser 服務名稱...
ECHO ---------------------------------------------------
sc description ursSipParser "ursSipParser"
ECHO ---------------------------------------------------

ECHO 正在啟動 ursSipParser 服務...
ECHO ---------------------------------------------------
net start "ursSipParser"
ECHO ---------------------------------------------------

ECHO 結束
PAUSE