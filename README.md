# TNTC - The .NET Translation Tool

TNTC is a command-line tool designed to help manage translations in .NET projects. It extracts strings from source code, manages translations across multiple languages, and provides a structured workflow for handling localization. It is loosely inspired by the original [TNT project](https://github.com/pragmatrix/tnt), but fully implemented in C# and supporting LLM-based translations via an OpenAI integration.

## Features

- **String Extraction**: Automatically extracts translatable strings from C# source code
- **Multi-language Support**: Handles translations for multiple languages including Chinese, German, French, Spanish, Italian, Portuguese, among others
- **Translation State Management**: Tracks the state of translations (New, NeedsReview, NeedsReviewTranslation, Translated, Final)
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

TNTC provides several commands:

### Extract Command
```bash
tntc extract <projectFolder>
```
Extracts all strings from source files and updates translations. The tool will look for any folder with a `.tnt` folder as a project to be translated.

**Note**: This command requires an [OpenAI API key]([url](https://platform.openai.com/docs/quickstart/step-2-setup-your-api-key)) to be set in the environment variable `OPENAI_API_KEY` for automatic translation functionality.

```bash
# Example with API key
export OPENAI_API_KEY='your-api-key-here'
tntc extract <projectFolder>
```

Source files are scanned for translatable strings using the [Roslyn compiler]([url](https://en.wikipedia.org/wiki/Roslyn_(compiler))).

### Upgrade from TNT Command
```bash
tntc upgrade-from-tnt <projectFolder>
```
Upgrades existing TNT translations to the new JSON format. This is useful when migrating from an older version of the tool.

## Translation Files Folder Structure

- `.tnt/`: Configuration directory for translation settings
  - `translation-{language}.json`: Translation files for each supported language
  - `extra-sources.json`: Configuration for additional source directories
- `.tnt-content/`: Directory containing the final translation files used by the application
  - Contains the processed and finalized translations ready for use

