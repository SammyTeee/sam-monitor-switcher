@echo off
git --git-dir="%~dp0git-metadata" --work-tree="%~dp0." -c http.proxy= -c https.proxy= push -u origin main
