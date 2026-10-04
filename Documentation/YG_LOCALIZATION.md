# YGames localization

Installed official modules: Localization 1.02 and AutoTranslateLangs 1.003.
Russian and English are enabled in SettingsYG2. Unsupported SDK locales fall back to English.
Newtonsoft JSON 3.2.2 is installed for the editor auto-translation tools.

Language comes from `YG2.lang`. Use `YG2.SwitchLanguage("ru")` or `YG2.SwitchLanguage("en")`.
YandexLanguage connects plugin events to static labels and dynamic game messages; the old JavaScript polling bridge is no longer used.

Authored HUD text and all runtime UI text factories attach the official `YG.LanguageLegacy.LanguageYG` component.
Static labels populate its ru/en fields from `Assets/Resources/GameTranslations.json` and translate through `AssignTranslate`.
LocalizedGameText drives these components so changing language preserves font sizes, user input, nicknames and live HUD values.
The component's autonomous callbacks are disabled because the game owns those values and initializes many labels at runtime.
Dynamic messages use the same catalog and plugin language before inserting numbers or user-provided names.

Menu, shop, rooms, deployment, controls, credits, cloud-save status, ads, scoreboard, rewards and combat HUD use this path.
The catalog includes 290 entries; weapon names, player names, room names and numbers are preserved.

Project integration adjustments:

- Plugin paths point to the actual `Assets/plugins/PluginYourGames` directory.
- Localization's platform language method uses the SDK in WebGL and system language in desktop builds.
- The editor JSON parser is delegated to `Assets/Editor/YGamesTranslationJson.cs`: Unity's firstpass plugin assembly cannot reference the package DLL directly.
- Module defines are enabled in project settings. Preserve these adjustments when updating the plugin.

Validation requests: `Temp/validate-menu-ui.request`, `Temp/validate-start-menu.request`, `Temp/validate-release-ui.request`, `Temp/validate-pc-release.request`.
Checks cover official component coverage, live ru/en switching, font preservation, editable room input, all weapon previews, controls, deployment and respawn.
Google's live translation endpoint and the Yandex SDK in a published WebGL build were not exercised; runtime uses the prepared translations.

Official documentation: https://max-games.ru/plugin-yg/doc/lang/
