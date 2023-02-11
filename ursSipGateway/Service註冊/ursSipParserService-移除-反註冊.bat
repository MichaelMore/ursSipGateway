@ECHO OFF
net session >nul 2>&1
IF NOT %ERRORLEVEL% EQU 0 (
   ECHO 請以 Administrator 權限執行.
   PAUSE
   EXIT /B 1
)

ECHO 正在停止 ursSipParser...
ECHO ---------------------------------------------------
net stop ursSipParser
ECHO ---------------------------------------------------

ECHO 正在刪除 ursSipParser...
ECHO ---------------------------------------------------
sc delete ursSipParser
ECHO ---------------------------------------------------

ECHO 完成
PAUSE