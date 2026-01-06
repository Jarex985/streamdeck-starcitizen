## Property Inspector assets

This folder is the on-disk location referenced by both the plugin manifest and the project file for Property Inspector pages:

- `manifest.json` paths such as `PropertyInspector/StarCitizen/ActionDelay.html`
- `starcitizen.csproj` `<Content Include="PropertyInspector\StarCitizen\*.html">`

Only `ActionDelay.html` lives here in source form today, but the folder itself is required so the manifest paths remain valid and we avoid name collisions with the root-level Star Citizen templates (for example `dialtemplate.html`, `macrotemplate.html`, and `statictemplate.html`). Do **not** delete the folder; it will be populated during packaging/build when additional Property Inspector pages are emitted.
