# Usage: powershell -File inspect_dll.ps1 <TypeName> [DllName]
# Examples:
#   powershell -File inspect_dll.ps1 ResearchManager Mafi.Core.dll
#   powershell -File inspect_dll.ps1 PanelWithHeader Mafi.Unity.dll
#   powershell -File inspect_dll.ps1 Display Mafi.Unity.dll
#   powershell -File inspect_dll.ps1 Queueue Mafi.dll
#
# If DllName is omitted, searches all four game DLLs: Mafi.dll, Mafi.Core.dll, Mafi.Base.dll, Mafi.Unity.dll
# Outputs: constructors, public properties, public fields, public methods (declared only), base class chain, interfaces

param(
    [Parameter(Mandatory=$true)][string]$TypeName,
    [string]$DllName
)

$basePath = "C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed"

if ($DllName) {
    $dllList = @($DllName)
} else {
    $dllList = @("Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll", "Mafi.Unity.dll")
}

$found = $false

foreach ($dll in $dllList) {
    $dllPath = Join-Path $basePath $dll
    if (-not (Test-Path $dllPath)) {
        Write-Host "DLL not found: $dllPath" -ForegroundColor Red
        continue
    }

    $types = @()
    try {
        $asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
        $types = $asm.GetTypes()
    } catch [System.Reflection.ReflectionTypeLoadException] {
        $types = $_.Exception.Types | Where-Object { $_ -ne $null }
    }

    $matches = $types | Where-Object { $_.Name -eq $TypeName }

    foreach ($t in $matches) {
        $found = $true
        Write-Host "`n============================================" -ForegroundColor Cyan
        Write-Host "  $($t.FullName)" -ForegroundColor Cyan
        Write-Host "  DLL: $dll | Public: $($t.IsPublic) | Abstract: $($t.IsAbstract) | Sealed: $($t.IsSealed)" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan

        # Base class chain
        Write-Host "`n--- Inheritance ---" -ForegroundColor Yellow
        $current = $t.BaseType
        $depth = 1
        while ($current -ne $null) {
            Write-Host ("  " * $depth + $current.FullName)
            $current = $current.BaseType
            $depth++
        }

        # Interfaces
        $ifaces = $t.GetInterfaces()
        if ($ifaces.Count -gt 0) {
            Write-Host "`n--- Interfaces ---" -ForegroundColor Yellow
            foreach ($iface in $ifaces) {
                Write-Host "  $($iface.FullName)"
            }
        }

        # Constructors
        $ctors = $t.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance)
        if ($ctors.Count -gt 0) {
            Write-Host "`n--- Constructors ---" -ForegroundColor Yellow
            foreach ($c in $ctors) {
                $p = ($c.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
                Write-Host "  ctor($p)"
            }
        }

        # Public properties (declared only)
        $props = $t.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::DeclaredOnly)
        if ($props.Count -gt 0) {
            Write-Host "`n--- Properties (declared) ---" -ForegroundColor Yellow
            foreach ($prop in $props) {
                $acc = @()
                if ($prop.CanRead) { $acc += "get" }
                if ($prop.CanWrite) { $acc += "set" }
                Write-Host "  $($prop.PropertyType.Name) $($prop.Name) { $($acc -join '; ') }"
            }
        }

        # Public fields (declared only)
        $fields = $t.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)
        if ($fields.Count -gt 0) {
            Write-Host "`n--- Fields (declared) ---" -ForegroundColor Yellow
            foreach ($f in $fields) {
                $mod = if ($f.IsStatic) { "static " } else { "" }
                Write-Host "  ${mod}$($f.FieldType.Name) $($f.Name)"
            }
        }

        # Public methods (declared only, excluding property accessors)
        $methods = $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly) |
            Where-Object { -not $_.IsSpecialName }
        if ($methods.Count -gt 0) {
            Write-Host "`n--- Methods (declared) ---" -ForegroundColor Yellow
            foreach ($m in $methods) {
                $mod = if ($m.IsStatic) { "static " } else { "" }
                $p = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
                Write-Host "  ${mod}$($m.ReturnType.Name) $($m.Name)($p)"
            }
        }
    }
}

if (-not $found) {
    Write-Host "No type named '$TypeName' found in: $($dllList -join ', ')" -ForegroundColor Red
    Write-Host "`nSearching for partial matches..." -ForegroundColor Yellow

    foreach ($dll in $dllList) {
        $dllPath = Join-Path $basePath $dll
        if (-not (Test-Path $dllPath)) { continue }

        $types = @()
        try {
            $asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
            $types = $asm.GetTypes()
        } catch [System.Reflection.ReflectionTypeLoadException] {
            $types = $_.Exception.Types | Where-Object { $_ -ne $null }
        }

        $partials = $types | Where-Object { $_.IsPublic -and $_.Name -like "*$TypeName*" } | Select-Object -First 10
        foreach ($t in $partials) {
            Write-Host "  $($t.FullName) ($dll)"
        }
    }
}
