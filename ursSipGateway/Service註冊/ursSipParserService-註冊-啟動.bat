@ECHO OFF
net session >nul 2>&1
IF NOT %ERRORLEVEL% EQU 0 (
   ECHO ���~: �ХH Administrator �v������.
   PAUSE
   EXIT /B 1
)

set batdir=%~dp0

ECHO �}�l�w�� ursSipParser �A��...
ECHO ---------------------------------------------------
sc create "ursSipParser" binPath= "%batdir%..\ursSipParser.exe" DisplayName= "ursSipParser" Start=delayed-auto
ECHO ---------------------------------------------------

ECHO ���b�]�w ursSipParser �A�ȦW��...
ECHO ---------------------------------------------------
sc description ursSipParser "ursSipParser"
ECHO ---------------------------------------------------

ECHO ���b�Ұ� ursSipParser �A��...
ECHO ---------------------------------------------------
net start "ursSipParser"
ECHO ---------------------------------------------------

ECHO ����
PAUSE