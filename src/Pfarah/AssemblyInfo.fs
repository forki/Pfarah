﻿namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Pfarah")>]
[<assembly: AssemblyProductAttribute("Pfarah")>]
[<assembly: AssemblyDescriptionAttribute("Parses files generated by the Clausewitz engine")>]
[<assembly: AssemblyVersionAttribute("0.3.1")>]
[<assembly: AssemblyFileVersionAttribute("0.3.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.3.1"
