@echo off
rem ---------------------------------------------------------------------------
rem CrimsonX Linux - build wrapper.
rem
rem   build.cmd              verify and package (build + tests + all 3 arches)
rem   build.cmd -SkipPack    build + tests only, the usual loop while coding
rem   build.cmd -Quick       compile only
rem
rem It exists because PowerShell's default execution policy on a stock Windows
rem refuses to run a .ps1 that was downloaded or copied onto the machine, and
rem -ExecutionPolicy Bypass on this one call is a smaller change than editing
rem the machine's policy.
rem ---------------------------------------------------------------------------
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
