// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Legacy;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Formatting
{
    internal class DefaultRazorFormattingService : RazorFormattingService
    {
        private readonly ForegroundDispatcher _foregroundDispatcher;
        private readonly FilePathNormalizer _filePathNormalizer;
        private readonly Lazy<ILanguageServer> _server;

        public DefaultRazorFormattingService(
            ForegroundDispatcher foregroundDispatcher,
            FilePathNormalizer filePathNormalizer,
            Lazy<ILanguageServer> server)
        {
            if (foregroundDispatcher is null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (filePathNormalizer is null)
            {
                throw new ArgumentNullException(nameof(filePathNormalizer));
            }

            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            _foregroundDispatcher = foregroundDispatcher;
            _filePathNormalizer = filePathNormalizer;
            _server = server;
        }

        public async override Task<TextEdit[]> FormatAsync(Uri uri, RazorCodeDocument codeDocument, Range range, FormattingOptions options)
        {
            var edits2 = await FormatProjectedDocument(RazorLanguageKind.Html, range, uri.AbsolutePath, options);
            var syntaxTree = codeDocument.GetSyntaxTree();
            var classifiedSpans = syntaxTree.GetClassifiedSpans();
            var indentations = GetLineIndentationMap(codeDocument.Source, classifiedSpans);

            var edits = new List<TextEdit>();
            for (var i = (int)range.Start.Line; i <= (int)range.End.Line; i++)
            {
                var context = indentations[i];
                if (context.IndentationLevel == -1)
                {
                    // Couldn't determine the desired indentation. Leave this line alone.
                    continue;
                }

                var desiredIndentation = context.IndentationLevel * options.TabSize;
                var effectiveIndentation = desiredIndentation - context.ExistingIndentation;
                if (effectiveIndentation > 0)
                {
                    var indentationChar = options.InsertSpaces ? ' ' : '\t';
                    var indentationString = new string(indentationChar, (int)effectiveIndentation);
                    var edit = new TextEdit()
                    {
                        Range = new Range(new Position(i, 0), new Position(i, 0)),
                        NewText = indentationString,
                    };

                    edits.Add(edit);
                }
                else if (effectiveIndentation < 0)
                {
                    var edit = new TextEdit()
                    {
                        Range = new Range(new Position(i, 0), new Position(i, -effectiveIndentation)),
                        NewText = string.Empty,
                    };

                    edits.Add(edit);
                }
            }

            return edits.ToArray();
        }

        internal static Dictionary<int, IndentationContext> GetLineIndentationMap(RazorSourceDocument source, IReadOnlyList<ClassifiedSpanInternal> classifiedSpans)
        {
            var result = new Dictionary<int, IndentationContext>();
            var total = 0;
            for (var i = 0; i < source.Lines.Count; i++)
            {
                // Get first non-whitespace character position
                var lineLength = source.Lines.GetLineLength(i);
                var nonWsChar = 0;
                for (var j = 0; j < lineLength; j++)
                {
                    var ch = source[total + j];
                    if (!char.IsWhiteSpace(ch) && !ParserHelpers.IsNewLine(ch))
                    {
                        nonWsChar = j;
                        break;
                    }
                }

                // position now contains the first non-whitespace character or 0. Get the corresponding ClassifiedSpan.
                if (TryGetClassifiedSpanIndex(total + nonWsChar, classifiedSpans, out var index))
                {
                    var span = classifiedSpans[index];
                    result[i] = new IndentationContext
                    {
                        Line = i,
                        IndentationLevel = span.IndentationLevel,
                        ExistingIndentation = nonWsChar,
                    };
                }
                else
                {
                    // Couldn't find a corresponding ClassifiedSpan.
                    result[i] = new IndentationContext
                    {
                        Line = i,
                        IndentationLevel = -1,
                        ExistingIndentation = nonWsChar,
                    };
                }

                total += lineLength;
            }

            return result;
        }

        internal static bool TryGetClassifiedSpanIndex(int absoluteIndex, IReadOnlyList<ClassifiedSpanInternal> classifiedSpans, out int index)
        {
            index = -1;
            for (var i = 0; i < classifiedSpans.Count; i++)
            {
                var classifiedSpan = classifiedSpans[i];
                var span = classifiedSpan.Span;

                if (span.AbsoluteIndex <= absoluteIndex)
                {
                    var end = span.AbsoluteIndex + span.Length;
                    if (end > absoluteIndex)
                    {
                        if (end == absoluteIndex)
                        {
                            // We're at an edge.
                            if (span.Length > 0 &&
                                classifiedSpan.AcceptedCharacters == AcceptedCharactersInternal.None)
                            {
                                // Non-marker spans do not own the edges after it
                                continue;
                            }
                        }

                        index = i;
                        return true;
                    }
                }
            }

            return false;
        }

        private async Task<TextEdit[]> FormatProjectedDocument(
            RazorLanguageKind kind,
            Range range,
            string documentPath,
            FormattingOptions options)
        {
            var @params = new RazorDocumentRangeFormattingParams()
            {
                Kind = kind,
                ProjectedRange = range,
                HostDocumentFilePath = _filePathNormalizer.Normalize(documentPath),
                Options = options
            };

            var result = await _server.Value.Client.SendRequest<RazorDocumentRangeFormattingParams, RazorDocumentRangeFormattingResponse>(
                "razor/rangeFormatting", @params);

            return result.Edits;
        }

        private SourceLocation GetSourceLocation(SourceText sourceText, Position position)
        {
            var linePosition = new LinePosition((int)position.Line, (int)position.Character);
            var hostDocumentIndex = sourceText.Lines.GetPosition(linePosition);
            var location = new SourceLocation(hostDocumentIndex, (int)position.Line, (int)position.Character);
            return location;
        }

        internal class IndentationContext
        {
            public int Line { get; set; }

            public int IndentationLevel { get; set; }

            public int ExistingIndentation { get; set; }

            public override string ToString()
            {
                return $"Line: {Line}, Indentation Level: {IndentationLevel}, ExistingIndentation: {ExistingIndentation}";
            }
        }
    }
}
