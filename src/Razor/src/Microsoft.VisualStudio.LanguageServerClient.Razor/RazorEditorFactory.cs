using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Package;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.LanguageServerClient.Razor
{
    [Guid(EditorFactoryGuidString)]
    public class RazorEditorFactory : EditorFactory
    {
        private const string EditorFactoryGuidString = "3dfdce9e-1799-4372-8aa6-d8e65182fdfc";
        private const string RazorLSPEditor_EnvironmentVariable = "Razor.LSP.Editor";

        public RazorEditorFactory(AsyncPackage package) : base(package)
        {
        }

        public override int CreateEditorInstance(uint createDocFlags, string moniker, string physicalView, IVsHierarchy pHier, uint itemid, IntPtr existingDocData, out IntPtr docView, out IntPtr docData, out string editorCaption, out Guid cmdUI, out int cancelled)
        {
            var lspRazorEnabledString = Environment.GetEnvironmentVariable(RazorLSPEditor_EnvironmentVariable);
            if (!bool.TryParse(lspRazorEnabledString, out var lspRazorEnabled))
            {
                lspRazorEnabled = false;
            }

            if (!lspRazorEnabled)
            {
                docView = default;
                docData = default;
                editorCaption = null;
                cmdUI = default;
                cancelled = 0;

                // Razor LSP is not enabled, allow another editor to handle this document
                return VSConstants.VS_E_UNSUPPORTEDFORMAT;
            }

            return base.CreateEditorInstance(createDocFlags, moniker, physicalView, pHier, itemid, existingDocData, out docView, out docData, out editorCaption, out cmdUI, out cancelled);
        }
    }
}
