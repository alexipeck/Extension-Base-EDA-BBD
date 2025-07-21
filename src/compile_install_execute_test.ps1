if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Host "This script requires administrative privileges. Please run PowerShell as Administrator."
    exit
}

$ProjectDir = "C:\Users\Me\projects\Extension-Base-EDA-BBD"
$LandisExtensionsDir = "C:\Program Files\LANDIS-II-v8\extensions"

cd $ProjectDir\src\
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet build failed. Exiting script."
    exit $LASTEXITCODE
}

Copy-Item -Path "$ProjectDir\src\bin\Debug\netstandard2.0\Landis.Extension.EDA-v3-BBD.dll" -Destination "$LandisExtensionsDir\" -Force
Copy-Item -Path "$ProjectDir\src\bin\Debug\netstandard2.0\Landis.Extension.EDA-v3-BBD.pdb" -Destination "$LandisExtensionsDir\" -Force

cd $ProjectDir\tests\Core8.0-EDA3.0\
try {
    Write-Output "Executing LANDIS-II..."
    landis-ii-8.cmd .\scenario_CA_coast.txt 2>&1 | Tee-Object -FilePath console-output.txt
    Write-Output "LANDIS-II execution completed."
    cd $ProjectDir\src\
} finally {
    cd $ProjectDir\src\
}