# XeCLI v1.0.7 Release Notes

XeCLI v1.0.7 pairs the verified NAND workflow with a fully localized experience, the new Inno Setup-based installer, and the canonical GitHub wiki so every operator has a single, current reference.

## Spanish Localization

- The launcher, 
gh commands, and Spectre CLI help now read from the Spanish resource catalog when UiLanguage=es, --lang es, or XECLI_LANG=es is set.
- Installer prompts, 
gh-ext helper output, and the avatar browser UI use the same string catalog so the desktop experience stays consistent.
- Missing Spanish keys fall back to English, ensuring CLI scripts still behave regardless of language.

## Inno Setup Installer

- The Inno installer uses the XeCLI logo, dark wizard style, branded hero art, and a Support Us Ko-fi button that opens the campaign page from the final wizard step.
- The release build script produces the self-contained win-x64 package and verified setup, then runs the installer in silent mode to validate install/uninstall, PATH registration, and uninstaller cleanup.
- The release also adds the launcher icon, license notice, and user-friendly prompts required for the new installer flow.

## Documentation and Wiki

- The GitHub wiki pages under wiki/ are now the canonical documentation surface; the HTML wiki, CSS, JS, and index files were removed.
- The Latest Features and Releases pages highlight every release, with this 1.0.7 entry surfacing the Spanish/localization work, the Inno Setup installer, and the wiki migration.
- Patch notes continue to cover shipped behavior only; README/wiki-only updates remain excluded from the release archive.

## Verification

- dotnet build src/Xbox360.Remote.Cli/Xbox360.Remote.Cli.csproj -c Release
- scripts/build-installer.ps1 -VerifyInstaller
- The published installer is XeCLI-1.0.7-setup-win-x64.exe (update the packaging commands before release publication).

## Related Docs

- [Latest Features](Latest-Features)
- [Releases](Releases)
- [XeCLI-XellFetch](XeCLI-XellFetch)
