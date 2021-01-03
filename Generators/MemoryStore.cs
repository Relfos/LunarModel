using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LunarModel.Generators
{
    public class MemoryStore : Generator
    {
        private Dictionary<Entity, string> _mapNames = new Dictionary<Entity, string>();

        public override void Namespaces(Model model)
        {
            model.AppendLine("using System.Collections.Generic;");
            model.AppendLine("using System.Linq;");
            model.AppendLine();
        }

        public override void Declarations(Model model, IEnumerable<Entity> entities)
        {
            foreach (var entity in entities)
            {
                var decl = $"Dictionary<UInt64, {entity.Name}>";
                var mapName = $"_{entity.Name.CapLower()}s";
                _mapNames[entity] = mapName;
                model.AppendLine($"\t\tprivate {decl} {mapName} = new {decl}();");
            }
        }

        public override void Aggregate(Model model, Entity source, Entity target, string fieldName)
        {
            model.AppendLine($"\t\t\treturn {_mapNames[source]}.Values.Where(x => x.{fieldName} == {fieldName}).ToArray();");
        }

        public override void List(Model model, Entity entity)
        {
            model.AppendLine($"\t\t\treturn {_mapNames[entity]}.Values.Skip(offset).Take(count).ToArray();");
        }

        public override void Count(Model model, Entity entity)
        {
            model.AppendLine($"return {_mapNames[entity]}.Count;");
        }

        private void InitDecls(Model model,Entity entity, string varName, bool skipInternals)
        {
            if (entity.Parent != null)
            {
                InitDecls(model, entity.Parent, varName, true);
            }

            foreach (var field in entity.Fields)
            {
                if (skipInternals && field.Flags.HasFlag(FieldFlags.Internal))
                {
                    continue;
                }

                var decl = entity.Decls[field];
                model.AppendLine($"\t\t\t{varName}.{decl.Name} = {decl.Name.CapLower()};");
            }
        }

        public override void Create(Model model,Entity entity, string varName)
        {
            if (entity.Parent == null)
            {
                model.AppendLine($"\t\t\t{varName}.ID = (UInt64)({_mapNames[entity]}.Count + 1);");
            }
            InitDecls(model, entity, varName, false);
            model.AppendLine($"\t\t\t{_mapNames[entity]}[{varName}.ID] = {varName};");
            model.AppendLine($"\t\t\treturn {varName};");
        }

        public override void Delete(Model model,Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}ID";
            model.AppendLine($"\t\t\tif ({_mapNames[entity]}.ContainsKey({varName}))");
            model.AppendLine("\t\t\t{");
            model.AppendLine($"\t\t\t\t{_mapNames[entity]}.Remove({varName});");
            model.AppendLine($"\t\t\t\treturn true;");
            model.AppendLine("\t\t\t}");
            model.AppendLine("\t\t\treturn false;");
        }

        public override void Find(Model model,Entity entity, string field)
        {
            if (field.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                model.AppendLine($"if ({field} == 0)");
                model.AppendLine("{");
                model.AppendLine("\t return null;");
                model.AppendLine("}");

                model.AppendLine($"\t\t\tif ({_mapNames[entity]}.ContainsKey({field}))");
                model.AppendLine("\t\t\t{");
                model.AppendLine($"\t\t\t\treturn {_mapNames[entity]}[{field}];");
                model.AppendLine("\t\t\t}");
                model.AppendLine("\t\t\treturn null;");
            }
            else
            {
                model.AppendLine($"\t\t\t\treturn {_mapNames[entity]}.Values.FirstOrDefault(x => x.{field} == {field});");
            }
        }


        public override void Edit(Model model, Entity entity, string idName)
        {
            var varName = entity.Name.CapLower();
            model.AppendLine($"var {varName} = this.Find{entity.Name}ByID({idName});");

            model.AppendLine($"switch(field)");
            model.AppendLine("{");

            model.TabIn();
            foreach (var field in entity.Fields)
            {
                if (!field.Flags.HasFlag(FieldFlags.Editable))
                {
                    continue;
                }

                var decl = entity.Decls[field];

                model.AppendLine("case \"" + field.Name + "\":");
                model.TabIn();

                if (decl.Type.Equals("string", StringComparison.OrdinalIgnoreCase))
                {
                    model.AppendLine($"{varName}.{field.Name} = value;");
                }
                else
                {
                    model.AppendLine($"{decl.Type} {field.Name};");

                    if (model.Enums.Any(x => x.Name == decl.Type))
                    {
                        model.AppendLine($"if (!Enum.TryParse<{decl.Type}>(value, out {field.Name}))");
                    }
                    else
                    {
                        model.AppendLine($"if (!{decl.Type}.TryParse(value, out {field.Name}))");
                    }
                    model.AppendLine("{");
                    model.AppendLine($"\treturn false;");
                    model.AppendLine("}");

                    model.AppendLine($"{varName}.{field.Name} = {field.Name};");
                }

                model.AppendLine("return true;");
                model.AppendLine();
                model.TabOut();
            }

            model.AppendLine("default:");
            model.AppendLine($"\treturn false;");

            model.TabOut();

            model.AppendLine("}");

        }
    }
}
