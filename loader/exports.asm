; MWCoop - chargeur : renvoi des 17 fonctions de la vraie version.dll (System32).
; g_real est rempli par proxy.cpp au chargement ; chaque export saute a la vraie fonction.
EXTERN g_real:QWORD

.code
FWD MACRO name, idx
name&_fwd PROC
    jmp qword ptr [g_real + idx*8]
name&_fwd ENDP
ENDM

FWD GetFileVersionInfoA, 0
FWD GetFileVersionInfoByHandle, 1
FWD GetFileVersionInfoExA, 2
FWD GetFileVersionInfoExW, 3
FWD GetFileVersionInfoSizeA, 4
FWD GetFileVersionInfoSizeExA, 5
FWD GetFileVersionInfoSizeExW, 6
FWD GetFileVersionInfoSizeW, 7
FWD GetFileVersionInfoW, 8
FWD VerFindFileA, 9
FWD VerFindFileW, 10
FWD VerInstallFileA, 11
FWD VerInstallFileW, 12
FWD VerLanguageNameA, 13
FWD VerLanguageNameW, 14
FWD VerQueryValueA, 15
FWD VerQueryValueW, 16

END
