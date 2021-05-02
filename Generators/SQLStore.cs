using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LunarModel.Generators
{
    public class SQLStore : Generator
    {
        private Dictionary<Entity, string> _tableNames = new Dictionary<Entity, string>();

        public override void Namespaces(Model model)
        {
            model.AppendLine("using System.Collections.Generic;");
            model.AppendLine("using System.Data.SQLite;");
            model.AppendLine("using System.Linq;");
            model.AppendLine("using System.Text;");
            model.AppendLine();
        }

        public override void Declarations(Model model, IEnumerable<Entity> entities)
        {
            model.AppendLine("\t\tprivate string connectionString;");
            model.AppendLine();

            model.AppendLine("\t\tpublic Database(string connectionString = \"Data Source = database.db\") {");
            model.AppendLine("\t\tthis.connectionString = connectionString;");
            model.AppendLine();

            foreach (var entity in entities)
            {
                var tableName = $"{entity.Name.CapLower().Pluralize()}";
                _tableNames[entity] = tableName;

                var fields = "";
                foreach (var field in entity.Fields)
                {
                    if (field.Flags.HasFlag(FieldFlags.Dynamic))
                    {
                        continue;
                    }

                    if (fields.Length > 0)
                    {
                        fields += ", ";
                    }

                    var decl = entity.Decls[field];

                    string type;

                    if (model.IsEnum(decl))
                    {
                        type = "integer";
                    }
                    else
                    switch (decl.Type.ToLower())
                    {
                            case "string": type = "text"; break;

                            case "decimal": type = "real"; break;

                            case "bool": type = "boolean"; break;

                            case "bytes": type = "blob"; break;

                            case "int32":
                            case "int64":
                                type = "integer"; break;

                            case "uint32":
                            case "uint64":
                                type = "UNSIGNED integer"; break;

                            default: throw new Exception("Unsupport sql type: " + decl.Type);
                    }

                    fields += $"\\\"{decl.Name}\\\" {type}";
                }

                string auto = entity.Parent == null ? " AUTOINCREMENT" : "";

                model.AppendLine($"\t\t\tif (!TableExists(\"{tableName}\"))");
                model.AppendLine("\t\t\t{");
                model.AppendLine($"\t\t\t\tCreateTable(\"{tableName}\", \"id integer PRIMARY KEY {auto}, {fields}\");");
                //                "name text, kind integer, about text"
                model.AppendLine("\t\t\t}");
                model.AppendLine();
            }
            model.AppendLine("\t\t}");

            model.AppendLine();
            var lines = File.ReadAllLines("Generators\\sql.txt");
            foreach (var line in lines)
            {
                model.AppendLine(line);
            }
        }

        public override void Aggregate(Model model, Entity source, Entity target, string fieldName, bool unique)
        {

            var varName = source.Name.CapLower();

            int limit = unique ? 1 : 0;

            if (unique)
            {
                model.AppendLine($"\t{source.Name} {varName} = null;");
            }
            else
            {
                model.AppendLine($"\tvar {varName.Pluralize()} = new List<{source.Name}>();");
            }

            model.TabIn();
            model.AppendLine($"ReadRows(\"{_tableNames[target]}\", {limit}, 0, \"{_tableNames[source]}\", \"{target.Name}ID\", \"{fieldName}\", {fieldName}, (reader) =>");
            model.AppendLine("{");

            if (unique)
            {
                model.AppendLine($"\t{varName} = new {source.Name}();");
            }
            else
            {
                model.AppendLine($"\tvar {varName} = new {source.Name}();");
            }

            int index = 0;
            ReadFieldsFromReader(model, source, varName, ref index);
            //ReadFieldsFromReader(model, target, varName, ref index, true);

            if (!unique)
            {
                model.AppendLine($"\t{varName.Pluralize()}.Add({varName});");
            }

            model.AppendLine("});");

            if (unique)
            {
                //model.AppendLine($"\t\t\treturn {_tableNames[source]}.Values.Where(x => x.{fieldName} == {fieldName}).FirstOrDefault();");
                model.AppendLine($"return {varName};");
            }
            else
            {
                //model.AppendLine($"\t\t\t//return {_tableNames[source]}.Values.Where(x => x.{fieldName} == {fieldName}).ToArray();");
                model.AppendLine($"return {varName.Pluralize()}.ToArray();");
            }

            model.TabOut();
        }

        private void ReadFieldsFromReader(Model model, Entity entity, string varName, ref int index, bool skipIndexOnly = false)
        {
            if (index == 0)
            {
                if (!skipIndexOnly) {
                    model.AppendLine($"\t{varName}.ID = reader.GetInt64(0);");
                }
                index++;
            }

            if (entity.Parent != null)
            {
                ReadFieldsFromReader(model, entity.Parent, varName, ref index, skipIndexOnly);
                index++; // skip ID for sub-table
            }

            var action = skipIndexOnly ? "Skipping" : "Reading";

            model.AppendLine($"\t// {action} {entity.Name} fields");
            foreach (var entry in entity.Fields)
            {
                if (entry.Flags.HasFlag(FieldFlags.Dynamic))
                {
                    continue;
                }

                if (!skipIndexOnly)
                {
                    var decl = entity.Decls[entry];

                    if (model.IsEnum(decl))
                    {
                        model.AppendLine($"\t{varName}.{decl.Name} = ({decl.Type})reader.GetInt32({index});");
                    }
                    else
                    {
                        string type;

                        switch (decl.Type.ToLower())
                        {
                            case "bool": type = "Boolean"; break;
                            default: type = decl.Type; break;
                        }

                        model.AppendLine($"\t{varName}.{decl.Name} = reader.Get{type}({index});");
                    }
                }

                index++;
            }
        }

        public override void List(Model model, Entity entity)
        {
            var varName = entity.Name.CapLower();

            model.AppendLine($"\tvar {varName.Pluralize()} = new List<{entity.Name}>();");

            string joinTable = entity.Parent != null ? $"\"{_tableNames[entity.Parent]}\"" : "null";
            string joinField = entity.Parent != null ? "\"id\"" : "null";


            model.TabIn();
            model.AppendLine($"ReadRows(\"{_tableNames[entity]}\", count, offset, {joinTable}, {joinField}, null, null, (reader) =>");
            model.AppendLine("{");
            model.AppendLine($"\tvar {varName} = new {entity.Name}();");
            int index = 0;
            ReadFieldsFromReader(model, entity, varName, ref index);
            model.AppendLine($"\t{varName.Pluralize()}.Add({varName});");
            model.AppendLine("});");

            model.AppendLine($"return {varName.Pluralize()}.ToArray();");

            model.TabOut();
        }

        public override void Count(Model model, Entity entity)
        {
            model.AppendLine($"\treturn (int)TableCount(\"{_tableNames[entity]}\");");
        }

        private void InitDecls(Model model, Entity entity, string varName, bool skipInternals)
        {
            if (entity.Parent != null)
            {
                InitDecls(model, entity.Parent, varName, true);

                model.AppendLine("dic.Clear();");
                model.AppendLine($"dic[\"id\"] = {varName}.ID;");
            }
            else
            {
                model.AppendLine("var dic = new Dictionary<string, object>();");
            }

            foreach (var field in entity.Fields)
            {
                if (skipInternals && field.Flags.HasFlag(FieldFlags.Internal))
                {
                    continue;
                }

                if (field.Flags.HasFlag(FieldFlags.Dynamic))
                {
                    continue;
                }

                var decl = entity.Decls[field];
                model.AppendLine($"{varName}.{decl.Name} = {decl.Name.CapLower()};");
                model.AppendLine($"dic[\"{decl.Name}\"] = {decl.Name.CapLower()};");
            }
        }

        public override void Create(Model model, Entity entity, string varName)
        {
            InitDecls(model, entity, varName, false);

            if (entity.Parent == null)
            {
                model.AppendLine($"{varName}.ID = InsertRow(\"{_tableNames[entity]}\", dic);");
            }
            else
            {
                model.AppendLine($"InsertRow(\"{_tableNames[entity]}\", dic);");
            }

            model.AppendLine($"return {varName};");
        }

        public override void Delete(Model model, Entity entity)
        {
            var varName = $"{entity.Name.CapLower()}ID";
            model.AppendLine($"\treturn DeleteRow(\"{_tableNames[entity]}\", \"{varName}\", {varName});");
        }

        public override void Find(Model model, Entity entity, string field)
        {
            if (field.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                model.AppendLine($"if ({field} == 0)");
                model.AppendLine("{");
                model.AppendLine("\t return null;");
                model.AppendLine("}");
            }
                
            var varName = entity.Name.CapLower();

            string joinTable = entity.Parent != null ? $"\"{_tableNames[entity.Parent]}\"" : "null";
            string joinField = entity.Parent != null ? "\"id\"" : "null";

            model.AppendLine($"{entity.Name} {varName} = null;");
            model.AppendLine($"ReadRow(\"{_tableNames[entity]}\", {joinTable}, {joinField}, \"{field}\", {field}, (reader) =>");
            model.AppendLine("{");
            model.TabIn();
            model.AppendLine($"{varName} = new {entity.Name}();");

            int index = 0;

            if (model.IsAbstract(entity))
            {
                var enumName = entity.Name + "Kind";

                model.AppendLine($"var ID = reader.GetInt64(0);");
                model.AppendLine($"{varName}.{enumName} = ({enumName})reader.GetInt32(1);");
                model.AppendLine($"switch ({varName}.{enumName})");
                model.AppendLine("{");
                model.TabIn();

                Enumerate kind = null;

                foreach (var entry in model.Enums)
                {
                    if (entry.Name == enumName)
                    {
                        kind = entry;
                    }
                }

                foreach (var entry in kind.Values)
                {
                    model.AppendLine($"case {enumName}.{entry}:");
                    model.AppendLine($"\t{varName} = Find{entry}ByID(ID);");
                    model.AppendLine("\tbreak;");
                    model.AppendLine();
                }

                model.TabOut();
                model.AppendLine("}");
            }
            else
            {
                ReadFieldsFromReader(model, entity, varName, ref index);
            }

            model.TabOut();
            model.AppendLine("});");

            model.AppendLine($"return {varName};");
        }


        public override void Edit(Model model, Entity entity, string varName)
        {
            var idName = varName + ".ID";

            model.AppendLine("object obj;");

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
                    model.AppendLine($"obj = value;");
                }
                else
                {
                    model.AppendLine($"{decl.ExportType()} {field.Name};");

                    if (model.Enums.Any(x => x.Name == decl.Type))
                    {
                        model.AppendLine($"if (!Enum.TryParse<{decl.Type}>(value, out {field.Name}))");
                    }
                    else
                    {
                        model.AppendLine($"if (!{decl.ExportType()}.TryParse(value, out {field.Name}))");
                    }

                    model.AppendLine("{");
                    model.AppendLine($"\treturn false;");
                    model.AppendLine("}");

                    string output = decl.Name;

                    model.AppendLine($"{varName}.{output} = {field.Name};");
                    model.AppendLine($"obj = {field.Name};");
                    if (decl.Name != field.Name)
                    {
                        model.AppendLine($"field = \"{decl.Name}\";");
                    }
                }

                model.AppendLine("break;");
                model.AppendLine();
                model.TabOut();
            }

            model.AppendLine("default:");
            model.AppendLine($"\treturn false;");

            model.AppendLine("}");
            model.TabOut();


            model.AppendLine($"\treturn UpdateRow(\"{_tableNames[entity]}\", {idName}, field, obj);");

        }
    }
}
