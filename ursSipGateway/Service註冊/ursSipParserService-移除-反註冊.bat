@ECHO OFF
net session >nul 2>&1
IF NOT %ERRORLEVEL% EQU 0 (
   ECHO �ХH Administrator �v������.
   PAUSE
   EXIT /B 1
)

ECHO ���b���� ursSipParser...
ECHO ---------------------------------------------------
net stop ursSipParser
ECHO ---------------------------------------------------

ECHO ���b�R�� ursSipParser...
ECHO ---------------------------------------------------
sc delete ursSipParser
ECHO ---------------------------------------------------

ECHO ����
PAUSE