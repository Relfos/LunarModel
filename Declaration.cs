using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LunarModel
{
    public abstract class Declaration : Node
    {
        public readonly string Name;

        protected Declaration(string name)
        {
            Name = name;
        }
    }

    public class TypeDeclaration: Declaration
    {
        public TypeDeclaration(string name) : base(name)
        {

        }
    }

    public struct EnumEntry
    {
        public readonly string name;
        public readonly uint value;

        public EnumEntry(string name, uint value)
        {
            this.name = name;
            this.value = value;
        }
    }


    public class EnumDeclaration : TypeDeclaration
    {
        public Dictionary<string, uint> entryNames;

        public EnumDeclaration(string name, IEnumerable<EnumEntry> entries) : base(name)
        {
            entryNames = new Dictionary<string, uint>();

            foreach (var entry in entries)
            {
                if (entryNames.ContainsKey(entry.name))
                {
                    throw new CompilerException($"Duplicated entry {entry.value} in enum {name}");
                }

                entryNames[entry.name] = entry.value;
            }
        }
    }

    public struct EntityField
    {
        public readonly string name;
        public readonly TypeDeclaration type;
        public readonly FieldFlags flags;

        public EntityField(string name, TypeDeclaration type, FieldFlags flags)
        {
            this.name = name;
            this.type = type;
            this.flags = flags;
        }
    }

    public class EntityDeclaration : TypeDeclaration
    {
        public string Parent;
        public EntityField[] fields;

        public EntityDeclaration(string name, string parent, IEnumerable<EntityField> fields) : base(name)
        {
            this.Parent = parent;
            this.fields = fields.ToArray();
        }
    }

}
