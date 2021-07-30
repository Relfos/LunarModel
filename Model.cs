using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using LunarLabs.Parser;

namespace LunarModel
{
    [Flags]
    public enum FieldFlags
    {
        None = 0,
        Editable = 1,
        Hidden = 2,
        Searchable = 4,
        Internal = 8,
        Unique = 16,
        Dynamic = 32,
    }

    public class Field
    {
        public string Name;
        public string Type;
        public FieldFlags Flags;

        public Field(string name, string type, FieldFlags flags)
        {
            Name = name;
            Type = type;
            Flags = flags;
        }

        public override string ToString()
        {
            return $"{Name}:{Type}";
        }
    }

    public struct FieldDecl
    {
        public readonly string Name;
        public readonly string Type;

        public FieldDecl(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string ExportType()
        {
            switch (Type.ToLower())
            {
                case "bool": return "bool"; 
                case "bytes": return "byte[]";
                default: return Type;
            }
        }
    }

    public class Entity
    {
        public Entity Parent;
        public string Name;
        public List<Field> Fields;
        public string KindEnumName;
        public List<Entity> SubEntities = new List<Entity>();

        public Dictionary<Field, FieldDecl> Decls = new Dictionary<Field, FieldDecl>();

        public Entity(string name, Entity parent, IEnumerable<Field> fields)
        {
            Name = name;
            Parent = parent;
            Fields = fields.ToList();
        }

        public bool HasEditableFields()
        {
            if (Fields.Any(x => x.Flags.HasFlag(FieldFlags.Editable)))
            {
                return true;
            }

            if (Parent != null)
            {
                return Parent.HasEditableFields();
            }

            return false;
        }
    }

    public class Enumerate
    {
        public string Name;
        public string[] Values;

        public Enumerate(string name, IEnumerable<string> values)
        {
            Name = name;
            Values = values.ToArray();
        }
    }

    public abstract class Generator
    {
        public abstract void Namespaces(Model model);
        public abstract void Declarations(Model model, IEnumerable<Entity> entities);
        public abstract void Create(Model model, Entity entity, string varName);
        public abstract void Delete(Model model, Entity entity);
        public abstract void Find(Model model, Entity entity, string field);        
        public abstract void List(Model model, Entity entity);
        public abstract void Count(Model model, Entity entity);
        public abstract void Aggregate(Model model, Entity source, Entity target, string fieldName, bool unique);
        public abstract void Edit(Model model, Entity entity, string varName);
    }

    public class Model
    {
        public readonly string Name;
        public readonly Generator generator;

        public List<Enumerate> Enums = new List<Enumerate>();
        public List<Entity> Entities = new List<Entity>();

        private string serializationClass;
        private string clientClass;
        private string databaseClass;
        private string serverClass;

        StringBuilder _sb = new StringBuilder();
        int _tabs = 0;

        public Model(string name, Generator generator)
        {
            this.Name = name;
            this.generator = generator;
        }

        private IEnumerable<Entity> GetReferences(Entity entity)
        {
            var result = new List<Entity>();

            foreach (var other in Entities)
            {
                if (other.Fields.Any(x => x.Type == entity.Name))
                {
                    if (other.Fields.Any(x => x.Name == entity.Name))
                    {
                        result.Add(other);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Skipped possible reference to {entity.Name} in {other.Name}");
                    }
                }
            }

            return result;
        }

        private bool IsUniqueReference(Entity entity, Entity reference)
        {
            return reference.Fields.Any(x => x.Type == entity.Name && x.Flags.HasFlag(FieldFlags.Unique));
        }

        internal bool IsAbstract(Entity entity)
        {
            return (Entities.Any(x => x.Parent == entity));
        }

        internal bool IsEnum(FieldDecl decl)
        {
            return Enums.Any(x => x.Name == decl.Type);
        }

        internal bool IsEntity(FieldDecl decl)
        {
            return Entities.Any(x => x.Name == decl.Type);
        }

        public void Generate()
        {
            this.serializationClass = $"{this.Name}Serialization";
            this.databaseClass = "Database";
            this.clientClass = "Client";
            this.serverClass = $"Base{Name}Server";

            GenerateClasses();
            GenerateSerialization();
            GenerateDatabase();
            GenerateClient();
            GenerateServer();
        }

        private void BeginDoc()
        {
            _sb.Clear();
            _tabs = 0;
        }

        private void EndDoc(string fileName)
        {
            var path = Directory.GetCurrentDirectory();
            fileName = path + Path.DirectorySeparatorChar+ fileName;
            Console.WriteLine("Exported " + fileName);
            File.WriteAllText(fileName, _sb.ToString());
        }

        private void BeginRegion(string name)
        {
            AppendLine($"#region {name.ToUpper()}");
        }

        private void EndRegion()
        {
            AppendLine($"#endregion");
        }

        public void TabIn()
        {
            _tabs++;
        }

        public void TabOut()
        {
            _tabs--;
        }

        private void Append(string text)
        {
            for (int i = 0; i < _tabs; i++)
                _sb.Append('\t');

            _sb.Append(text);
        }

        public void AppendLine(string text = "")
        {
            Append(text + "\n");
        }

        private void GenerateClasses()
        {
            BeginDoc();

            AppendLine("using System;");
            AppendLine();

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            foreach (var enumm in this.Enums)
            {
                AppendLine();
                TabIn();
                AppendLine("public enum " + enumm.Name);
                AppendLine("{");
                TabIn();
                foreach (var entry in enumm.Values)
                {
                    AppendLine("\t\t" + entry + ", ");
                }
                TabOut();
                AppendLine("\t}");
                TabOut();
            }

            AppendLine();
            TabIn();
            AppendLine("public class Entity");
            AppendLine("{");
            TabIn();
            AppendLine("public Int64 ID { get; internal set;} ");
            AppendLine($"public static implicit operator Int64(Entity obj) => obj.ID;");
            TabOut();
            AppendLine("}");
            TabOut();

            foreach (var entity in this.Entities)
            {
                TabIn();

                var isSealed = !Entities.Any(x => x.Parent == entity);

                AppendLine();
                AppendLine($"public {(isSealed?"sealed ":"")}class {entity.Name} : " + (entity.Parent != null ? entity.Parent.Name : "Entity"));
                AppendLine("{");

                if (IsAbstract(entity))
                {
                    var enumName = $"{entity.Name}Kind";
                    entity.KindEnumName = enumName;
                    entity.Fields.Insert(0, new Field(enumName, enumName, FieldFlags.Internal));
                }
                else
                if (entity.Parent != null)
                {
                    entity.Parent.SubEntities.Add(entity);
                }

                foreach (var field in entity.Fields)
                {
                    string name, type;

                    bool isID = Entities.Any(x => x.Name == field.Type);

                    if (isID)
                    {
                        name = field.Name.CapLower() + "ID";
                        type = "Int64";
                    }
                    else
                    {
                        type = field.Type;
                        name = field.Name;
                    }

                    var decl = new FieldDecl(name, type);
                    entity.Decls[field] = decl;

                    string accessor = "";

                    if (!field.Flags.HasFlag(FieldFlags.Dynamic))
                    {
                        accessor = "internal ";
                    }

                    if (!isID)
                    {
                        type = decl.ExportType();
                    }

                    AppendLine($"\tpublic {type} {name} " + " { get; "+ accessor + "set;}");
                }

                AppendLine("}");
                TabOut();
            }

            AppendLine("}");

            EndDoc("Model.cs");
        }

        private void AppendToNodeFields(Entity entity, string varName)
        {
            if (entity.Parent != null)
            {
                AppendToNodeFields(entity.Parent, varName);
            }

            foreach (var field in entity.Fields)
            {
                if (field.Flags.HasFlag(FieldFlags.Hidden))
                {
                    continue;
                }

                var decl = entity.Decls[field];
                AppendLine($"node.AddField(\"{decl.Name}\", {varName}.{decl.Name});");
            }
        }

        private void AppendFromNodeFields(Entity entity, string varName)
        {
            if (entity.Parent != null)
            {
                AppendFromNodeFields(entity.Parent, varName);
            }

            foreach (var field in entity.Fields)
            {
                if (field.Flags.HasFlag(FieldFlags.Hidden))
                {
                    continue;
                }

                var decl = entity.Decls[field];

                string getStr;

                if (IsEnum(decl))
                {
                    getStr = $"Enum<{decl.Type.CapUpper()}>";
                }
                else
                {
                    getStr = decl.Type.CapUpper();
                }

                AppendLine($"{varName}.{decl.Name} = node.Get{getStr}(\"{decl.Name}\");");
            }
        }

        private void GenerateSerialization()
        {
            BeginDoc();

            AppendLine("using System;");
            AppendLine("using LunarLabs.Parser;");
            AppendLine();

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            TabIn();
            AppendLine($"public class {serializationClass}");
            AppendLine("{");

            AppendLine("public static DataNode ToArray<T>(string name, T[] items, Func<T, DataNode> convert)");
            AppendLine("{");
            TabIn();
                AppendLine("var result = DataNode.CreateArray(name);");
                AppendLine("foreach (T item in items)");
                AppendLine("{");
                TabIn();
                    AppendLine("var child = convert(item);");
                    AppendLine("result.AddNode(child);");
                TabOut();

                AppendLine("}");
                AppendLine("return result;");
                TabOut();
            AppendLine("}");

            foreach (var entity in this.Entities)
            {
                AppendLine();
                TabIn();

                AppendLine($"public static {entity.Name} {entity.Name}FromNode(DataNode node)");
                AppendLine("{");
                var varName = entity.Name.CapLower();
                TabIn();

                AppendLine($"var id = node.GetInt64(\"id\");");
                AppendLine($"if (id == 0)");
                AppendLine("{");
                TabIn();
                AppendLine($"return null;");
                TabOut();
                AppendLine("}");
                AppendLine();

                if (IsAbstract(entity))
                {
                    AppendLine($"var kind = node.GetEnum<{entity.KindEnumName}>(\"{entity.KindEnumName}\");");
                    AppendLine($"switch (kind)");
                    AppendLine("{");
                    TabIn();
                    foreach (var child in entity.SubEntities)
                    {
                        AppendLine($"case {entity.KindEnumName}.{child.Name}:");
                        TabIn();
                        AppendLine($"return {child.Name}FromNode(node);");
                        AppendLine();
                        TabOut();
                    }

                    AppendLine($"default:");
                    TabIn();
                    AppendLine($"throw new Exception(\"Unknown {entity.Name} kind\");");
                    TabOut();

                    TabOut();
                    AppendLine("}");
                }
                else
                {
                    AppendLine($"var {varName} = new {entity.Name}();");
                    AppendLine($"{varName}.ID = id;");

                    AppendFromNodeFields(entity, varName);
                    AppendLine($"return {varName};");
                }

                TabOut();
                AppendLine("}");

                AppendLine();
                AppendLine($"public static DataNode {entity.Name}ToNode({entity.Name} {varName})");
                AppendLine("{");

                TabIn();

                AppendLine($"if ({varName} == null)");
                AppendLine("{");
                TabIn();
                AppendLine($"var temp = DataNode.CreateObject(\"{entity.Name}\");");
                AppendLine($"temp .AddField(\"id\", 0);");
                AppendLine($"return temp ;");
                TabOut();
                AppendLine("}");
                AppendLine();

                if (IsAbstract(entity))
                {
                    AppendLine($"switch ({varName}.{entity.KindEnumName})");
                    AppendLine("{");
                    TabIn();
                    foreach (var child in entity.SubEntities)
                    {
                        AppendLine($"case {entity.KindEnumName}.{child.Name}:");
                        TabIn();
                        AppendLine($"return {child.Name}ToNode(({child.Name}){varName});");
                        AppendLine();
                        TabOut();
                    }

                    AppendLine($"default:");
                    TabIn();
                    AppendLine($"throw new Exception(\"Unknown {entity.Name} kind\");");
                    TabOut();

                    TabOut();
                    AppendLine("}");
                }
                else
                {
                    AppendLine($"var node = DataNode.CreateObject(\"{entity.Name}\");");
                    AppendLine($"node.AddField(\"id\", {varName}.ID);");

                    AppendToNodeFields(entity, varName);
                    AppendLine($"return node;");
                }

                TabOut();
                AppendLine("}");

                TabOut();
            }

            AppendLine("}");
            TabOut();
            AppendLine("}");

            EndDoc($"Serialization.cs");
        }

        private string GetConstructorFields(Entity entity, bool withTypes, bool skip)
        {
            var result = "";

            int declIndex = 0;
            foreach (var field in entity.Fields)
            {
                if (skip && field.Flags.HasFlag(FieldFlags.Internal))
                {
                    continue;
                }

                if (field.Flags.HasFlag(FieldFlags.Dynamic))
                {
                    continue;
                }

                var decl = entity.Decls[field];

                if (declIndex > 0)
                {
                    result += ", ";
                }

                if (withTypes)
                {
                    result += $"{decl.ExportType()} ";
                }

                result += $"{decl.Name.CapLower()}";

                declIndex++;
            }

            return result;
        }

        private void GenerateDatabase()
        {
            BeginDoc();

            AppendLine("using System;");
            generator.Namespaces(this);

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            AppendLine("");
            AppendLine($"\tpublic class {databaseClass}");
            TabIn();
            AppendLine("{");
            generator.Declarations(this, this.Entities);

            foreach (var entity in this.Entities)
            {
                AppendLine();

                var visibility = IsAbstract(entity) ? "private" : "public";

                TabIn();

                var newFields = GetConstructorFields(entity, true, !IsAbstract(entity)).Trim();

                if (entity.Parent != null)
                {
                    var parentFields = GetConstructorFields(entity.Parent, true, true);

                    if (newFields.Length == 0)
                    {
                        newFields = parentFields;
                    }
                    else
                    {
                        newFields = parentFields +", " + newFields;
                    }
                }

                var varName = $"{entity.Name.CapLower()}";

                string constraint = IsAbstract(entity) ? $" where T: {entity.Name}" : "";

                AppendLine($"{visibility} {(constraint.Length > 0 ? "T" : entity.Name)} Create{entity.Name}{(constraint.Length>0?"<T>":"")}({newFields}){constraint}");
                AppendLine("{");
                TabIn();
                if (constraint.Length > 0)
                {
                    AppendLine($"var {varName} = (T)Activator.CreateInstance(typeof(T));");
                }
                else
                if (entity.Parent != null)
                {
                    var initValues = GetConstructorFields(entity.Parent, false, true);
                    AppendLine($"var {varName} = Create{entity.Parent.Name}<{entity.Name}>({entity.Parent.Name}Kind.{entity.Name}, {initValues});");
                }
                else
                {
                    AppendLine($"var {varName} = new {entity.Name}();");
                }
                generator.Create(this, entity, varName);
                TabOut();
                AppendLine("}");

                AppendLine("");
                AppendLine($"{visibility} bool Delete{entity.Name}(Int64 {entity.Name.CapLower()}ID)");
                AppendLine("{");
                if (entity.Parent != null)
                {
                    TabIn();
                    AppendLine($"if (!Delete{entity.Parent.Name}({entity.Name.CapLower()}ID))");
                    AppendLine("{");
                    AppendLine("\treturn false;");
                    AppendLine("}");
                    TabOut();
                }
                generator.Delete(this, entity);
                AppendLine("}");

                var searchableFields = GetSearchableFields(entity);

                foreach (var field in searchableFields)
                {
                    var name = field.Key;
                    var type = field.Value;

                    AppendLine("");
                    AppendLine($"public {entity.Name} Find{entity.Name}By{name}({type} {name})");
                    AppendLine("{");
                    TabIn();
                    generator.Find(this, entity, name);
                    TabOut();
                    AppendLine("}");
                }

                AppendLine();
                AppendLine($"public int Count{entity.Name.Pluralize()}()");
                AppendLine("{");
                generator.Count(this, entity);
                AppendLine("}");

                if (!IsAbstract(entity))
                {
                    AppendLine("");
                    AppendLine($"public {entity.Name}[] List{entity.Name.Pluralize()}(int page, int count)");
                    AppendLine("{");
                    AppendLine("\tvar offset = page * count;");
                    generator.List(this, entity);
                    AppendLine("}");
                }


                if (entity.Fields.Any(x => x.Flags.HasFlag(FieldFlags.Editable)))
                {
                    AppendLine("");
                    AppendLine($"public bool Edit{entity.Name}({entity.Name} {varName}, string field, string value)");
                    AppendLine("{");
                    generator.Edit(this, entity, varName);
                    AppendLine("}");
                }


                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        //var fieldName = $"{entity.Name.CapLower()}ID";
                        var fieldName = "ID";

                        bool isUnique = IsUniqueReference(entity, reference);

                        var targetName = reference.Name;
                        if (!isUnique)
                        {
                            targetName = targetName.Pluralize();
                        }

                        AppendLine();
                        var methodName = $"Get{targetName}Of{entity.Name}";
                        AppendLine();
                        AppendLine($"public {reference.Name}{(isUnique?"":"[]")} {methodName}(Int64 {fieldName})");
                        AppendLine("{");
                        generator.Aggregate(this, reference, entity, fieldName, isUnique);
                        AppendLine("}");
                    }
                }

                TabOut();
            }

            AppendLine("}");
            TabOut();

            AppendLine("}");

            EndDoc("Database.cs");
        }

        private List<KeyValuePair<string, string>> GetSearchableFields(Entity entity)
        {
            var searchableFields = new List<KeyValuePair<string, string>>();
            
            searchableFields.Add(new KeyValuePair<string, string>("ID", "Int64"));

            foreach (var field in entity.Fields)
            {
                if (field.Flags.HasFlag(FieldFlags.Searchable))
                {
                    var decl = entity.Decls[field];

                    searchableFields.Add(new KeyValuePair<string, string>(field.Name, decl.Type));
                }
            }

            return searchableFields;
        }

        private void DoWebRequest(string url, IEnumerable<string> args, string nullValue, Action callback, bool requireAuthCheck = true)
        {
            if (requireAuthCheck)
            {
                AppendLine("if (string.IsNullOrEmpty(AuthToken))");
                AppendLine("{");
                TabIn();
                AppendLine("callback(" + nullValue + ", \"Authentication required.\");");
                TabOut();
                AppendLine("}");
                AppendLine();
            }

            AppendLine("var args = new Dictionary<string, string>();");
            if (requireAuthCheck)
            {
                AppendLine($"args[\"token\"] = AuthToken;");
            }

            foreach (var entry in args)
            {
                AppendLine($"args[\"{entry}\"] = {entry}.ToString();");
            }

            AppendLine();
            AppendLine("StartCoroutine(");
            TabIn();
            AppendLine($"API.Request(URL + {url.Replace('\'', '"')}, args, (root, error) =>");
            AppendLine("{");
            AppendLine("if (error != null)");
            AppendLine("{");
            TabIn();
            AppendLine("callback(" + nullValue + ", error);");
            AppendLine("return;");
            TabOut();
            AppendLine("}");

            /*
            AppendLine("root = root[\"response\"];");
            AppendLine("if (root == null)");
            AppendLine("{");
            TabIn();
                AppendLine("callback(" + nullValue + ", \"malformed API response\");");
                AppendLine("return;");
            TabOut();
            AppendLine("}");*/

            AppendLine();
            callback();
            TabOut();
            AppendLine("}));");
            TabOut();
        }

        private void GenerateClient()
        {
            BeginDoc();

            AppendLine($"using System;");
            AppendLine($"using System.Linq;");
            AppendLine($"using System.Collections.Generic;");
            AppendLine($"using UnityEngine;");
            AppendLine();

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            AppendLine();
            TabIn();
            AppendLine($"public class {clientClass}: MonoBehaviour");
            AppendLine("{");

            AppendLine();
            TabIn();

            AppendLine($"public string URL = \"http://localhost/api\";");
            AppendLine("public User User { get; private set;}");
            AppendLine();
            AppendLine("public string AuthToken { get; private set; }");

            AppendLine();
            AppendLine("public static " + clientClass + " Instance { get; private set; }");

            AppendLine();
            AppendLine("public void Awake()");
            AppendLine("{");
            AppendLine("\tInstance = this;");
            AppendLine("}");

            AppendLine();
            AppendLine($"public void LogIn(string creds, Action<User, string> callback)");
            AppendLine("{");
            TabIn();

            
            DoWebRequest($"'/login'", new[] { "creds"}, "null", () => {
                AppendLine("this.AuthToken = root.GetString(\"token\");");
                AppendLine("var node = root.GetNodeByIndex(1);");
                AppendLine($"this.User = {serializationClass}.UserFromNode(node);");
                AppendLine($"callback(this.User, null);");
            }, false);
            TabOut();
            AppendLine("}");

            AppendLine("");
            AppendLine($"public void LogOut()");
            AppendLine("{");
            TabIn();
            AppendLine("this.AuthToken = null;");
            AppendLine("this.User = null;");
            TabOut();
            AppendLine("}");

            foreach (var entity in this.Entities)
            {
                BeginRegion(entity.Name);

                var plural = entity.Name.Pluralize();

                var fieldStr = "";

                int declIndex = 0;
                foreach (var entry in entity.Decls)
                {
                    if (declIndex > 0)
                    {
                        fieldStr+=", ";
                    }

                    fieldStr+=  $"{entry.Value.Type} {entry.Value.Name.CapLower()}";

                    declIndex++;
                }

                if (declIndex > 0)
                {
                    fieldStr += ", ";
                }

                var isAbstract = IsAbstract(entity);
                var idName = $"{entity.Name.CapLower()}ID";

                if (!isAbstract)
                {
                    var fieldArgs = new StringBuilder();
                    var fieldList = new List<string>();
                    foreach (var field in entity.Fields)
                    {
                        var decl = entity.Decls[field];

                        string type;


                        switch (decl.Type.ToLower())
                        {
                            case "bool": type = "bool"; break;

                            default:
                                type = decl.Type;
                                break;
                        }

                        fieldArgs.Append(type);
                        fieldArgs.Append(' ');
                        fieldArgs.Append(decl.Name);
                        fieldArgs.Append(',');
                        fieldArgs.Append(' ');

                        fieldList.Add(decl.Name);
                    }

                    AppendLine();
                    AppendLine($"public void Create{entity.Name}({fieldArgs}Action<{entity.Name}, string> callback)");
                    AppendLine("{");
                    TabIn();

                    DoWebRequest($"'/{entity.Name.ToLower()}/create'", fieldList, "null", () => {
                        AppendLine($"var data = root[\"{entity.Name.CapLower()}\"];");
                        AppendLine($"var {entity.Name.CapLower()} = {serializationClass}.{entity.Name}FromNode(data);");
                        AppendLine($"callback({entity.Name.CapLower()}, null);");
                    });
                    //TabOut();
                    AppendLine("}");

                    AppendLine("");
                    AppendLine($"public void Delete{entity.Name}(Int64 {idName}, Action<bool, string> callback)");
                    AppendLine("{");
                    TabIn();
                    DoWebRequest($"'/{entity.Name.ToLower()}/delete'", new[] { idName }, "false", () => {
                        AppendLine($"var result = root.GetBool(\"{entity.Name.CapLower()}\");");
                        AppendLine($"callback(result, null);");
                    });
                    TabOut();
                    AppendLine("}");
                }


                if (entity.HasEditableFields())
                {
                    AppendLine("");
                    AppendLine($"public void Edit{entity.Name}(Int64 {idName}, Dictionary<string, string> entries, Action<bool, string> callback)");
                    AppendLine("{");
                    TabIn();
                    AppendLine($"if (entries.Count == 0)");
                    AppendLine("{");
                    TabIn();
                        AppendLine($"callback(true, null);");
                    TabOut();
                    AppendLine("}");
                    AppendLine($"var fields = string.Join(\"|\", entries.Keys);");
                    AppendLine($"var values = string.Join(\"|\", entries.Values);");
                    DoWebRequest($"'/{entity.Name.ToLower()}/edit'", new[] { idName, "fields", "values" }, "false", () => {
                        AppendLine($"var result = root.GetBool(\"{entity.Name.CapLower()}\");");
                        AppendLine($"callback(result, null);");
                    });
                    TabOut();
                    AppendLine("}");
                }

                AppendLine("");
                AppendLine($"public void Count{plural}(Action<int, string> callback)");
                AppendLine("{");
                TabIn();
                DoWebRequest($"'/{entity.Name.ToLower()}/count'", Enumerable.Empty<string>(), "-1", () => {
                    AppendLine($"var count = root.GetInt32(\"{entity.Name.CapLower()}\");");
                    AppendLine($"callback(count, null);");
                });
                TabOut();
                AppendLine("}");

                if (!isAbstract)
                {
                    AppendLine("");
                    AppendLine($"public void List{plural}(int page, int count, Action<{entity.Name}[], string> callback)");
                    AppendLine("{");
                    TabIn();

                    DoWebRequest($"'/{entity.Name.ToLower()}/list'", new[] { "page", "count" }, "null", () =>
                    {
                        AppendLine($"var data = root[\"{plural.CapLower()}\"];");
                        AppendLine($"var {plural.CapLower()} = new {entity.Name}[data.ChildCount];");
                        AppendLine("for (int i=0; i<data.ChildCount; i++) {");
                        AppendLine("var child = data.GetNodeByIndex(i);");
                        AppendLine($"{plural.CapLower()}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                        AppendLine("}");
                        AppendLine($"callback({plural.CapLower()}, null);");
                    });
                    TabOut();
                    AppendLine("}");
                }

                var searchableFields = GetSearchableFields(entity);
                foreach (var field in searchableFields)
                {
                    var keyName = field.Key;
                    var keyType = field.Value;

                    var pluralKeyName = keyName == "ID" ? "IDs" : keyName.Pluralize();

                    AppendLine("");
                    AppendLine($"public void Find{entity.Name}By{keyName}({keyType} {entity.Name.CapLower()}{keyName}, Action<{entity.Name}, string> callback)");
                    AppendLine("{");
                    TabIn();
                    AppendLine("\tFind" + plural + "By"+keyName+"(new "+ keyType + "[]{ " + entity.Name.CapLower() + keyName+ " }, (" + plural.CapLower() + ", error) => {");
                    AppendLine("\tcallback(" + plural.CapLower() + " != null && " + plural.CapLower() + ".Length > 0 ? " + plural.CapLower() + "[0] : null, error);");
                    AppendLine("});");
                    TabOut();
                    AppendLine("}");

                    var keySource = $"{entity.Name.CapLower()}{pluralKeyName}";
                    AppendLine("");
                    AppendLine($"public void Find{plural}By{keyName}(IEnumerable<{keyType}> {keySource}, Action<{entity.Name}[], string> callback)");
                    AppendLine("{");
                    TabIn();

                    AppendLine($"if (!{entity.Name.CapLower()}{pluralKeyName}.Any())");
                    AppendLine("{");
                    TabIn();
                    AppendLine("callback(new " + entity.Name + "[0]{}, null);");
                    AppendLine("return;");
                    TabOut();
                    AppendLine("}");
                    AppendLine();

                    var args = new Dictionary<string, string>();
                    AppendLine($"var {pluralKeyName} = string.Join(\"|\", {entity.Name.CapLower()}{pluralKeyName});");
                    DoWebRequest($"'/{entity.Name.ToLower()}/findBy{keyName}'", new[] { pluralKeyName }, "null", () => {
                        AppendLine($"var data = root[\"{plural.CapLower()}\"];");
                        AppendLine($"var {plural.CapLower()} = new {entity.Name}[data.ChildCount];");
                        AppendLine("for (int i=0; i<data.ChildCount; i++) {");
                        AppendLine("var child = data.GetNodeByIndex(i);");
                        AppendLine($"{plural.CapLower()}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                        AppendLine("}");
                        AppendLine($"callback({plural.CapLower()}, null);");
                    });
                    TabOut();
                    AppendLine("}");
                }


                /*AppendLine("");
                AppendLine($"public void Get{entity.Name}(Int64 ID, Action<{entity.Name}, string> callback)");
                AppendLine("{");
                TabIn();
                // TODO id as arguments
                DoWebRequest($"'/{entity.Name.ToLower()}/get'", Enumerable.Empty<string>(), "null", () => {
                    AppendLine($"var data = root[\"{plural}\"];");
                    AppendLine("var child = data.GetNodeByIndex(0);");
                    AppendLine($"var {entity.Name.CapLower()} = {serializationClass}.{entity.Name}FromNode(child);");
                    AppendLine("}");
                    AppendLine($"callback({entity.Name.CapLower()}, null);");
                });
                TabOut();
                AppendLine("}");*/

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var isUnique = IsUniqueReference(entity, reference);

                        var targetName = reference.Name;
                        if (!isUnique)
                        {
                            targetName = targetName.Pluralize();
                        }

                        
                        var fieldName = $"{entity.Name.CapLower()}ID";

                        AppendLine("");
                        var methodName = $"Get{(isUnique ? reference.Name: reference.Name.Pluralize())}Of{entity.Name}";
                        AppendLine("");

                        if (isUnique)
                        {
                            AppendLine($"public void {methodName}(Int64 {fieldName}, Action<{reference.Name}, string> callback)");
                        }
                        else
                        {
                            AppendLine($"public void {methodName}(Int64 {fieldName}, Action<{reference.Name}[], string> callback)");
                        }

                        AppendLine("{");
                        TabIn();

                        DoWebRequest($"'/{entity.Name.ToLower()}/{targetName.ToLower()}'", new[] { fieldName }, "null", () => {

                            if (isUnique)
                            {
                                AppendLine($"var data = root[\"{targetName}\"];");
                                AppendLine("if (data.Value == \"missing\")");
                                AppendLine("{");
                                TabIn();
                                AppendLine($"callback(null, null);");
                                TabOut();
                                AppendLine("}");

                                AppendLine($"var {targetName} = {serializationClass}.{reference.Name}FromNode(data);");
                            }
                            else
                            {
                                AppendLine($"var data = root[\"{targetName}\"];");
                                AppendLine($"var {targetName} = new {reference.Name}[data.ChildCount];");
                                AppendLine("for (int i=0; i<data.ChildCount; i++)");
                                AppendLine("{");
                                TabIn();
                                AppendLine("var child = data.GetNodeByIndex(i);");
                                AppendLine($"{targetName}[i] = {serializationClass}.{reference.Name}FromNode(child);");
                                TabOut();
                                AppendLine("}");
                            }

                            AppendLine($"callback({targetName}, null);");
                        });

                        TabOut();
                        AppendLine("}");
                    }
                }

                EndRegion();
            }

            AppendLine("}");
            TabOut();

            AppendLine("}");

            EndDoc("Client.cs");
        }

        private void CheckPermissions(string varName, string idName, string permission)
        {
            DoServerAuthCheck();
            AppendLine($"var permissions = GetPermissions(authUser, {idName}, \"{varName}\");");
            AppendLine($"if (!permissions.HasFlag(Permissions.{permission}))");
            AppendLine("{");
            TabIn();
            AppendLine($"return Error(\"Cannot {permission.ToLower()} {varName}, permission denied\");");
            TabOut();
            AppendLine("}");
            AppendLine();
        }

        private void ReadRequestVariable(string varName, string type)
        {
            AppendLine($"{type} {varName};");
            var temp = $"temp_{varName}";
            AppendLine($"var {temp} = request.GetVariable(\"{varName}\");");

            ParseVariable(temp, varName, type);
        }

        private void DoServerAuthCheck()
        {
            AppendLine($"var token = request.GetVariable(\"token\");");
            AppendLine($"if (string.IsNullOrEmpty(token))");
            AppendLine("{");
            TabIn();
            AppendLine($"return Error(\"Missing auth token\");");
            TabOut();
            AppendLine("}");
            AppendLine();

            AppendLine("var authUser = GetAuthUser(token);");
            AppendLine($"if (authUser == null)");
            AppendLine("{");
            TabIn();
            AppendLine("return Error(\"Authentication token invalid or expired\");");
            TabOut();
            AppendLine("}");
            AppendLine();
        }

        private void ParseVariable(string src, string dest, string type, bool useTemps = false)
        {
            AppendLine($"if (string.IsNullOrEmpty({src}))");
            AppendLine("{");
            AppendLine($"\treturn Error(\"Missing argument: {dest}\");");
            AppendLine("}");

            if (type.Equals("String", StringComparison.OrdinalIgnoreCase))
            {
                AppendLine($"{dest} = {src};");
            }
            else
            {
                var name = dest;

                if (useTemps)
                {
                    name = "temp_" + dest.Replace('.', '_');
                    AppendLine($"{type} {name};");
                }

                if (Enums.Any(x => x.Name == type))
                {
                    AppendLine($"if (!Enum.TryParse<{type}>({src}, out {name}))");
                }
                else
                {
                    AppendLine($"if (!{type}.TryParse({src}, out {name}))");
                }
                AppendLine("{");
                AppendLine($"\treturn Error(\"Invalid argument: {dest}\");");
                AppendLine("}");

                if (useTemps)
                {
                    AppendLine();
                    AppendLine($"{dest} = {name};");
                }
            }

            AppendLine();
        }

        private string GetExpandedFields(Entity entity)
        {
            string fields = "";

            if (entity.Parent != null)
            {
                fields = GetExpandedFields(entity.Parent);
            }

            foreach (var field in entity.Fields)
            {
                if (field.Flags.HasFlag(FieldFlags.Internal))
                {
                    continue;
                }

                if (field.Flags.HasFlag(FieldFlags.Dynamic))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(fields))
                {
                    fields = fields + ", ";
                }

                var decl = entity.Decls[field];

                var fieldName = decl.Name.CapLower();
                fields += fieldName;

                ReadRequestVariable(fieldName, decl.ExportType());
            }

            return fields;
        }

        private void AppendEditableFields(Entity entity, string varName)
        {
            if (entity.Parent != null)
            {
                AppendEditableFields(entity.Parent, varName);
            }

            foreach (var field in entity.Fields)
            {
                if (!field.Flags.HasFlag(FieldFlags.Editable))
                {
                    continue;
                }

                var decl = entity.Decls[field];

                AppendLine($"case \"{decl.Name}\":");
                TabIn();
                AppendLine($"{decl.ExportType()} {decl.Name};");
                ParseVariable("value", $"{decl.Name}", decl.ExportType(), false);
                AppendLine($"Database.Edit{entity.Name}({varName}, \"{decl.Name}\", {decl.Name}.ToString());");
                AppendLine($"break;");
                AppendLine();
                TabOut();
            }
        }

        public bool HasDynamicFields(Entity entity)
        {
            return entity.Fields.Any(x => x.Flags.HasFlag(FieldFlags.Dynamic));
        }

        private void GenerateServer()
        {
            BeginDoc();

            AppendLine("using System;");
            AppendLine("using System.Linq;");
            AppendLine("using System.Collections.Generic;");
            AppendLine("using LunarLabs.Parser;");
            AppendLine("using LunarLabs.Parser.JSON;");
            AppendLine("using LunarLabs.WebServer.Core;");
            AppendLine("using LunarLabs.WebServer.HTTP;");
            AppendLine();

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");
            TabIn();

            AppendLine();
            AppendLine("public enum Permissions");
            AppendLine("{");
            TabIn();
            AppendLine("None = 0,");
            AppendLine("Read = 1,");
            AppendLine("Write = 2,");
            AppendLine("Create = 4,");
            AppendLine("Delete = 8,");
            AppendLine("List = 16,");
            TabOut();
            AppendLine("}");


            AppendLine();
            AppendLine("public enum AuthMode");
            AppendLine("{");
            TabIn();
            AppendLine("Login,");
            AppendLine("Signup,");
            AppendLine("Mixed,");
            TabOut();
            AppendLine("}");
    
            AppendLine();
            AppendLine($"public abstract class {serverClass}: HTTPServer");
            AppendLine("{");
            TabIn();

            AppendLine($"public readonly {databaseClass} Database;");
            
            AppendLine();
            AppendLine($"private Dictionary<string, User> _auths = new Dictionary<string, User>();");
            AppendLine("private readonly Random _random = new Random();");
            AppendLine("public readonly string Prefix;");

            AppendLine();
            AppendLine($"public {serverClass}({databaseClass} database, ServerSettings settings, string prefix = \"api\", LoggerCallback log = null, SessionStorage sessionStorage = null): base(settings, log, sessionStorage)");
            AppendLine("{");
            TabIn();

            AppendLine("if (!prefix.StartsWith(\"/\"))");
            AppendLine("{");
            TabIn();
            AppendLine("prefix = \"/\" + prefix;");
            TabOut();
            AppendLine("}");
            AppendLine("this.Prefix = prefix;");

            AppendLine();
            AppendLine("this.Database = database;");

            /*AppendLine();
            AppendLine($"this.OnNotFound = (request) =>");
            AppendLine("{");
            AppendLine("\treturn Error(\"URL not found\");");
            AppendLine("};");*/

            AppendLine();
            AppendLine("this.OnNotFound = (request) =>");
            AppendLine("{");
            TabIn();
                AppendLine("var error = Error(\"can't find route: \" + request.url);");
                AppendLine("var json = JSONWriter.WriteToString(error);");
                AppendLine("return HTTPResponse.FromString(json, HTTPCode.NotFound);");
            TabOut();
            AppendLine("};");


            AppendLine();
            BeginRegion("AUTH");
            AppendLine($"this.Post(Prefix + \"/login\", (request) =>");
            AppendLine("{");
            TabIn();
            AppendLine($"var creds = request.GetVariable(\"creds\");");
            AppendLine($"if (creds == null)");
            AppendLine("{");
            AppendLine($"\treturn Error(\"missing login credentials\");");
            AppendLine("}");

            AppendLine();
            AppendLine("string error;");
            AppendLine($"var user = Authenticate(creds, out error);");
            AppendLine($"if (user == null)");
            AppendLine("{");
            AppendLine($"\treturn Error(\"login failed: \" + error);");
            AppendLine("}");

            AppendLine();
            AppendLine("var ticks = DateTime.UtcNow.Ticks;");
            AppendLine("var rand = _random.Next(1000, 9999);");
            AppendLine("var token = $\"{user.ID}_{ticks}_{rand}\";");
            AppendLine($"_auths[token] = user;");

            AppendLine("var result = DataNode.CreateObject(\"response\");");
            AppendLine("result.AddField(\"token\", token);");
            AppendLine($"result.AddNode({serializationClass}.UserToNode(user));");
            AppendLine("return result;");

            TabOut();
            AppendLine("});");
            EndRegion();

            foreach (var entity in this.Entities)
            {
                var isAbstract = IsAbstract(entity);
                var idName = $"{entity.Name}ID";

                AppendLine();
                BeginRegion(entity.Name);

                var varName = entity.Name.CapLower();

                if (!isAbstract)
                {
                    AppendLine();
                    AppendLine($"this.Post(Prefix + \"/{varName}/create/\", (request) =>");
                    AppendLine("{");
                    TabIn();
                    CheckPermissions(varName, "0", "Create");

                    string fields = GetExpandedFields(entity);
                    AppendLine($"var {varName} = Database.Create{entity.Name}({fields});");
                    AppendLine($"if ({varName}  == null)");
                    AppendLine("{");
                    AppendLine($"\treturn Error(\"{entity.Name} creation failed\");");
                    AppendLine("}");
                    AppendLine("var result = DataNode.CreateObject(\"response\");");
                    AppendLine($"result.AddNode({serializationClass}.{entity.Name}ToNode({varName}));");
                    AppendLine("return result;");
                    TabOut();
                    AppendLine("});");
            
                    AppendLine();
                    AppendLine($"this.Post(Prefix + \"/{varName}/delete/\", (request) =>");
                    AppendLine("{");
                    TabIn();
                    ReadRequestVariable(idName, "Int64");
                    CheckPermissions(varName, idName, "Delete");
                    AppendLine($"var result = Database.Delete{entity.Name}({idName});");
                    AppendLine($"return Response(result.ToString());");
                    TabOut();
                    AppendLine("});");
                }

                AppendLine();
                AppendLine($"this.Post(Prefix + \"/{varName}/count/\", (request) =>");
                AppendLine("{");
                TabIn();
                CheckPermissions(varName, "0", "List");
                var plural = entity.Name.Pluralize();
                AppendLine($"var result = Database.Count{plural}();");
                AppendLine($"return Response(result.ToString());");
                TabOut();
                AppendLine("});");

                if (!isAbstract)
                {
                    AppendLine();
                    AppendLine($"this.Post(Prefix + \"/{varName}/list\", (request) =>");
                    AppendLine("{");
                    TabIn();
                    CheckPermissions(varName, "0", "List");
                    ReadRequestVariable("page", "int");
                    ReadRequestVariable("count", "int");
                    AppendLine($"var items = Database.List{entity.Name.Pluralize()}(page, count);");
                    AppendLine("var array =  " + serializationClass + ".ToArray(\"" + plural + "\", items, " + serializationClass + "." + entity.Name + "ToNode);");
                    AppendLine("var result = DataNode.CreateObject(\"response\");");
                    AppendLine("result.AddField(\"page\", page);");
                    AppendLine("result.AddNode(array);");
                    AppendLine("return result;");
                    TabOut();
                    AppendLine("});");
                }

                var searchableFields = GetSearchableFields(entity);

                foreach (var field in searchableFields)
                {
                    var keyName = field.Key;
                    var keyType = field.Value;
                    var pluralKeyName = keyName == "ID" ? "IDs" : keyName.Pluralize();

                    AppendLine();
                    AppendLine($"this.Post(Prefix + \"/{varName}/findBy{keyName}\", (request) =>");
                    AppendLine("{");
                    TabIn();
                    CheckPermissions(varName, "0", "List");
                    ReadRequestVariable(pluralKeyName, "string");

                    string filter = keyType.Equals("string", StringComparison.OrdinalIgnoreCase) ? "" : $"Select(x => {keyType}.Parse(x)).";

                    string processor = "";

                    if (HasDynamicFields(entity))
                    {
                        processor = $".Select(x => Process{entity.Name}(x))";
                    }

                    AppendLine($"var items = {pluralKeyName}.Split('|').{filter}Select(x => Database.Find{entity.Name}By{keyName}(x)){processor}.ToArray();");
                    AppendLine("var array =  " + serializationClass + ".ToArray(\"" + plural + "\", items, " + serializationClass + "." + entity.Name + "ToNode);");
                    AppendLine("var result = DataNode.CreateObject(\"response\");");
                    AppendLine("result.AddNode(array);");
                    AppendLine("return result;");
                    TabOut();
                    AppendLine("});");
                }

                if (entity.HasEditableFields())
                {
                    AppendLine();
                    AppendLine($"this.Post(Prefix + \"/{varName}/edit/\", (request) =>");
                    AppendLine("{");
                    TabIn();

                    ReadRequestVariable(idName, "Int64");
                    ReadRequestVariable("fields", "string");
                    ReadRequestVariable("values", "string");
                    CheckPermissions(varName, idName, "Write");
                    AppendLine($"var {varName} = Database.Find{entity.Name}ByID({idName});");

                    if (HasDynamicFields(entity))
                    {
                        AppendLine($"{varName} = Process{entity.Name}({varName});");
                    }

                    AppendLine($"var fieldEntries = fields.Split('|');");
                    AppendLine($"var valuesEntries = values.Split('|');");
                    AppendLine($"if (fieldEntries.Length != valuesEntries.Length)");
                    AppendLine("{");
                    AppendLine("\treturn Error(\"Field and values dont match\");");
                    AppendLine("}");
                    AppendLine();

                    AppendLine($"for (int i=0; i<fieldEntries.Length; i++)");
                    AppendLine("{");
                    TabIn();

                    AppendLine("var field = fieldEntries[i];");
                    AppendLine("var value = valuesEntries[i];");
                    AppendLine($"switch (field)");
                    AppendLine("{");
                    TabIn();

                    AppendEditableFields(entity, varName);

                    AppendLine("default:");
                    AppendLine("\treturn Error(\"Invalid field: \" + field);");
                    TabOut();
                    AppendLine("}");

                    TabOut();
                    AppendLine("}");

                    AppendLine($"return Response(\"true\");");
                    TabOut();
                    AppendLine("});");
                }

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var isUnique = IsUniqueReference(entity, reference);

                        var targetName = reference.Name;
                        if (!isUnique)
                        {
                            targetName = targetName.Pluralize();
                        }

                        var methodName = $"Get{targetName}Of{entity.Name}";

                        AppendLine();
                        AppendLine($"this.Post(Prefix + \"/{varName}/{targetName.ToLower()}\",  (request) =>");
                        AppendLine("{");
                        TabIn();
                        ReadRequestVariable(idName, "Int64");
                        CheckPermissions(varName, idName, "Read");

                        AppendLine("var result = DataNode.CreateObject(\"response\");");

                        if (isUnique)
                        {
                            AppendLine($"var item = Database.{methodName}({idName});");
                            AppendLine("if (item != null)");
                            AppendLine("{");
                            TabIn();
                            AppendLine($"result.AddNode({serializationClass}.{reference.Name}ToNode(item));");
                            TabOut();
                            AppendLine("}");
                            AppendLine("else");
                            AppendLine("{");
                            TabIn();
                            AppendLine("result.AddField(\""+reference.Name+"\", \"none\");");
                            TabOut();
                            AppendLine("}");
                        }
                        else
                        {
                            AppendLine($"var items = Database.{methodName}({idName});");
                            AppendLine("var array =  " + serializationClass + ".ToArray(\"" + targetName + "\", items, " + serializationClass + "." + reference.Name + "ToNode);");
                            AppendLine("result.AddNode(array);");
                        }

                        AppendLine("return result;");

                        TabOut();
                        AppendLine("});");
                    }
                }

                EndRegion();
                AppendLine();
            }

            TabOut();
            AppendLine("}");

            AppendLine();
            AppendLine("public DataNode Error(string msg)");
            AppendLine("{");
            TabIn();
            AppendLine("var result = DataNode.CreateObject(\"response\");");
            AppendLine("result.AddField(\"error\", msg);");
            AppendLine("return result;");
            TabOut();
            AppendLine("}");

            AppendLine();
            AppendLine("public DataNode Response(string content)");
            AppendLine("{");
            TabIn();
            AppendLine("var result = DataNode.CreateObject(\"response\");");
            AppendLine("result.AddField(\"content\", content);");
            AppendLine("return result;");
            TabOut();
            AppendLine("}");

            AppendLine();
            AppendLine("protected User GetAuthUser(string token)");
            AppendLine("{");
            TabIn();
            AppendLine("if (_auths.ContainsKey(token))");
            AppendLine("{");
            AppendLine("\treturn _auths[token];");
            AppendLine("}");
            AppendLine();
            AppendLine("return null;");
            TabOut();
            AppendLine("}");

            AppendLine();
            AppendLine("public abstract User Authenticate(string creds, out string error);");
            AppendLine("public abstract Permissions GetPermissions(User user, Int64 targetID, string scheme);");

            foreach (var entity in Entities)
            {
                if (HasDynamicFields(entity))
                {
                    AppendLine($"public abstract {entity.Name} Process{entity.Name}({entity.Name} entity);");
                }
            }

            AppendLine("}");
            TabOut();

            AppendLine("}");

            EndDoc("Server.cs");
        }

    }
}
