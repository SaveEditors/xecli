using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("Xbox 360 DLL")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("1337 Yiffy Pops")]
[assembly: AssemblyProduct("Xbox 360 DLL")]
[assembly: AssemblyCopyright("Copyright © DJ Shepherd 2009")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("906fc2b9-6ddc-45de-9a63-a9de9c7657ac")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version 
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers 
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.0.0.42")]
[assembly: AssemblyFileVersion("1.0.0.42")]


// XeCLI modification: retain the public identity helper while removing its
// obsolete network-based privilege check. Package creation must be offline.
namespace System{
    /// <summary></summary>
    [System.Diagnostics.DebuggerStepThrough]
    public static class DLLIdentify {
        /// <summary></summary>
        public static string Bish { get { return "X360 library maintained under GNU GPL version 3."; } }

        internal static void PrivilegeCheck(object sender)
        {
            // Intentionally offline. The original callback contacted a retired
            // service and could terminate the calling thread.
        }
    }
}
