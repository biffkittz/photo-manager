<#
.SYNOPSIS
    Regenerates the protect.ps1 compatibility fixtures used by LegacyPhotoCipherTests.

.DESCRIPTION
    Loads Encrypt-FileBytes / Decrypt-FileBytes *verbatim* from the repository's protect.ps1 (the
    "Encryption/Decryption Functions" region) so the fixtures are produced by the real script code,
    not a re-implementation. Uses a throwaway TEST password; never run this with the real one.

    Modes:
      generate  - writes <name>.bin plaintexts and <name>.bin.encrypted ciphertexts into -OutDir
      decrypt   - decrypts every *.csharp.encrypted file in -OutDir with Decrypt-FileBytes, writing
                  *.csharp.decrypted (proves files written by the C# code are readable by protect.ps1)

.EXAMPLE
    docker run --rm -v "$PWD:/repo:ro" -v "$PWD/out:/out" mcr.microsoft.com/powershell:latest `
      pwsh -File /repo/app/backend/tests/PhotoManager.Core.Tests/TestData/Legacy/generate-vectors.ps1 `
      -ProtectScript /repo/protect.ps1 -OutDir /out -Mode generate
#>
param(
    [Parameter(Mandatory = $true)][string] $ProtectScript,
    [Parameter(Mandatory = $true)][string] $OutDir,
    [ValidateSet("generate", "decrypt")][string] $Mode = "generate",
    [string] $Password = "test-only pässwörd ✓ 123"
)

$ErrorActionPreference = "Stop"
$source = Get-Content -Raw -Path $ProtectScript
$match = [regex]::Match($source, '(?s)#region Encryption/Decryption Functions(.*?)#endregion')
if (-not $match.Success) { throw "Could not find the encryption region in $ProtectScript" }
# Define the functions exactly as written in protect.ps1.
. ([scriptblock]::Create($match.Groups[1].Value))

if ($Mode -eq "generate") {
    $sizes = @(0, 1, 15, 16, 17, 1000, 65539)
    foreach ($size in $sizes) {
        $bytes = New-Object byte[] $size
        for ($i = 0; $i -lt $size; $i++) { $bytes[$i] = [byte](($i * 31 + 7) % 251) }
        $plain = Join-Path $OutDir "plain-$size.bin"
        [System.IO.File]::WriteAllBytes($plain, $bytes)
        Encrypt-FileBytes -InputPath $plain -OutputPath "$plain.encrypted" -Password $Password
    }
    Write-Host "Generated $($sizes.Count) vectors in $OutDir"
}
else {
    Get-ChildItem -Path $OutDir -Filter "*.csharp.encrypted" | ForEach-Object {
        $out = $_.FullName -replace '\.encrypted$', '.decrypted'
        Decrypt-FileBytes -InputPath $_.FullName -OutputPath $out -Password $Password
        Write-Host "Decrypted $($_.Name)"
    }
}
