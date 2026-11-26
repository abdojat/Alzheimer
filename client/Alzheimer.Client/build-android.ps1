param(
    [string]$UnityExe
)

$ErrorActionPreference = "Stop"
$project = Split-Path -Parent $MyInvocation.MyCommand.Path
$repository = Resolve-Path (Join-Path $project "..\..")
$output = Join-Path $repository "builds\android\Alzheimer.apk"

if (-not $UnityExe) {
    $candidates = @(
        "${env:ProgramFiles}\Unity\Hub\Editor",
        "${env:ProgramFiles(x86)}\Unity\Hub\Editor",
        "${env:LOCALAPPDATA}\UnityHub\Editor",
        "D:\Unity\Hub\Editor"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $match = Get-ChildItem $candidate -Filter Unity.exe -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match "6000\." } |
                Select-Object -First 1
            if ($match) { $UnityExe = $match.FullName }
        }
    }
}

if (-not $UnityExe -or -not (Test-Path $UnityExe)) {
    throw "Unity 6000.6.10f1 Editor not found. Pass -UnityExe 'C:\...\Unity.exe'."
}

New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
$log = Join-Path $project "build-android.log"

$unityArguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $project,
    "-executeMethod", "Alzheimer.Client.Editor.BuildAndroid.Build",
    "-output", $output,
    "-logFile", $log
)
$unityProcess = Start-Process `
    -FilePath $UnityExe `
    -ArgumentList $unityArguments `
    -PassThru `
    -Wait `
    -WindowStyle Hidden

if ($unityProcess.ExitCode -ne 0) {
    throw "Unity build failed. See $log"
}
if (-not (Test-Path $output)) {
    throw "Unity completed without creating $output"
}

Write-Host "APK created: $output"
