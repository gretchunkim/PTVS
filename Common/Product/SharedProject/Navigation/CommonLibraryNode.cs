// Visual Studio Shared Project
// Copyright(c) Microsoft Corporation
// All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the License); you may not use
// this file except in compliance with the License. You may obtain a copy of the
// License at http://www.apache.org/licenses/LICENSE-2.0
//
// THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS
// OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY
// IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
//
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools.Parsing;
using ErrorHandler = Microsoft.VisualStudio.ErrorHandler;
using VSConstants = Microsoft.VisualStudio.VSConstants;

namespace Microsoft.VisualStudioTools.Navigation {

    /// <summary>
    /// This is a specialized version of the LibraryNode that handles the dynamic languages
    /// items. The main difference from the generic one is that it supports navigation
    /// to the location inside the source code where the element is defined.
    /// </summary>
    internal abstract class CommonLibraryNode : LibraryNode {
        private readonly IVsHierarchy _ownerHierarchy;
        private readonly uint _fileId;
        private readonly TextSpan _sourceSpan;
        private string _fileMoniker;

        protected CommonLibraryNode(LibraryNode parent, string name, string fullName, IVsHierarchy hierarchy, uint itemId, LibraryNodeType type, IList<LibraryNode> children = null) :
            base(parent, name, fullName, type, children: children) {
            _ownerHierarchy = hierarchy;
            _fileId = itemId;

            // Now check if we have all the information to navigate to the source location.
#if FALSE
            if ((null != _ownerHierarchy) && (VSConstants.VSITEMID_NIL != _fileId)) {
                if ((SourceLocation.Invalid != scope.Start) && (SourceLocation.Invalid != scope.End)) {
                    _sourceSpan = new TextSpan();
                    _sourceSpan.iStartIndex = scope.Start.Column - 1;
                    if (scope.Start.Line > 0) {
                        _sourceSpan.iStartLine = scope.Start.Line - 1;
                    }
                    _sourceSpan.iEndIndex = scope.End.Column;
                    if (scope.End.Line > 0) {
                        _sourceSpan.iEndLine = scope.End.Line - 1;
                    }
                    CanGoToSource = true;
                }
            }
#endif
     
        }
      
        public TextSpan SourceSpan {
            get {
                return _sourceSpan;
            }
        }

        protected CommonLibraryNode(CommonLibraryNode node) :
            base(node) {
            _fileId = node._fileId;
            _ownerHierarchy = node._ownerHierarchy;
            _fileMoniker = node._fileMoniker;
            _sourceSpan = node._sourceSpan;
        }

        protected CommonLibraryNode(CommonLibraryNode node, string newFullName) :
            base(node, newFullName) {
            _fileId = node._fileId;
            _ownerHierarchy = node._ownerHierarchy;
            _fileMoniker = node._fileMoniker;
            _sourceSpan = node._sourceSpan;
        }

        public override uint CategoryField(LIB_CATEGORY category) {
            switch (category) {
                case (LIB_CATEGORY)_LIB_CATEGORY2.LC_MEMBERINHERITANCE:
                    if (NodeType == LibraryNodeType.Members || NodeType == LibraryNodeType.Definitions) {
                        return (uint)_LIBCAT_MEMBERINHERITANCE.LCMI_IMMEDIATE;
                    }
                    break;
            }
            return base.CategoryField(category);
        }

        public override void GotoSource(VSOBJGOTOSRCTYPE gotoType) {
            // We do not support the "Goto Reference"
            if (VSOBJGOTOSRCTYPE.GS_REFERENCE == gotoType) {
                return;
            }

            // There is no difference between definition and declaration, so here we
            // don't check for the other flags.

            IVsWindowFrame frame = null;
            IntPtr documentData = FindDocDataFromRDT();
            try {
                // Now we can try to open the editor. We assume that the owner hierarchy is
                // a project and we want to use its OpenItem method.
                IVsProject3 project = _ownerHierarchy as IVsProject3;
                if (null == project) {
                    return;
                }
                Guid viewGuid = VSConstants.LOGVIEWID_Code;
                ErrorHandler.ThrowOnFailure(project.OpenItem(_fileId, ref viewGuid, documentData, out frame));
            } finally {
                if (IntPtr.Zero != documentData) {
                    Marshal.Release(documentData);
                    documentData = IntPtr.Zero;
                }
            }

            // Make sure that the document window is visible.
            ErrorHandler.ThrowOnFailure(frame.Show());

            // Get the code window from the window frame.
            object docView;
            ErrorHandler.ThrowOnFailure(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView));
            IVsCodeWindow codeWindow = docView as IVsCodeWindow;
            if (null == codeWindow) {
                object docData;
                ErrorHandler.ThrowOnFailure(frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out docData));
                codeWindow = docData as IVsCodeWindow;
                if (null == codeWindow) {
                    return;
                }
            }

            // Get the primary view from the code window.
            IVsTextView textView;
            ErrorHandler.ThrowOnFailure(codeWindow.GetPrimaryView(out textView));

            // Set the cursor at the beginning of the declaration.
            ErrorHandler.ThrowOnFailure(textView.SetCaretPos(_sourceSpan.iStartLine, _sourceSpan.iStartIndex));
            // Make sure that the text is visible.
            TextSpan visibleSpan = new TextSpan();
            visibleSpan.iStartLine = _sourceSpan.iStartLine;
            visibleSpan.iStartIndex = _sourceSpan.iStartIndex;
            visibleSpan.iEndLine = _sourceSpan.iStartLine;
            visibleSpan.iEndIndex = _sourceSpan.iStartIndex + 1;
            ErrorHandler.ThrowOnFailure(textView.EnsureSpanVisible(visibleSpan));

        }

        public override void SourceItems(out IVsHierarchy hierarchy, out uint itemId, out uint itemsCount) {
            hierarchy = _ownerHierarchy;
            itemId = _fileId;
            itemsCount = 1;
        }

        public override string UniqueName {
            get {
                if (string.IsNullOrEmpty(_fileMoniker)) {
                    ErrorHandler.ThrowOnFailure(_ownerHierarchy.GetCanonicalName(_fileId, out _fileMoniker));
                }
                return string.Format(CultureInfo.InvariantCulture, "{0}/{1}", _fileMoniker, Name);
            }
        }

        private IntPtr FindDocDataFromRDT() {
            // Get a reference to the RDT.
            IVsRunningDocumentTable rdt = Package.GetGlobalService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (null == rdt) {
                return IntPtr.Zero;
            }

            // Get the enumeration of the running documents.
            IEnumRunningDocuments documents;
            ErrorHandler.ThrowOnFailure(rdt.GetRunningDocumentsEnum(out documents));

            IntPtr documentData = IntPtr.Zero;
            uint[] docCookie = new uint[1];
            uint fetched;
            while ((VSConstants.S_OK == documents.Next(1, docCookie, out fetched)) && (1 == fetched)) {
                uint flags;
                uint editLocks;
                uint readLocks;
                string moniker;
                IVsHierarchy docHierarchy;
                uint docId;
                IntPtr docData = IntPtr.Zero;
                try {
                    ErrorHandler.ThrowOnFailure(
                        rdt.GetDocumentInfo(docCookie[0], out flags, out readLocks, out editLocks, out moniker, out docHierarchy, out docId, out docData));
                    // Check if this document is the one we are looking for.
                    if ((docId == _fileId) && (_ownerHierarchy.Equals(docHierarchy))) {
                        documentData = docData;
                        docData = IntPtr.Zero;
                        break;
                    }
                } finally {
                    if (IntPtr.Zero != docData) {
                        Marshal.Release(docData);
                    }
                }
            }

            return documentData;
        }
    }
}
