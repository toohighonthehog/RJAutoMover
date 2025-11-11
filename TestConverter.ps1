# Test if the converter assembly loads correctly
$dllPath = "c:\Users\rjohnson\source\repos\RJAutoMover\RJAutoMoverConfig\bin\Debug\net10.0-windows\RJAutoMoverConfig.dll"

if (Test-Path $dllPath) {
    Write-Host "DLL exists at: $dllPath"
    try {
        Add-Type -Path $dllPath
        $converter = New-Object RJAutoMoverConfig.Converters.BooleanToVisibilityConverter
        Write-Host "Successfully created converter instance: $($converter.GetType().FullName)"
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)"
        Write-Host "Stack: $($_.Exception.StackTrace)"
    }
} else {
    Write-Host "DLL not found at: $dllPath"
}
