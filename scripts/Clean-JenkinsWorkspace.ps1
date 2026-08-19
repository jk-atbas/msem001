$dir = "C:\jenkins\workspace"
Get-ChildItem -Path $dir -Directory | Remove-Item -Recurse -Force