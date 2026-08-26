# TNTC - The .NET Translation Tool

TNTC is a command-line tool designed to help manage translations in .NET projects. It extracts strings from source code, manages translations across multiple languages, and provides a structured workflow for handling localization. It is loosely inspired by the original [TNT project](https://github.com/pragmatrix/tnt), but fully implemented in C#.

TNTC does not translate anything itself and needs no API key. It extracts the strings, tells you which ones are still missing a translation, and merges translations back in once something - an LLM, a coding agent, a translator - has produced them. The translating is deliberately left outside the tool, so the prompt and the review can live in your repository rather than compiled into a binary.

## Features

- **String Extraction**: Automatically extracts translatable strings from C# source code
- **Multi-language Support**: Handles translations for 20 languages - Chinese, Czech, Dutch, French, German, Greek, Hebrew, Hindi, Italian, Japanese, Korean, Malay, Nepali, Polish, Portuguese, Russian, Serbian, Spanish, Swedish and Ukrainian
- **Translation State Management**: Tracks the state of translations (New, NeedsReview, NeedsReviewTranslation, Translated, Final, LLMGenerated, ClaudeSkillGenerated)
- **Translation Validation**: Rejects a translation that dropped a placeholder, a tag, a link or the whitespace the caller concatenates against
- **JSON-based Storage**: Stores translations in a structured JSON format, easily manageable on the source control of your code-base
- **Source Location Tracking**: Keeps track of where translated strings were extracted from and used in the codebase

## Installation

The tool requires .NET 9.0 or later. To install the tool globally, you can run:

```bash
dotnet tool install --global TNTC
```

You can also install the tool locally scoped to a project, see [here](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install) for more information on installing dotnet tools.

## Usage

Using TNTC on your source-code requires tagging all strings that need to be translated with a .t() method call.

For example:

```csharp
var translatableString = "This string will be translated".t();  //Note the .t() added after the string
var fixedString        = "This string will be not translated";  
```

Interpolated strings can also be translated, but require a slightly different syntax to be able to capture them before they're interpolated:

```csharp
var interpolatedString = t($"Today is {DateTimeOffset.UtcNow:u}");  //Note the t($"...") is added around the interpolated string
```

## Command Line Commands

The three commands below are one loop: `extract` finds the work, `missing` hands it out, `apply` takes it
back. `verify` checks what is already on file.

```bash
tntc extract  ./MyApp                                  # scan the sources, queue what has no translation
tntc missing  ./MyApp --limit 50 --output batch.json    # write out the pending strings
#                                                        ... fill in batch.json ...
tntc apply    ./MyApp batch.json --model claude-opus-5  # merge them back, validating each one
tntc verify   ./MyApp                                   # check every translation on file
```

Every command takes the project folder as its argument, and `--languages` to narrow the work to a
comma-separated set of language codes (all 20 by default).

### Extract Command
```bash
tntc extract <projectFolder> [--languages de,fr,zh]
```
Scans the source files for translatable strings using the [Roslyn compiler](https://en.wikipedia.org/wiki/Roslyn_(compiler)),
refreshes each string's source locations, and queues anything with no translation yet as `New` with
empty text. The tool will look for any folder with a `.tnt` folder as a project to be translated;
`.tnt/extra-sources.json` lists further folders to scan.

A string no source file references any more keeps its translation but ends up with no source
locations, and is never queued for translation.

### Missing Command
```bash
tntc missing <projectFolder> [--languages de,fr] [--limit 50] [--output batch.json] [--include-unused] [--retranslate New]
```
Writes every string still waiting for a translation to a batch file (or to standard output when
`--output` is omitted), each with the source locations it is used at and one empty slot per language:

```json
{
  "languages": [ { "code": "de", "name": "German" } ],
  "totalPending": 128,
  "count": 50,
  "items": [
    {
      "originalString": "Delete {0} items",
      "sourceLocations": [ "MyApp/Views/ListView.cs:212" ],
      "translations": { "de": "" }
    }
  ]
}
```

Fill in each `translations` value and hand the file to `apply`. Leave a value empty to skip it. Never
edit `originalString` - it is the key `apply` merges on.

`--limit` caps how many strings a batch carries and reports how many remain, so a large backlog can be
worked in rounds. `--retranslate` also queues strings whose translation exists but is in one of the
given states (e.g. `--retranslate New` to redo everything a previous, less trusted pipeline produced);
their current text comes along as `currentTranslations` for reference.

### Apply Command
```bash
tntc apply <projectFolder> <batchFile> --model <modelId> [--state ClaudeSkillGenerated] [--allow-warnings] [--force]
```
Merges the filled-in batch back into the translation files and regenerates `.tnt-content`. Each
translation is checked before it is written, and a translation that fails is reported and left out:

| Rule | Severity | Catches |
| --- | --- | --- |
| `placeholder` | error | a `{0}`, `{0:MMM dd, yyyy}`, `{{name}}`, `%s` or `%d` that was dropped, added or translated |
| `url` | error | an `http(s)` link that was changed |
| `html` | error | an HTML/XML tag that was dropped, added or transliterated |
| `newlines` | error | a line break that was dropped or added |
| `html-attributes` | warning | a tag whose name survived but whose attributes were rewritten |
| `leading-whitespace` / `trailing-whitespace` | warning | whitespace the caller concatenates against |

Warnings are fatal too unless `--allow-warnings` is passed. A translation that was reviewed
(`Final` or `Translated`) is kept as it is unless `--force` is passed.

`--model` records what produced the translation on each record's `GeneratedBy`, and the records are
written under `--state`, `ClaudeSkillGenerated` by default. Pass `--state Translated` for translations
a human wrote or reviewed.

Exits with `2` when any translation was rejected, so a script notices.

### Verify Command
```bash
tntc verify <projectFolder> [--languages de,fr] [--strict]
```
Checks that every translation file parses and runs the table above over every translation already on
file, then prints a per-language summary of totals, pending strings, strings no source references any
more, and issues found. Exits with `3` when an error is found, so it can guard a build. `--strict`
makes warnings count as errors too.

### Upgrade from TNT Command
```bash
tntc upgrade-from-tnt <projectFolder>
```
Upgrades existing TNT translations to the new JSON format. This is useful when migrating from an older version of the tool.

## Exit Codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `1` | the command could not run (bad language code, missing batch file, unreadable translation file) |
| `2` | `apply` rejected at least one translation |
| `3` | `verify` found at least one error |
| `5` | an unexpected failure |

## The Claude Code skill

The repository ships a Claude Code skill, [`tntc-translate`](.claude/skills/tntc-translate/SKILL.md),
that drives the loop above end-to-end: it runs `extract`, takes batches from `missing`, translates
them in-context (reading the source locations when a string is ambiguous), merges them back through
`apply`'s validation, and finishes with `verify`. Translations it writes are recorded as
`ClaudeSkillGenerated` with the producing model on `GeneratedBy`.

The skill is also packed into the NuGet package under `skills/tntc-translate/`, so a consuming
repository can copy it into its own `.claude/skills/` folder.

Product-specific terminology does not live in the skill: it reads `.tnt/glossary.md` from the project
folder being translated, where the do-not-translate terms and terminology choices belong.

## Translation Files Folder Structure

- `.tnt/`: Configuration directory for translation settings
  - `translation-{language}.json`: Translation files for each supported language
  - `extra-sources.json`: Configuration for additional source directories
- `.tnt-content/`: Directory containing the final translation files used by the application
  - `{language}.tnt`: the flat `[[original, translated], ...]` pairs the application loads at run time
  - A string still waiting for a translation is left out of these files, so the application falls back
    to the original string instead of rendering an empty one

