## Summary

- What changed
- Why it changed
- Operator impact

## Validation

- [ ] `dotnet restore XeCLI.sln --locked-mode --runtime win-x64`
- [ ] `dotnet build XeCLI.sln --configuration Release --no-restore`
- [ ] Help output checked for any new commands or flags
- [ ] Relevant wiki pages updated if command behavior changed
- [ ] Release notes updated for shipped behavior changes, and doc-only or README-only edits were left out of patch notes
- [ ] PR text stays focused on shipped behavior and user impact
- [ ] No machine-specific paths, IPs, dumps, captures, temp appdata, or personal data added

## Testing Notes

- Console or environment used:
- Commands exercised:
- Known limitations:
