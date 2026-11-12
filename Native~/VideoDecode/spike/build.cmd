@echo off
cd /d %~dp0
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /nologo /std:c++17 /O2 /EHsc /W3 /MD vspike.cpp /Fe:vspike.exe
