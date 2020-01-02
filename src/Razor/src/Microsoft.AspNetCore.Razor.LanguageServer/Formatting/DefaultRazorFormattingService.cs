// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
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
            var edits = await FormatProjectedDocument(RazorLanguageKind.Html, range, uri.AbsolutePath, options);

            return edits;
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
    }
}
