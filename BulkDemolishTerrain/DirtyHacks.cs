namespace System.Runtime.Versioning
{
    //
    // Summary:
    //     Identifies the version of the .NET Framework that a particular assembly was compiled
    //     against.
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class TargetFrameworkAttribute : Attribute
    {
        //
        // Summary:
        //     Initializes an instance of the System.Runtime.Versioning.TargetFrameworkAttribute
        //     class by specifying the .NET Framework version against which an assembly was
        //     built.
        //
        // Parameters:
        //   frameworkName:
        //     The version of the .NET Framework against which the assembly was built.
        //
        // Exceptions:
        //   T:System.ArgumentNullException:
        //     frameworkName is null.
        public TargetFrameworkAttribute(string frameworkName)
        {
            FrameworkName = FrameworkDisplayName = frameworkName;
        }

        //
        // Summary:
        //     Gets the name of the .NET Framework version against which a particular assembly
        //     was compiled.
        //
        // Returns:
        //     The name of the .NET Framework version with which the assembly was compiled.
        public string FrameworkName { get; }
        //
        // Summary:
        //     Gets the display name of the .NET Framework version against which an assembly
        //     was built.
        //
        // Returns:
        //     The display name of the .NET Framework version.
        public string FrameworkDisplayName { get; set; }
    }
}
