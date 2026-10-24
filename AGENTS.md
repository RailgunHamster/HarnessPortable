# UI operations and automation API

The Windows UI automation contract is in `docs/windows-automation-api.md`.

- Every user operation must be reachable through the public automation API. Standard controls use `ui.act`; custom gestures and application operations need registered semantic methods.
- Declare an explicit, stable `AutomationProperties.AutomationId` for every interactive XAML control. Keep the ID when renaming a handler or changing its visible text.
- Construct dynamic buttons and menu items through `AutomationControls`. Add tray operations to `TrayIconService.Actions`, which feeds both the UI and the API.
- Map custom input gestures in `spec/windows-ui-api-gestures.json` to implemented, registered operations. UI handlers and API handlers must call the same application operation. Do not invoke private UI handlers through reflection or simulate screen coordinates.
- Use `AutomationDialogs` for native message/file dialogs so automated runs can complete those flows. Ordinary launches retain native OS dialogs.
- Keep passwords and authentication fields write-only in snapshots. Mark custom sensitive inputs with `AutomationMetadata.Sensitive`.
- A new control kind, gesture, dialog, or operation requires a contract/behavior test. Do not weaken a coverage gate or label an input control observational merely to make the test pass.
- Run Windows tests and the cross-process fixture after changing the contract. Tests use a fresh data directory and loopback fixtures; never use saved user profiles, real server credentials, or install over a user's application.
- This runtime API currently covers Windows. Do not claim Android/macOS API support until native adapters and platform tests exist.
