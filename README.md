# TNTC - The .NET Translation Tool

TNTC is a command-line tool designed to help manage translations in .NET projects. It extracts strings from source code, manages translations across multiple languages, and provides a structured workflow for handling localization. It is loosely inspired by the original [TNT project](https://github.com/pragmatrix/tnt), but fully implemented in C# and supporting LLM-based translations via an OpenAI integration.

## Features

- **String Extraction**: Automatically extracts translatable strings from C# source code
- **Multi-language Support**: Handles translations for multiple languages including:
  - Chinese
  - German
  - French
  - Spanish
  - Italian
  - Portuguese
- **Translation State Management**: Tracks the state of translations (New, NeedsReview, NeedsReviewTranslation, Translated, Final)
- **JSON-based Storage**: Stores translations in a structured JSON format
- **Source Location Tracking**: Keeps track of where translated strings are used in the codebase

## Installation

The tool requires .NET 9.0 or later. To build the project:

```bash
dotnet build
```

## Usage

TNTc provides several commands:

### Extract Command
```bash
tntc extract <projectFolder>
```
Extracts all strings from source files and updates translations. The tool will look for any folder with a `.tnt` folder as a project to be translated.

**Note**: This command requires an OpenAI API key to be set in the environment variable `OPENAI_API_KEY` for automatic translation functionality.

```bash
# Example with API key
export OPENAI_API_KEY='your-api-key-here'
tntc extract <projectFolder>
```

### Upgrade from TNT Command
```bash
tntc upgrade-from-tnt <projectFolder>
```
Upgrades existing TNT translations to the new JSON format. This is useful when migrating from an older version of the tool.

## Project Structure

## Project Structure

- `.tnt/`: Configuration directory for translation settings
  - `translation-{language}.json`: Translation files for each supported language
  - `extra-sources.json`: Configuration for additional source directories
- `.tnt-content/`: Directory containing the final translation files used by the application
  - Contains the processed and finalized translations ready for use
- Source files are scanned for translatable strings using the Roslyn compiler platform
