using System;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class BepInPlugin : Attribute
    {
        public string Guid { get; }
        public string Name { get; }
        public Version Version { get; }

        public BepInPlugin(string guid, string name, Version version)
        {
            Guid = guid;
            Name = name;
            Version = version;
        }

        public BepInPlugin(string guid, string name, string version)
            : this(guid, name, new Version(version)) { }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class BepInProcess : Attribute
    {
        public string[] ProcessNames { get; }

        public BepInProcess(params string[] processNames)
        {
            ProcessNames = processNames;
        }
    }
}
