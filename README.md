# TNTC - The .NET Translation Tool

TNTC is a command-line tool designed to help manage translations in .NET projects. It extracts strings from source code, manages translations across multiple languages, and provides a structured workflow for handling localization. It is loosely inspired by the original [TNT project](https://github.com/pragmatrix/tnt), but fully implemented in C#.

TNTC does not translate anything itself and needs no API key. It extracts the strings, tells you which ones are still missing a translation, and merges translations back in once something - an LLM, a coding agent, a translator - has produced them. The translating is deliberately left outside the tool, so the prompt and the review can live in your repository rather than compiled into a binary.

## Features

- **String Extraction**: Automatically extracts translatable strings from C# source code, and from the referenced NuGet packages - their shipped tables, or their IL when they ship none
- **Multi-language Support**: Handles translations for 20 languages - Chinese, Czech, Dutch, French, German, Greek, Hebrew, Hindi, Italian, Japanese, Korean, Malay, Nepali, Polish, Portuguese, Russian, Serbian, Spanish, Swedish and Ukrainian
- **Translation State Management**: Tracks the state of translations (New, NeedsReview, NeedsReviewTranslation, Translated, Final, LLMGenerated, ClaudeSkillGenerated, PackageProvided)
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

A fifth command, `sync`, re-reads what the referenced packages contribute - see
[Strings that come from a package](#strings-that-come-from-a-package).

Every command takes the project folder as its argument, and `--languages` to narrow the work to a
comma-separated set of language codes (all 20 by default).

### Extract Command
```bash
tntc extract <projectFolder> [--languages de,fr,zh] [--include-packages] [--scan-assemblies]
```
Scans the source files for translatable strings using the [Roslyn compiler](https://en.wikipedia.org/wiki/Roslyn_(compiler)),
refreshes each string's source locations, and queues anything with no translation yet as `New` with
empty text. The tool will look for any folder with a `.tnt` folder as a project to be translated;
`.tnt/extra-sources.json` lists further folders to scan.

A string no source file references any more keeps its translation but ends up with no source
locations, and is never queued for translation.

`--include-packages` and `--scan-assemblies` widen the scan beyond this repository - see
[Strings that come from a package](#strings-that-come-from-a-package).

### Sync Command
```bash
tntc sync <projectFolder> [--languages de,fr] [--scan-assemblies]
```
Re-imports the translation tables the referenced packages ship and rewrites `.tnt-content` from what
is on file. Run it after a package version moved. It does not read this project's sources, so it
never queues anything - that is `extract`'s job.

### Missing Command
```bash
tntc missing <projectFolder> [--languages de,fr] [--limit 50] [--output batch.json] [--include-unused] [--retranslate New] [--include-package-covered]
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

A string a referenced package already translated is left out, because it is already answered;
`--include-package-covered` puts it back, for a project that wants to word something differently.
The package's current wording then arrives in `currentTranslations`.

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
file, then prints a per-language summary of totals, pending strings, strings a referenced package
answers, strings no source references any more, and issues found. When packages contributed, it
finishes with what each one ships and - for the strings scanned out of their assemblies - how many
are answered by a package, by this project, or by nothing. Exits with `3` when an error is found, so it can guard a build. `--strict`
makes warnings count as errors too.

### Upgrade from TNT Command
```bash
tntc upgrade-from-tnt <projectFolder>
```
Upgrades existing TNT translations to the new JSON format. This is useful when migrating from an older version of the tool.

## Strings that come from a package

An application's UI is rarely all its own. A component library, a document viewer, a charting
package each draw text, and `extract` cannot see any of it: those sources are not in the repository.
For years the consequence was silent - a host shipping twenty languages showed an English button
because the button belonged to a package.

Two flags on `extract` close that, and they belong together:

```bash
tntc extract ./MyApp --include-packages --scan-assemblies
```

- **`--include-packages`** imports the tables a package ships. The convention is `l10n/<code>.tnt`
  inside the nupkg - exactly the files `tntc` writes to `.tnt-content` - so a package translates
  itself with TNTC and packs the result. Those strings are then *answered*: they reach the
  application through this project's `.tnt-content` and are never queued for its translator.
- **`--scan-assemblies`** reads the referenced assemblies' IL for the strings they will ask TNT to
  translate, so a package that draws UI but ships no tables still gets its strings queued here.
  Nothing has to change in the package for this to work.

Both read the resolved graph from `obj/project.assets.json`, so the project has to have been
restored; a project folder that was not is named on stderr and contributes nothing. What they find
is written to `.tnt/packages/` - the manifest of what each package contributed, its tables, and the
strings scanned out of its assemblies - and every other command reads it from there. So `missing`,
`apply` and `verify` need no flags, and re-running them without a restore reproduces exactly the
same `.tnt-content`.

`.tnt/packages/` is generated and belongs in the commit: it is the reviewable record of what a
package version contributes, which is the argument for settling precedence in a diff rather than
merging tables at run time. When a package version moves, `tntc sync` re-imports it.

### Precedence, and how to override

For any one string and language:

- a translation of this project's own wins, always;
- otherwise the package's is used, and the project's record is marked `PackageProvided` with the
  package's coordinates - so the file still lists every string in use and says who answers this one;
- a package that stops shipping a string makes it work again on the next `extract`.

To word something differently from the package, `tntc missing --include-package-covered` offers it
with the package's current text in `currentTranslations`. Once applied, the project's translation
shadows the package's.

### What the scanner can and cannot tell you

It matches the two shapes the C# compiler emits for TNT's two entry points - a literal handed to
`t(string)`, and the format a `FormattableString` was built from before `t(FormattableString)`.
`TNT.T` is matched by name rather than by assembly identity, because a Transpose library declares
its own copy of it.

- **A scanned key has no `path:line`.** It records the package, type and method it was found in
  (`Tesserae!Tesserae.Helpers.Validation.NotEmpty:0`), so a translator can see where a string is
  drawn but not open the call site. That is the quality argument for a package translating itself
  and shipping the tables.
- **A `t(string)` call whose argument is not a literal is reported, not recorded** - it is the
  `$"…".t()` mistake, where the string is built before it is looked up and the table is asked for a
  key it can never hold. Nothing can translate it; it is the package's to fix.
- **A reference assembly carries no method bodies.** The runtime assets are scanned where a package
  has them, and a package that ships only reference assemblies yields nothing rather than something
  wrong.
- A key read from IL is the key TNT will actually look up, format specifiers included
  (`{0:n1} seconds`), which is not always what a source-level extractor records for the same call.

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

To use the skill in a repository, install it once and commit the result:

```bash
tntc install-skill <repositoryRoot>    # writes .claude/skills/tntc-translate/ - commit it
```

`install-skill` targets the repository root (where `.claude` lives), which is usually not the
project folder with the `.tnt` folder - that sits deeper in the tree.

After that first install, two mechanisms keep the committed copy in sync - use whichever fits the
repository (they write identical content for a given version, so having both is harmless):

- **The `TNTC.Skills` NuGet package** - for repositories that build .NET anyway. Add it to a
  project that builds regularly:

  ```xml
  <PackageReference Include="TNTC.Skills" Version="..." PrivateAssets="all"/>
  ```

  Its `buildTransitive` target updates `.claude/skills/tntc-translate/` on build whenever the
  installed `.skills-version` differs from the package's, and prints
  `TNTC.Skills: updated the tntc-translate skill ... Commit the diff.` when it does. The committed
  `PackageReference` version is the repository's source of truth, so up- and downgrades both apply.
  Opt out per build with `/p:TntcSkipSkillUpdate=true`. (The TNTC tool package itself cannot be
  `PackageReference`'d - NuGet rejects tool packages with NU1212 - which is why this companion
  package exists.)

- **The tool itself** - for everything else. The `extract` / `missing` / `apply` / `verify`
  commands rewrite an installed skill whose `.skills-version` is older than the running tool, and
  say so on stderr. The tool never downgrades: when the installed skill is newer (the repository's
  package moved ahead), it prints a hint to `dotnet tool update --global TNTC` instead.

Either way the diff is expected and belongs in the commit, the same as any generated file. A
repository that never ran `install-skill` is left alone by both mechanisms.

Product-specific terminology does not live in the skill: it reads `.tnt/glossary.md` from the project
folder being translated, where the do-not-translate terms and terminology choices belong.

## Translation Files Folder Structure

- `.tnt/`: Configuration directory for translation settings
  - `translation-{language}.json`: Translation files for each supported language
  - `extra-sources.json`: Configuration for additional source directories
  - `packages/`: generated - what the referenced packages contribute (`packages.json` manifest,
    `translations-{language}.json` tables, `strings.json` scanned out of their assemblies). Written
    by `extract --include-packages` / `--scan-assemblies` and by `sync`; read by every command.
- `.tnt-content/`: Directory containing the final translation files used by the application
  - `{language}.tnt`: the flat `[[original, translated], ...]` pairs the application loads at run time
  - A string still waiting for a translation is left out of these files, so the application falls back
    to the original string instead of rendering an empty one

