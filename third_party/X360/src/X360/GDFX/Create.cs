// X360 library, originally by DJ Shepherd.
// Distributed and modified under GNU GPL version 3; see README.md and LICENSE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Drawing;
using X360.IO;
using X360.Other;

namespace X360.GDFX
{
    internal class CGDFEntry
    {
        [CompilerGenerated]
        public string gdfpath = "";

        public string parent
        {
            get
            {
                int idx = gdfpath.LastIndexOf('/');
                if (idx == -1)
                    return "";
                return gdfpath.Substring(0, idx);
            }
        }

        public string name { get { return gdfpath.xExtractName(); } }

        public CGDFEntry(string xgdfpath) { gdfpath = xgdfpath; }
    }

    internal class CGDFFile : CGDFEntry
    {
        [CompilerGenerated]
        public string filelocale = "";

        public CGDFFile(string xgdfpath, string xfilelocale) :
            base(xgdfpath) { filelocale = xfilelocale; }
    }

    internal class CreateGDF
    {
        [CompilerGenerated]
        internal List<CGDFFile> xFiles = new List<CGDFFile>();
        [CompilerGenerated]
        internal List<CGDFEntry> xFolders = new List<CGDFEntry>();

        public CreateGDF() { }

        bool parentavailable(string path)
        {
            int idx = path.LastIndexOf('/');
            if (idx == -1)
                return true;
            string par = path.Substring(0, idx);
            foreach (CGDFEntry x in xFolders)
            {
                if (x.parent.ToLower() == par.ToLower())
                    return true;
            }
            return false;
        }

        public bool AddFile(string GDFFilePath, string FileLocation)
        {
            GDFFilePath = GDFFilePath.xExtractLegitPath();
            if (!parentavailable(GDFFilePath))
                return false;
            xFiles.Add(new CGDFFile(GDFFilePath, FileLocation));
            return true;
        }

        public bool AddFolder(string GDFFolderPath)
        {
            GDFFolderPath = GDFFolderPath.xExtractLegitPath();
            if (!parentavailable(GDFFolderPath))
                return false;
            xFolders.Add(new CGDFEntry(GDFFolderPath));
            return true;
        }

        public bool DeleteFile(string GDFPath)
        {
            GDFPath = GDFPath.xExtractLegitPath();
            for (int i = 0; i < xFiles.Count; i++)
            {
                if (xFiles[i].gdfpath.ToLower() == GDFPath.ToLower())
                {
                    xFiles.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public bool DeleteFolder(string GDFFolder)
        {
            GDFFolder = GDFFolder.xExtractLegitPath();
            for (int i = 0; i < xFolders.Count; i++)
            {
                if (xFolders[i].parent.ToLower() == GDFFolder.ToLower())
                    DeleteFolder(xFolders[i].gdfpath);
            }
            for (int i = 0; i < xFiles.Count; i++)
            {
                if (xFiles[i].parent.ToLower() == GDFFolder.ToLower())
                    xFiles.RemoveAt(i--);
            }
            return true;
        }
    }
}
