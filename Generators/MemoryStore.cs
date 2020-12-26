﻿using System;
using System.Collections.Generic;
using System.Text;

namespace LunarModel.Generators
{
    public class MemoryStore : Generator
    {
        private Dictionary<Entity, string> _mapNames = new Dictionary<Entity, string>();

        public override void Namespaces(StringBuilder sb)
        {
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine();
        }

        public override void Declarations(StringBuilder sb, IEnumerable<Entity> entities)
        {
            foreach (var entity in entities)
            {
                var decl = $"Dictionary<UInt64, {entity.Name}>";
                var mapName = $"_{entity.Name.CapLower()}s";
                _mapNames[entity] = mapName;
                sb.AppendLine($"\t\tprivate {decl} {mapName} = new {decl}();");
            }
        }

        public override void Aggregate(StringBuilder sb, Entity source, Entity target, string fieldName)
        {
            sb.AppendLine($"\t\t\treturn {_mapNames[source]}.Values.Where(x => x.{fieldName} == {fieldName}).ToArray();");
        }

        public override void List(StringBuilder sb, Entity entity)
        {
            sb.AppendLine($"\t\t\treturn {_mapNames[entity]}.Values.Skip(offset).Take(count).ToArray();");
        }

        public override void Get(StringBuilder sb, Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}";
            sb.AppendLine($"var {varName} = new {entity.Name}[IDs.Length];");
            sb.AppendLine($"for (int i=0; i<{varName}.Length; i++)");
            sb.AppendLine("{");
            sb.AppendLine("\tvar id = IDs[i];");
            sb.AppendLine($"\t{varName}[i] = {_mapNames[entity]}[id];");
            sb.AppendLine("}");
            sb.AppendLine($"return {varName};");
        }

        public override void Count(StringBuilder sb, Entity entity)
        {
            sb.AppendLine($"\t\t\treturn {_mapNames[entity]}.Count;");
        }

        public override void Create(StringBuilder sb, Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}";

            sb.AppendLine($"\t\t\tvar {varName} = new {entity.Name}();");
            foreach (var decl in entity.Decls)
            {
                sb.AppendLine($"\t\t\t{varName}.{decl.Value.Name} = {decl.Value.Name.CapLower()};");
            }
            sb.AppendLine($"\t\t\t{_mapNames[entity]}[{varName}.ID] = {varName};");
            sb.AppendLine($"\t\t\treturn {varName};");
        }

        public override void Delete(StringBuilder sb, Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}ID";
            sb.AppendLine($"\t\t\tif ({_mapNames[entity]}.ContainsKey({varName}))");
            sb.AppendLine("\t\t\t{");
            sb.AppendLine($"\t\t\t\t{_mapNames[entity]}.Remove({varName});");
            sb.AppendLine($"\t\t\t\treturn true;");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\treturn false;");
        }

        public override void Find(StringBuilder sb, Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}ID";
            sb.AppendLine($"\t\t\tif ({_mapNames[entity]}.ContainsKey({varName}))");
            sb.AppendLine("\t\t\t{");
            sb.AppendLine($"\t\t\t\treturn {_mapNames[entity]}[{varName}];");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\treturn null;");
        }
    }
}
