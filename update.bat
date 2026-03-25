@echo off
timeout /t 2 /nobreak > nul
copy /y offline_template.htm app_offline.htm > nul
timeout /t 3 /nobreak > nul
tar -xf release.zip
del app_offline.htm
del offline_template.htm
del release.zip
del update.bat