$OriginalModRoot = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3429142659'
$CombatExtendedRoot = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2890901044'
$RimWorldDataRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Data'
$null = Add-Type -AssemblyName System.Xml.Linq
$patchRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$errors = [System.Collections.Generic.List[string]]::new()
$patchedGuns = @{}
$replacedXpaths = @{}
$originalThingDefs = @{}
$knownDefNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

function Add-KnownDefsFromRoot {
    param(
        [string]$Root,
        [scriptblock]$FileFilter = { $true }
    )

    if (-not (Test-Path -LiteralPath $Root)) {
        return
    }

    Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.xml' |
        Where-Object $FileFilter |
        ForEach-Object {
            try {
                $knownDocument = [System.Xml.Linq.XDocument]::Load($_.FullName)
                foreach ($defNameElement in $knownDocument.Descendants('defName')) {
                    if ($null -ne $defNameElement.Parent -and
                        $null -ne $defNameElement.Parent.Parent -and
                        $defNameElement.Parent.Parent.Name.LocalName -eq 'Defs') {
                        $null = $knownDefNames.Add($defNameElement.Value)
                    }
                }
            }
            catch { }
        }
}

Add-KnownDefsFromRoot -Root $OriginalModRoot -FileFilter {
    $_.FullName -match '\\1\.6(_[^\\]+)?\\'
}
Add-KnownDefsFromRoot -Root $CombatExtendedRoot
Add-KnownDefsFromRoot -Root $RimWorldDataRoot
Add-KnownDefsFromRoot -Root $patchRoot

Get-ChildItem -LiteralPath $OriginalModRoot -Recurse -Filter '*.xml' |
    Where-Object { $_.FullName -match '\\1\.6(_[^\\]+)?\\' } |
    ForEach-Object {
        try {
            $originalDocument = [System.Xml.Linq.XDocument]::Load($_.FullName)
            foreach ($thingDef in $originalDocument.Descendants('ThingDef')) {
                $nameElement = $thingDef.Element('defName')
                if ($null -ne $nameElement -and -not $originalThingDefs.ContainsKey($nameElement.Value)) {
                    $originalThingDefs[$nameElement.Value] = $thingDef
                }
            }
        }
        catch { }
    }

Get-ChildItem -LiteralPath $patchRoot -Recurse -Filter '*.xml' | ForEach-Object {
    try {
        $document = [System.Xml.Linq.XDocument]::Load($_.FullName)

        if ($document.Root.Name.LocalName -eq 'LanguageData') {
            foreach ($translation in $document.Root.Elements()) {
                $translatedDef = $translation.Name.LocalName.Split('.')[0]
                if (-not $knownDefNames.Contains($translatedDef)) {
                    $errors.Add("translation targets missing defName '$translatedDef' in $($_.FullName)")
                }
            }
        }

        foreach ($recipe in $document.Descendants('RecipeDef')) {
            $recipeName = [string]$recipe.Element('defName')
            $ingredientDefs = @(
                $recipe.Descendants('ingredients').Descendants('thingDefs').Elements() |
                    ForEach-Object { $_.Name.LocalName }
            )
            $fixedDefs = @(
                $recipe.Descendants('fixedIngredientFilter').Descendants('thingDefs').Elements() |
                    ForEach-Object { $_.Name.LocalName }
            )

            foreach ($ingredientDef in $ingredientDefs) {
                if ($fixedDefs.Count -gt 0 -and $ingredientDef -notin $fixedDefs) {
                    $errors.Add("$($_.FullName): recipe '$recipeName' uses '$ingredientDef' but fixedIngredientFilter does not allow it")
                }
            }
        }

        foreach ($operation in $document.Descendants('Operation')) {
            $operationClass = $operation.Attribute('Class').Value

            if ($operationClass -eq 'PatchOperationReplace' -and $null -ne $operation.Element('xpath')) {
                $replaceXpath = $operation.Element('xpath').Value.Trim()
                if ($replacedXpaths.ContainsKey($replaceXpath)) {
                    $errors.Add("duplicate PatchOperationReplace XPath '$replaceXpath': $($replacedXpaths[$replaceXpath]) and $($_.FullName)")
                }
                else {
                    $replacedXpaths[$replaceXpath] = $_.FullName
                }
            }

            if ($operationClass -ne 'CombatExtended.PatchOperationMakeGunCECompatible') {
                continue
            }

            $gunDef = $operation.Element('defName').Value
            if ([string]::IsNullOrWhiteSpace($gunDef)) {
                continue
            }

            if ($patchedGuns.ContainsKey($gunDef)) {
                $errors.Add("duplicate CE gun patch '$gunDef': $($patchedGuns[$gunDef]) and $($_.FullName)")
            }
            else {
                $patchedGuns[$gunDef] = $_.FullName
            }
        }

        foreach ($xpathElement in $document.Descendants('xpath')) {
            $xpath = $xpathElement.Value
            $targetMatches = [regex]::Matches($xpath, 'defName\s*=\s*["'']([^"'']+)["'']')
            foreach ($targetMatch in $targetMatches) {
                $targetDef = $targetMatch.Groups[1].Value
                if (-not $knownDefNames.Contains($targetDef)) {
                    $errors.Add("XPath targets missing defName '$targetDef' in $($_.FullName): $($xpath.Trim())")
                }
            }
        }

        foreach ($operation in $document.Descendants('Operation')) {
            if ($operation.Attribute('Class').Value -ne 'PatchOperationAdd') {
                continue
            }

            $xpathElement = $operation.Element('xpath')
            if ($null -eq $xpathElement) {
                continue
            }

            $xpath = $xpathElement.Value.Trim()
            if ($xpath -notmatch 'ThingDef\[defName="([^"]+)"\]/statBases$') {
                continue
            }

            $targetDef = $Matches[1]
            if (-not $originalThingDefs.ContainsKey($targetDef)) {
                continue
            }

            $originalStats = $originalThingDefs[$targetDef].Element('statBases')
            $addedStats = $operation.Element('value').Elements()
            foreach ($addedStat in $addedStats) {
                if ($null -ne $originalStats -and $null -ne $originalStats.Element($addedStat.Name)) {
                    $errors.Add("PatchOperationAdd duplicates existing stat '$($addedStat.Name.LocalName)' on '$targetDef' in $($_.FullName)")
                }
            }
        }
    }
    catch {
        $errors.Add("$($_.FullName): $($_.Exception.Message)")
    }
}

if ($errors.Count -gt 0) {
    $errors
    exit 1
}

Write-Output 'ALL_BALANCE_XML_VALID'
