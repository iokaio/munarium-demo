# SPDX-License-Identifier: Apache-2.0
# PSScriptAnalyzer settings for demo-ci and for a local run:
#
#   Invoke-ScriptAnalyzer -Path . -Recurse -Settings ./PSScriptAnalyzerSettings.psd1
#
# Every rule at every severity applies except the two below.
@{
    ExcludeRules = @(
        # These are operator scripts that narrate to a console. Write-Host is
        # the intended tool and they use it deliberately.
        'PSAvoidUsingWriteHost',
        # The repository is PowerShell 7 only, which reads BOM-less UTF-8
        # correctly. A byte-order mark would add nothing but diff noise.
        'PSUseBOMForUnicodeEncodedFile'
    )
}
