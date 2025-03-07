using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TNTc;

namespace CodeScanner
{
    public class TranslatableString
    {
        public string         SourceString   { get; set; }
        public SourceLocation SourceLocation { get; set; }
    }

    public class SourceLocation : IEquatable<SourceLocation>
    {
        public string SourceFilePath { get; set; }
        public int    SourceFileLine { get; set; }

        public bool Equals(SourceLocation? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return SourceFilePath == other.SourceFilePath && SourceFileLine == other.SourceFileLine;
        }
        public override bool Equals(object? obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((SourceLocation)obj);
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(SourceFilePath, SourceFileLine);
        }
    }

    public class SourceLocationConverter : JsonConverter<SourceLocation>
    {
        public SourceLocationConverter() { }

        public override SourceLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            var split = value.Split(":", StringSplitOptions.RemoveEmptyEntries);

            if (split.Length != 2) throw new InvalidOperationException(value);
            var sourceFilePath = split[0];
            var sourceFileLine = int.Parse(split[1]);

            return new SourceLocation { SourceFilePath = sourceFilePath, SourceFileLine = sourceFileLine };
        }

        public override void Write(Utf8JsonWriter writer, SourceLocation value, JsonSerializerOptions options)
            => writer.WriteStringValue($"{value.SourceFilePath}:{value.SourceFileLine}");
    }

    public class TranslationRecordStateConverter : JsonConverter<TranslationRecordState>
    {
        public override TranslationRecordState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            return Enum.Parse<TranslationRecordState>(value, ignoreCase: true);
        }

        public override void Write(Utf8JsonWriter writer, TranslationRecordState value, JsonSerializerOptions options)
            => writer.WriteStringValue(Enum.GetName<TranslationRecordState>(value));
    }

    public class StringsCollector : CSharpSyntaxWalker
    {
        private readonly SemanticModel            SemanticModel;
        private readonly string                   _currentFilePath;
        public           List<TranslatableString> _stringsWithTMethod = new List<TranslatableString>();

        public StringsCollector(SemanticModel semanticModel, string filePath)
        {
            SemanticModel    = semanticModel;
            _currentFilePath = filePath;
        }

        // Add method to handle return statements
        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            // If the return expression is a parenthesized expression
            if (node.Expression is ParenthesizedExpressionSyntax parenthesized)
            {
                // Check if it's a method invocation with .t()
                if (parenthesized.Expression is InvocationExpressionSyntax invocation)
                {
                    HandleInvocation(invocation);
                }
            }
            base.VisitReturnStatement(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            HandleInvocation(node);
            base.VisitInvocationExpression(node);
        }

        private void HandleInvocation(InvocationExpressionSyntax node)
        {
            // Handle both member access (something.t()) and direct t() calls
            bool                          isTranslationMethod = false;
            MemberAccessExpressionSyntax? memberAccess        = null;
            ExpressionSyntax?             stringExpression    = null;

            if (node.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "t")
            {
                isTranslationMethod = true;
                stringExpression    = ma.Expression;
            }
            else if (node.Expression is IdentifierNameSyntax ins && ins.Identifier.ValueText == "t")
            {
                isTranslationMethod = true;
                stringExpression    = node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            }

            if (!isTranslationMethod) return;

            if (stringExpression == null) return;

            // Handle string literals
            if (stringExpression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                string stringValue = (string)literal.Token.Value;
                AddTranslatableString(stringValue, literal);
            }
            // Handle interpolated strings
            else if (stringExpression is InterpolatedStringExpressionSyntax interpolated)
            {
                var stringBuilder    = new System.Text.StringBuilder();
                int placeholderCount = 0;

                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text)
                    {
                        stringBuilder.Append(text.TextToken.ValueText);
                    }
                    else if (content is InterpolationSyntax)
                    {
                        stringBuilder.Append($"{{{placeholderCount}}}");
                        placeholderCount++;
                    }
                }
                AddTranslatableString(stringBuilder.ToString(), interpolated);
            }
            // Handle binary expressions and parenthesized expressions
            else if (stringExpression is BinaryExpressionSyntax ||
                stringExpression is ParenthesizedExpressionSyntax)
            {
                ExtractStringFromExpression(stringExpression);
            }
        }

        private void ExtractStringFromExpression(ExpressionSyntax expr)
        {
            // Remove this check since we already validated the language code
            // if (languageArg != null && !IsValidLanguageCode(languageArg))
            // {
            //     return;
            // }

            switch (expr)
            {
                case BinaryExpressionSyntax binExpr when binExpr.OperatorToken.ValueText == "+":
                    // Try evaluating the full expression first
                    var constantValue = SemanticModel.GetConstantValue(expr);

                    if (constantValue.HasValue && constantValue.Value is string stringValue)
                    {
                        AddTranslatableString(stringValue, expr);
                        return;
                    }

                    // If we can't evaluate the full expression, try the left side only
                    if (binExpr.Left is LiteralExpressionSyntax leftLiteral &&
                        leftLiteral.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        string leftValue = (string)leftLiteral.Token.Value;
                        AddTranslatableString(leftValue, leftLiteral);
                    }
                    break;

                case ParenthesizedExpressionSyntax parenthesized:
                    ExtractStringFromExpression(parenthesized.Expression);
                    break;

                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    string literalValue = (string)literal.Token.Value;
                    AddTranslatableString(literalValue, literal);
                    break;
            }
        }

        private string? ExtractLanguageArg(ArgumentSyntax langArg)
        {
            // Try to evaluate using semantic model first
            var constantValue = SemanticModel.GetConstantValue(langArg.Expression);

            if (constantValue.HasValue && constantValue.Value is string stringValue)
            {
                return stringValue;
            }

            // Handle literal strings directly
            if (langArg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return (string)literal.Token.Value;
            }

            // Handle method invocation (for getLangr() case)
            if (langArg.Expression is InvocationExpressionSyntax invocation)
            {
                var methodValue = SemanticModel.GetConstantValue(invocation);

                if (methodValue.HasValue && methodValue.Value is string methodStringValue)
                {
                    return methodStringValue;
                }
            }

            // Handle string concatenation
            if (langArg.Expression is BinaryExpressionSyntax binExpr)
            {
                var fullValue = SemanticModel.GetConstantValue(binExpr);

                if (fullValue.HasValue && fullValue.Value is string fullStringValue)
                {
                    return fullStringValue;
                }
            }

            // Handle string interpolation
            if (langArg.Expression is InterpolatedStringExpressionSyntax interpolated)
            {
                var fullValue = SemanticModel.GetConstantValue(interpolated);

                if (fullValue.HasValue && fullValue.Value is string interpolatedValue)
                {
                    return interpolatedValue;
                }
            }

            return null;
        }

        private void AddTranslatableString(string value, SyntaxNode node)
        {
            var lineSpan = node.GetLocation().GetLineSpan();

            _stringsWithTMethod.Add(new TranslatableString
            {
                SourceString = value,
                SourceLocation = new SourceLocation()
                {
                    SourceFilePath = _currentFilePath,
                    SourceFileLine = lineSpan.StartLinePosition.Line + 1 // Add 1 because line numbers are 0-based
                }
            });
        }
    }
}