@echo off
cd /d C:\Users\KentLefner\DungKeeper
git stash
git pull origin claude/analyze-uimanager-errors-RzxuD
git stash pop
echo.
echo Done. You can close this window and reopen Unity Hub.
pause
