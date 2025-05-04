### TODO:
1. UI: Video Add button "Play in DeoVR" (deeplink)
2. custom rest endpoint generate deovr .json files done
3. Don't hardcode url in settings
4. Better way than hardcoding an apikey in setting
    maybe shortlived keys?
5. improve .js somethimes no button!
### Notes:
On Windows need access to the file index.html
Only works with https:// and valid SSL cert

#### build:
dotnet build .\DeoVRDeeplink.sln /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
