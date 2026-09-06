param(
    [string]$BannerlordRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string[]]$ModuleName = @(),
    [string]$OutputRoot = (Join-Path (Resolve-Path $PSScriptRoot).Path ("..\artifacts\native-widget-coverage\" + (Get-Date -Format "yyyyMMdd-HHmmss")))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$metadataElementNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($metadataName in @("Constants", "Parameters", "Variables", "VisualDefinitions")) {
    [void]$metadataElementNames.Add($metadataName)
}

$prefabIndex = @{}
$prefabStateCache = @{}
$widgetStats = @{}
$renderKindsByWidgetName = @{}
$assemblyPathsByName = @{}

function New-CloneElement {
    param([System.Xml.Linq.XElement]$Element)

    return [System.Xml.Linq.XElement]::Parse($Element.ToString([System.Xml.Linq.SaveOptions]::DisableFormatting))
}

function Get-ModulePrefabsPaths {
    param(
        [string]$InstallRoot,
        [string[]]$TargetModuleNames
    )

    $modulesRoot = Join-Path $InstallRoot "Modules"
    if (-not (Test-Path -LiteralPath $modulesRoot)) {
        throw "Could not find modules root '$modulesRoot'."
    }

    $requestedModuleNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($targetModuleName in $TargetModuleNames) {
        if (-not [string]::IsNullOrWhiteSpace($targetModuleName)) {
            [void]$requestedModuleNames.Add($targetModuleName)
        }
    }

    $resolvedPaths = New-Object System.Collections.Generic.List[string]
    $resolvedModuleNames = New-Object System.Collections.Generic.List[string]
    foreach ($moduleDirectory in Get-ChildItem -LiteralPath $modulesRoot -Directory | Sort-Object Name) {
        if ($requestedModuleNames.Count -gt 0 -and -not $requestedModuleNames.Contains($moduleDirectory.Name)) {
            continue
        }

        $modulePrefabsPath = Join-Path $moduleDirectory.FullName "GUI\Prefabs"
        if (-not (Test-Path -LiteralPath $modulePrefabsPath)) {
            continue
        }

        $resolvedPaths.Add([System.IO.Path]::GetFullPath($modulePrefabsPath))
        $resolvedModuleNames.Add($moduleDirectory.Name)
    }

    if ($requestedModuleNames.Count -gt 0) {
        $missingModuleNames = @($requestedModuleNames | Where-Object { $_ -notin $resolvedModuleNames })
        if ($missingModuleNames.Count -gt 0) {
            throw "Could not find GUI prefab roots for module(s): $($missingModuleNames -join ", ")."
        }
    }

    if ($resolvedPaths.Count -eq 0) {
        throw "No module GUI prefab roots were found under '$modulesRoot'."
    }

    return [pscustomobject]@{
        ModuleNames = @($resolvedModuleNames)
        PrefabsPaths = @($resolvedPaths)
    }
}

function Get-ModuleRoot {
    param([string]$Path)

    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    $parts = $normalizedPath.Split(
        [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
        [System.StringSplitOptions]::RemoveEmptyEntries)
    $modulesIndex = [Array]::FindIndex(
        $parts,
        [System.Predicate[string]]{
            param($part)
            return [string]::Equals($part, "Modules", [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($modulesIndex -lt 0 -or $modulesIndex + 1 -ge $parts.Length) {
        return $null
    }

    return [System.IO.Path]::Combine($parts[0..($modulesIndex + 1)])
}

function Get-RelativeDirectoryDistance {
    param(
        [string]$SourceDirectory,
        [string]$TargetDirectory
    )

    try {
        $relative = [System.IO.Path]::GetRelativePath($SourceDirectory, $TargetDirectory)
        if ([string]::IsNullOrWhiteSpace($relative)) {
            return 0
        }

        return ($relative.Split(
            [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
            [System.StringSplitOptions]::RemoveEmptyEntries)).Length
    }
    catch {
        return [int]::MaxValue
    }
}

function Ensure-PrefabIndex {
    param([string]$InstallRoot)

    if ($prefabIndex.Count -gt 0) {
        return
    }

    $modulesPath = Join-Path $InstallRoot "Modules"
    if (-not (Test-Path -LiteralPath $modulesPath)) {
        throw "Could not find modules path '$modulesPath'."
    }

    foreach ($file in [System.IO.Directory]::EnumerateFiles($modulesPath, "*.xml", [System.IO.SearchOption]::AllDirectories)) {
        $normalizedPath = [System.IO.Path]::GetFullPath($file)
        $prefabSegment = [System.IO.Path]::DirectorySeparatorChar + "GUI" + [System.IO.Path]::DirectorySeparatorChar + "Prefabs" + [System.IO.Path]::DirectorySeparatorChar
        if ($normalizedPath.IndexOf($prefabSegment, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        $referenceName = [System.IO.Path]::GetFileNameWithoutExtension($normalizedPath)
        if ([string]::IsNullOrWhiteSpace($referenceName)) {
            continue
        }

        if (-not $prefabIndex.ContainsKey($referenceName)) {
            $prefabIndex[$referenceName] = New-Object System.Collections.Generic.List[object]
        }

        $prefabIndex[$referenceName].Add([pscustomobject]@{
                FullPath       = $normalizedPath
                DirectoryPath  = [System.IO.Path]::GetDirectoryName($normalizedPath)
                ModuleRootPath = Get-ModuleRoot -Path $normalizedPath
            })
    }
}

function Resolve-PrefabReferencePath {
    param(
        [string]$SourcePrefabPath,
        [string]$ReferenceName
    )

    if ([string]::IsNullOrWhiteSpace($SourcePrefabPath) -or
        [string]::IsNullOrWhiteSpace($ReferenceName) -or
        -not $prefabIndex.ContainsKey($ReferenceName)) {
        return $null
    }

    $candidates = $prefabIndex[$ReferenceName]
    if ($candidates.Count -eq 0) {
        return $null
    }

    $sourceDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($SourcePrefabPath))
    $sameDirectoryMatches = @($candidates | Where-Object {
            [string]::Equals($_.DirectoryPath, $sourceDirectory, [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($sameDirectoryMatches.Count -eq 1) {
        return $sameDirectoryMatches[0].FullPath
    }

    $sourceModuleRoot = Get-ModuleRoot -Path $SourcePrefabPath
    if (-not [string]::IsNullOrWhiteSpace($sourceModuleRoot)) {
        $sameModuleMatches = @($candidates |
                Where-Object {
                    [string]::Equals($_.ModuleRootPath, $sourceModuleRoot, [System.StringComparison]::OrdinalIgnoreCase)
                } |
                Sort-Object @{
                    Expression = { Get-RelativeDirectoryDistance -SourceDirectory $sourceDirectory -TargetDirectory $_.DirectoryPath }
                }, @{
                    Expression = { $_.FullPath }
                })

        if ($sameModuleMatches.Count -ge 1) {
            return $sameModuleMatches[0].FullPath
        }
    }

    if ($candidates.Count -eq 1) {
        return $candidates[0].FullPath
    }

    return ($candidates |
        Sort-Object @{
            Expression = { Get-RelativeDirectoryDistance -SourceDirectory $sourceDirectory -TargetDirectory $_.DirectoryPath }
        }, @{
            Expression = { $_.FullPath }
        } |
        Select-Object -First 1).FullPath
}

function Get-PrefabState {
    param(
        [string]$Path,
        [bool]$IncludeRootOnly
    )

    $cacheKey = ([System.IO.Path]::GetFullPath($Path)) + "|" + $IncludeRootOnly
    if ($prefabStateCache.ContainsKey($cacheKey)) {
        return $prefabStateCache[$cacheKey]
    }

    $document = [System.Xml.Linq.XDocument]::Load($Path)
    $root = $document.Root
    if ($null -eq $root) {
        throw "Prefab '$Path' is missing a root element."
    }

    $rootWindow = $root.Element([System.Xml.Linq.XName]::Get("Window"))
    if ($null -eq $rootWindow) {
        $rootWindow = $root
    }

    if ($IncludeRootOnly) {
        if (-not [string]::Equals($rootWindow.Name.LocalName, "Window", [System.StringComparison]::OrdinalIgnoreCase)) {
            $rootNodeElement = New-CloneElement -Element $rootWindow
        }
        else {
            $visualElements = @($rootWindow.Elements() | Where-Object {
                    -not $metadataElementNames.Contains($_.Name.LocalName)
                })
            if ($visualElements.Count -eq 0) {
                throw "The prefab '$Path' does not contain a visual root inside <Window>."
            }

            if ($visualElements.Count -gt 1) {
                throw "The prefab '$Path' contains multiple visual roots inside <Window>."
            }

            $rootNodeElement = New-CloneElement -Element $visualElements[0]
        }
    }
    else {
        $rootNodeElement = New-CloneElement -Element $rootWindow
    }

    $state = [pscustomobject]@{
        SourcePath       = [System.IO.Path]::GetFullPath($Path)
        RootNodeElement  = $rootNodeElement
        ExpansionStack   = @([System.IO.Path]::GetFullPath($Path))
    }
    $prefabStateCache[$cacheKey] = $state
    return $state
}

function Get-VisualChildElements {
    param([System.Xml.Linq.XElement]$Element)

    if ([string]::Equals($Element.Name.LocalName, "Window", [System.StringComparison]::OrdinalIgnoreCase)) {
        return @($Element.Elements() | Where-Object {
                -not $metadataElementNames.Contains($_.Name.LocalName)
            })
    }

    $childrenElement = $Element.Element([System.Xml.Linq.XName]::Get("Children"))
    if ($null -eq $childrenElement) {
        return @()
    }

    return @($childrenElement.Elements())
}

function Get-ParsedChildElements {
    param([System.Xml.Linq.XElement]$Element)

    if ([string]::Equals($Element.Name.LocalName, "Window", [System.StringComparison]::OrdinalIgnoreCase)) {
        return Get-VisualChildElements -Element $Element
    }

    $childrenElement = $Element.Element([System.Xml.Linq.XName]::Get("Children"))
    if ($null -eq $childrenElement) {
        return @()
    }

    return @($childrenElement.Elements())
}

function Has-DirectLogicalChildrenLocation {
    param([System.Xml.Linq.XElement]$Element)

    foreach ($child in $Element.Elements()) {
        if ([string]::Equals($child.Name.LocalName, "LogicalChildrenLocation", [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Find-LogicalChildrenTarget {
    param([System.Xml.Linq.XElement]$Root)

    foreach ($child in Get-VisualChildElements -Element $Root) {
        if (Has-DirectLogicalChildrenLocation -Element $child) {
            return $child
        }
    }

    foreach ($child in Get-VisualChildElements -Element $Root) {
        $nestedTarget = Find-LogicalChildrenTarget -Root $child
        if ($null -ne $nestedTarget) {
            return $nestedTarget
        }
    }

    return $null
}

function Insert-LogicalChildrenIntoDefaultLocation {
    param(
        [System.Xml.Linq.XElement]$TargetElement,
        [System.Collections.Generic.List[System.Xml.Linq.XElement]]$LogicalChildren
    )

    if ($LogicalChildren.Count -eq 0) {
        return
    }

    if ([string]::Equals($TargetElement.Name.LocalName, "Window", [System.StringComparison]::OrdinalIgnoreCase)) {
        foreach ($logicalChild in $LogicalChildren) {
            $TargetElement.Add((New-CloneElement -Element $logicalChild))
        }

        return
    }

    $childrenElement = $TargetElement.Element([System.Xml.Linq.XName]::Get("Children"))
    if ($null -eq $childrenElement) {
        $childrenElement = [System.Xml.Linq.XElement]::new([System.Xml.Linq.XName]::Get("Children"))
        $TargetElement.Add($childrenElement)
    }

    foreach ($logicalChild in $LogicalChildren) {
        $childrenElement.Add((New-CloneElement -Element $logicalChild))
    }
}

function Try-InsertLogicalChildrenAtLogicalTarget {
    param(
        [System.Xml.Linq.XElement]$ExpandedRoot,
        [System.Collections.Generic.List[System.Xml.Linq.XElement]]$LogicalChildren
    )

    $logicalTarget = Find-LogicalChildrenTarget -Root $ExpandedRoot
    if ($null -eq $logicalTarget) {
        return $false
    }

    @($logicalTarget.Elements() | Where-Object {
            [string]::Equals($_.Name.LocalName, "LogicalChildrenLocation", [System.StringComparison]::OrdinalIgnoreCase)
        }) | ForEach-Object {
        $_.Remove()
    }

    Insert-LogicalChildrenIntoDefaultLocation -TargetElement $logicalTarget -LogicalChildren $LogicalChildren
    return $true
}

function Expand-CallerChildren {
    param(
        [System.Xml.Linq.XElement]$CallerElement,
        [string[]]$ExpansionStack
    )

    $callerChildren = [System.Collections.Generic.List[System.Xml.Linq.XElement]]::new()
    $childrenElement = $CallerElement.Element([System.Xml.Linq.XName]::Get("Children"))
    if ($null -eq $childrenElement) {
        Write-Output -NoEnumerate $callerChildren
        return
    }

    foreach ($child in $childrenElement.Elements()) {
        $callerChildren.Add((Expand-Element -Element $child -ExpansionStack $ExpansionStack))
    }

    Write-Output -NoEnumerate $callerChildren
}

function Expand-Element {
    param(
        [System.Xml.Linq.XElement]$Element,
        [string[]]$ExpansionStack
    )

    $sourcePath = $ExpansionStack[-1]
    $resolvedPath = Resolve-PrefabReferencePath -SourcePrefabPath $sourcePath -ReferenceName $Element.Name.LocalName
    if (-not [string]::IsNullOrWhiteSpace($resolvedPath)) {
        if ($ExpansionStack -contains $resolvedPath) {
            return New-CloneElement -Element $Element
        }

        $includeState = Get-PrefabState -Path $resolvedPath -IncludeRootOnly $true
        $nextStack = @($ExpansionStack + $resolvedPath)
        $expandedRoot = Expand-Element -Element $includeState.RootNodeElement -ExpansionStack $nextStack
        $callerChildren = Expand-CallerChildren -CallerElement $Element -ExpansionStack $ExpansionStack

        if ($callerChildren.Count -gt 0) {
            $consumed = Try-InsertLogicalChildrenAtLogicalTarget -ExpandedRoot $expandedRoot -LogicalChildren $callerChildren
            if (-not $consumed) {
                Insert-LogicalChildrenIntoDefaultLocation -TargetElement $expandedRoot -LogicalChildren $callerChildren
            }
        }

        return $expandedRoot
    }

    $clone = [System.Xml.Linq.XElement]::new($Element.Name)
    foreach ($attribute in $Element.Attributes()) {
        $clone.SetAttributeValue($attribute.Name, $attribute.Value)
    }

    foreach ($childElement in $Element.Elements()) {
        if ([string]::Equals($childElement.Name.LocalName, "LogicalChildrenLocation", [System.StringComparison]::OrdinalIgnoreCase)) {
            $clone.Add((New-CloneElement -Element $childElement))
            continue
        }

        $clone.Add((Expand-Element -Element $childElement -ExpansionStack $ExpansionStack))
    }

    return $clone
}

function Ensure-WidgetReflectionSnapshot {
    param([string]$InstallRoot)

    if ($renderKindsByWidgetName.Count -gt 0) {
        return
    }

    foreach ($assemblyDirectory in @(
            (Join-Path $InstallRoot "bin\Win64_Shipping_Client")
        ) + @(Get-ChildItem -LiteralPath (Join-Path $InstallRoot "Modules") -Directory | ForEach-Object {
                Join-Path $_.FullName "bin\Win64_Shipping_Client"
            })) {
        if (-not (Test-Path -LiteralPath $assemblyDirectory)) {
            continue
        }

        foreach ($assemblyPath in [System.IO.Directory]::EnumerateFiles($assemblyDirectory, "*.dll", [System.IO.SearchOption]::TopDirectoryOnly)) {
            try {
                $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Name
                if (-not [string]::IsNullOrWhiteSpace($assemblyName) -and -not $assemblyPathsByName.ContainsKey($assemblyName)) {
                    $assemblyPathsByName[$assemblyName] = $assemblyPath
                }
            }
            catch {
            }
        }
    }

    $resolveHandler = [System.ResolveEventHandler]{
        param($sender, $args)

        $requestedName = ([System.Reflection.AssemblyName]$args.Name).Name
        if ([string]::IsNullOrWhiteSpace($requestedName) -or -not $script:assemblyPathsByName.ContainsKey($requestedName)) {
            return $null
        }

        try {
            return [System.Reflection.Assembly]::LoadFrom($script:assemblyPathsByName[$requestedName])
        }
        catch {
            return $null
        }
    }

    [System.AppDomain]::CurrentDomain.add_AssemblyResolve($resolveHandler)
    try {
        $loadedAssemblies = New-Object System.Collections.Generic.List[System.Reflection.Assembly]
        foreach ($assemblyPath in $assemblyPathsByName.Values) {
            try {
                $loadedAssemblies.Add([System.Reflection.Assembly]::LoadFrom($assemblyPath))
            }
            catch {
            }
        }

        $widgetBaseType = $null
        foreach ($assembly in $loadedAssemblies) {
            $candidateType = $assembly.GetType("TaleWorlds.GauntletUI.BaseTypes.Widget", $false, $false)
            if ($null -ne $candidateType) {
                $widgetBaseType = $candidateType
                break
            }
        }

        if ($null -eq $widgetBaseType) {
            throw "Could not locate TaleWorlds.GauntletUI.BaseTypes.Widget in the Bannerlord assemblies."
        }

        foreach ($assembly in $loadedAssemblies) {
            $types = @()
            try {
                $types = @($assembly.GetTypes())
            }
            catch [System.Reflection.ReflectionTypeLoadException] {
                $types = @($_.Exception.Types | Where-Object { $null -ne $_ })
            }
            catch {
                $types = @()
            }

            foreach ($type in $types) {
                if (-not $type.IsClass -or $type.IsAbstract -or -not $widgetBaseType.IsAssignableFrom($type)) {
                    continue
                }

                if ($renderKindsByWidgetName.ContainsKey($type.Name)) {
                    continue
                }

                $current = $type
                $renderKind = "Unknown"
                while ($null -ne $current) {
                    switch ($current.FullName) {
                        "TaleWorlds.GauntletUI.BaseTypes.ScrollbarWidget" {
                            $renderKind = "ScrollbarWidget"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.ScrollablePanel" {
                            $renderKind = "ScrollablePanel"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.GridWidget" {
                            $renderKind = "GridWidget"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.ListPanel" {
                            $renderKind = "ListPanel"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.NavigationScopeTargeter" {
                            $renderKind = "NavigationScopeTargeter"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.ButtonWidget" {
                            $renderKind = "ButtonWidget"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.BrushWidget" {
                            $renderKind = "BrushWidget"
                            break
                        }
                        "TaleWorlds.GauntletUI.BaseTypes.Widget" {
                            $renderKind = "Widget"
                            break
                        }
                    }

                    if ($renderKind -ne "Unknown") {
                        break
                    }

                    $current = $current.BaseType
                }

                if ($renderKind -ne "Unknown") {
                    $renderKindsByWidgetName[$type.Name] = $renderKind
                }
            }
        }
    }
    finally {
        [System.AppDomain]::CurrentDomain.remove_AssemblyResolve($resolveHandler)
    }
}

function Resolve-WidgetSupport {
    param([string]$WidgetName)

    switch -Regex ($WidgetName) {
        "^Window$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "Window"; NodeType = "WindowNode" }
        }
        "^RichTextWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "RichTextWidget"; NodeType = "RichTextWidgetNode" }
        }
        "^TextWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "TextWidget"; NodeType = "TextWidgetNode" }
        }
        "^ButtonWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ButtonWidget"; NodeType = "ButtonWidgetNode" }
        }
        "^Standard\.PopupCloseButton$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ButtonWidget"; NodeType = "ButtonWidgetNode" }
        }
        "^EscapeMenuButtonWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "EscapeMenuButtonWidget"; NodeType = "EscapeMenuButtonWidgetNode" }
        }
        "^TabControl$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "TabControl"; NodeType = "TabControlNode" }
        }
        "^ListPanel$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ListPanel"; NodeType = "ListPanelNode" }
        }
        "^NavigatableListPanel$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ListPanel"; NodeType = "NavigatableListPanelNode" }
        }
        "^ScrollablePanelFixedHeaderWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ScrollablePanelFixedHeaderWidget"; NodeType = "ScrollablePanelFixedHeaderWidgetNode" }
        }
        "^NavigationScopeTargeter$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "NavigationScopeTargeter"; NodeType = "NavigationScopeTargeterNode" }
        }
        "^ItemTemplate$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "ItemTemplate"; NodeType = "ItemTemplateNode" }
        }
        "^HintWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "HintWidget"; NodeType = "HintWidgetNode" }
        }
        "^Widget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "Widget"; NodeType = "WidgetNode" }
        }
        "^BrushWidget$" {
            return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltIn"; RenderKind = "BrushWidget"; NodeType = "BrushWidgetNode" }
        }
    }

    if ($WidgetName.EndsWith("ScrollablePanel", [System.StringComparison]::OrdinalIgnoreCase) -or
        $WidgetName.EndsWith("ScrollablePanelWidget", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltInPattern"; RenderKind = "ScrollablePanel"; NodeType = "ScrollablePanelNode" }
    }

    if ([string]::Equals($WidgetName, "ScrollablePanel", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltInPattern"; RenderKind = "ScrollablePanel"; NodeType = "ScrollablePanelNode" }
    }

    if ([string]::Equals($WidgetName, "ScrollbarWidget", [System.StringComparison]::OrdinalIgnoreCase) -or
        $WidgetName.EndsWith("ScrollbarWidget", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltInPattern"; RenderKind = "ScrollbarWidget"; NodeType = "ScrollbarWidgetNode" }
    }

    if ($WidgetName.EndsWith("NavigatableGridWidget", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltInPattern"; RenderKind = "GridWidget"; NodeType = "NavigatableGridWidgetNode" }
    }

    if ([string]::Equals($WidgetName, "GridWidget", [System.StringComparison]::OrdinalIgnoreCase) -or
        $WidgetName.EndsWith("GridWidget", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Supported = $true; MappingSource = "BuiltInPattern"; RenderKind = "GridWidget"; NodeType = "GridWidgetNode" }
    }

    if ($renderKindsByWidgetName.ContainsKey($WidgetName)) {
        $renderKind = $renderKindsByWidgetName[$WidgetName]
        $nodeType = switch ($renderKind) {
            "Widget" { "WidgetNode" }
            "BrushWidget" { "BrushWidgetNode" }
            "ButtonWidget" { "ButtonWidgetNode" }
            "ListPanel" { "ListPanelNode" }
            "NavigationScopeTargeter" { "NavigationScopeTargeterNode" }
            "GridWidget" { "GridWidgetNode" }
            "ScrollablePanel" { "ScrollablePanelNode" }
            "ScrollbarWidget" { "ScrollbarWidgetNode" }
            default { "FallbackNode" }
        }

        if ($nodeType -ne "FallbackNode") {
            return [pscustomobject]@{
                Supported     = $true
                MappingSource = "Reflection"
                RenderKind    = $renderKind
                NodeType      = $nodeType
            }
        }
    }

    return [pscustomobject]@{
        Supported     = $false
        MappingSource = "Fallback"
        RenderKind    = "Unknown"
        NodeType      = "FallbackNode"
    }
}

function Add-WidgetOccurrence {
    param(
        [string]$WidgetName,
        [string]$PrefabPath
    )

    if (-not $widgetStats.ContainsKey($WidgetName)) {
        $widgetStats[$WidgetName] = [ordered]@{
            WidgetName = $WidgetName
            Occurrences = 0
            Prefabs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            SamplePrefabs = New-Object System.Collections.Generic.List[string]
        }
    }

    $entry = $widgetStats[$WidgetName]
    $entry.Occurrences++
    if ($entry.Prefabs.Add($PrefabPath) -and $entry.SamplePrefabs.Count -lt 5) {
        $entry.SamplePrefabs.Add($PrefabPath)
    }
}

function Get-PropertySum {
    param(
        [object[]]$Items,
        [string]$PropertyName
    )

    $measure = @($Items | Measure-Object -Property $PropertyName -Sum)
    if ($measure.Count -eq 0 -or $null -eq $measure[0] -or $null -eq $measure[0].Sum) {
        return 0
    }

    return [int]$measure[0].Sum
}

function Collect-NodeStats {
    param(
        [System.Xml.Linq.XElement]$Element,
        [string]$PrefabPath
    )

    Add-WidgetOccurrence -WidgetName $Element.Name.LocalName -PrefabPath $PrefabPath

    foreach ($child in Get-ParsedChildElements -Element $Element) {
        Collect-NodeStats -Element $child -PrefabPath $PrefabPath
    }
}

$resolvedBannerlordRoot = [System.IO.Path]::GetFullPath($BannerlordRoot)
$moduleSelection = Get-ModulePrefabsPaths -InstallRoot $resolvedBannerlordRoot -TargetModuleNames $ModuleName
$resolvedModuleNames = @($moduleSelection.ModuleNames)
$resolvedModulePrefabsPaths = @($moduleSelection.PrefabsPaths)
$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
Ensure-PrefabIndex -InstallRoot $resolvedBannerlordRoot
Ensure-WidgetReflectionSnapshot -InstallRoot $resolvedBannerlordRoot

$prefabPaths = @(
    foreach ($modulePrefabsPath in $resolvedModulePrefabsPaths) {
        [System.IO.Directory]::EnumerateFiles($modulePrefabsPath, "*.xml", [System.IO.SearchOption]::AllDirectories) |
            ForEach-Object { [System.IO.Path]::GetFullPath($_) }
    }
) | Sort-Object

foreach ($prefabPath in $prefabPaths) {
    $state = Get-PrefabState -Path $prefabPath -IncludeRootOnly $false
    $expandedRoot = Expand-Element -Element $state.RootNodeElement -ExpansionStack $state.ExpansionStack
    Collect-NodeStats -Element $expandedRoot -PrefabPath $prefabPath
}

$inventory = @(
    foreach ($entry in $widgetStats.Values) {
        $support = Resolve-WidgetSupport -WidgetName $entry.WidgetName
        [pscustomobject]@{
            WidgetName = $entry.WidgetName
            Occurrences = $entry.Occurrences
            PrefabCount = $entry.Prefabs.Count
            Supported = $support.Supported
            MappingSource = $support.MappingSource
            RenderKind = $support.RenderKind
            NodeType = $support.NodeType
            SamplePrefabs = ($entry.SamplePrefabs -join " | ")
        }
    }
) | Sort-Object @{ Expression = { -[int]$_.Occurrences } }, @{ Expression = { $_.WidgetName } }

$unsupported = @($inventory | Where-Object { -not $_.Supported })
$supported = @($inventory | Where-Object { $_.Supported })
$genericReflection = @($inventory | Where-Object {
        $_.MappingSource -eq "Reflection"
    })
$totalOccurrences = Get-PropertySum -Items $inventory -PropertyName "Occurrences"
$unsupportedOccurrences = Get-PropertySum -Items $unsupported -PropertyName "Occurrences"
$supportedOccurrences = Get-PropertySum -Items $supported -PropertyName "Occurrences"
$genericReflectionOccurrences = Get-PropertySum -Items $genericReflection -PropertyName "Occurrences"

$inventoryCsvPath = Join-Path $resolvedOutputRoot "widget-inventory.csv"
$unsupportedCsvPath = Join-Path $resolvedOutputRoot "unsupported-widgets.csv"
$genericReflectionCsvPath = Join-Path $resolvedOutputRoot "generic-reflection-widgets.csv"
$summaryPath = Join-Path $resolvedOutputRoot "summary.md"

$inventory | Export-Csv -Path $inventoryCsvPath -NoTypeInformation -Encoding utf8
$unsupported | Export-Csv -Path $unsupportedCsvPath -NoTypeInformation -Encoding utf8
$genericReflection | Export-Csv -Path $genericReflectionCsvPath -NoTypeInformation -Encoding utf8

$summaryLines = [System.Collections.Generic.List[string]]::new()
$summaryLines.Add("# Bannerlord Widget Coverage")
$summaryLines.Add("")
$summaryLines.Add("Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")")
$summaryLines.Add("BannerlordRoot: $resolvedBannerlordRoot")
$summaryLines.Add("ModuleCount: $($resolvedModuleNames.Count)")
$summaryLines.Add("Modules: $($resolvedModuleNames -join ", ")")
$summaryLines.Add("")
$summaryLines.Add("## Summary")
$summaryLines.Add("")
$summaryLines.Add("- Prefabs scanned: $($prefabPaths.Count)")
$summaryLines.Add("- Distinct widget names after include expansion: $($inventory.Count)")
$summaryLines.Add("- Total widget occurrences after include expansion: $totalOccurrences")
$summaryLines.Add("- Supported widget names: $($supported.Count)")
$summaryLines.Add("- Unsupported widget names: $($unsupported.Count)")
$summaryLines.Add("- Supported occurrences: $supportedOccurrences")
$summaryLines.Add("- Unsupported occurrences: $unsupportedOccurrences")
$summaryLines.Add("- Reflection-mapped widget names: $($genericReflection.Count)")
$summaryLines.Add("- Reflection-mapped occurrences: $genericReflectionOccurrences")
$summaryLines.Add("")
$summaryLines.Add("## Top Unsupported Widgets")
$summaryLines.Add("")

if ($unsupported.Count -eq 0) {
    $summaryLines.Add("No unsupported widgets were found in the scanned modules.")
}
else {
    foreach ($entry in ($unsupported | Select-Object -First 25)) {
        $summaryLines.Add("- $($entry.WidgetName)")
        $summaryLines.Add("  Occurrences: $($entry.Occurrences)")
        $summaryLines.Add("  PrefabCount: $($entry.PrefabCount)")
        $summaryLines.Add("  SamplePrefabs: $($entry.SamplePrefabs)")
    }
}

$summaryLines.Add("")
$summaryLines.Add("## Top Reflection-Mapped Widgets")
$summaryLines.Add("")

if ($genericReflection.Count -eq 0) {
    $summaryLines.Add("No reflection-mapped widgets were found in the scanned modules.")
}
else {
    foreach ($entry in ($genericReflection | Select-Object -First 25)) {
        $summaryLines.Add("- $($entry.WidgetName)")
        $summaryLines.Add("  Occurrences: $($entry.Occurrences)")
        $summaryLines.Add("  PrefabCount: $($entry.PrefabCount)")
        $summaryLines.Add("  Mapping: $($entry.RenderKind) -> $($entry.NodeType)")
        $summaryLines.Add("  SamplePrefabs: $($entry.SamplePrefabs)")
    }
}

$summaryLines.Add("")
$summaryLines.Add("## Top Supported Widgets")
$summaryLines.Add("")
foreach ($entry in ($supported | Select-Object -First 15)) {
    $summaryLines.Add("- $($entry.WidgetName)")
    $summaryLines.Add("  Occurrences: $($entry.Occurrences)")
    $summaryLines.Add("  PrefabCount: $($entry.PrefabCount)")
    $summaryLines.Add("  Mapping: $($entry.MappingSource) / $($entry.NodeType)")
}

[System.IO.File]::WriteAllText(
    $summaryPath,
    ($summaryLines -join [System.Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Bannerlord widget coverage output written to:"
Write-Host "  $resolvedOutputRoot"
Write-Host "Summary:"
Write-Host "  $summaryPath"
