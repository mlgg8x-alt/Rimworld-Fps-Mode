# Rim3D localization

RimWorld uses its standard translation system. The active language is selected in
RimWorld settings; the mod has no separate language switch or XML loader. English
is the current base dictionary and is used as the fallback when another language
has no translation.

## Structure

- `English/Keyed/Rim3D.xml` contains the base interface strings.
- `<language>/DefInjected/JobDef/Rim3D.xml` contains translated job `reportString` values.

All player-facing runtime text is connected to the translation dictionary,
including menus, inventory, health, construction, recipes, context actions,
items, status messages, controls, unavailable-action reasons, shooting, and
Multiplayer messages. Compatibility uses the main Rim3D dictionary, which is a
required dependency. Technical logs, Unity object names, identifiers, editor
menus, and static About metadata are not keyed runtime strings. RimWorld Defs
provide item and content names and descriptions.

## Adding a string

1. Add the `Rim3D_` key and its English value to `English/Keyed/Rim3D.xml`.
2. In C# with `using Verse;`, translate at the point of use:

```csharp
"Rim3D_Take".Translate().ToString()
"Rim3D_TakeCount".Translate(count, target.stackCount).ToString()
```

Pass dynamic values as `{0}`, `{1}`, `{2}` parameters. Translate complete
sentences where possible so translators can reorder words and values. Preserve
parameters, game tokens such as `TargetA`, and markup. In Keyed XML, write a
line break as `\\n`.

Do not translate `defName`, class names, resource paths, shader names, or save
keys. Use `LabelCap` for item names and the game Defs for descriptions. Store
keys in static arrays and translate them when displayed; do not cache translated
text in static fields before the language loads.

## Adding a language

Copy `English` to a folder named for a RimWorld language (for example, `German`)
and translate the XML values while preserving keys and structure. Translation
changes do not require rebuilding the DLL; restart the game after editing files.
The normal Rim3D build includes the `Languages` folder in the mod ZIP and install.
