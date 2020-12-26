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
    }

    public class Entity
    {
        public Entity Parent;
        public string Name;
        public List<Field> Fields;

        public Dictionary<Field, FieldDecl> Decls = new Dictionary<Field, FieldDecl>();

        public Entity(string name, Entity parent, IEnumerable<Field> fields)
        {
            Name = name;
            Parent = parent;
            Fields = fields.ToList();
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
        public abstract void Namespaces(StringBuilder sb);
        public abstract void Declarations(StringBuilder sb, IEnumerable<Entity> entities);
        public abstract void Create(StringBuilder sb, Entity entity);
        public abstract void Delete(StringBuilder sb, Entity entity);
        public abstract void Find(StringBuilder sb, Entity entity, string field);        
        public abstract void Get(StringBuilder sb, Entity entity);
        public abstract void List(StringBuilder sb, Entity entity);
        public abstract void Count(StringBuilder sb, Entity entity);
        public abstract void Aggregate(StringBuilder sb, Entity source, Entity target, string fieldName);
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
                    result.Add(other);
                }
            }

            return result;
        }

        private bool IsAbstract(Entity entity)
        {
            return (Entities.Any(x => x.Parent == entity));
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
            File.WriteAllText(fileName, _sb.ToString());
        }

        private void BeginRegion(string name)
        {
            AppendLine($"#region {name.ToUpper()}", false);
        }

        private void EndRegion()
        {
            AppendLine($"#endregion", false);
        }

        private void TabIn()
        {
            _tabs++;
        }

        private void TabOut()
        {
            _tabs--;
        }

        private void Append(string text, bool addTabs = true)
        {
            if (addTabs)
            {
                for (int i = 0; i < _tabs; i++)
                    _sb.Append('\t');
            }

            _sb.Append(text);
        }

        private void AppendLine(string text = "", bool addTabs = true)
        {
            Append(text + "\n", addTabs);
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
            AppendLine("public UInt64 ID { get; internal set;} ");
            AppendLine($"public static implicit operator UInt64(Entity obj) => obj.ID;");
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
                    entity.Fields.Insert(0, new Field(enumName, enumName, FieldFlags.None));
                }
                else
                if (entity.Parent != null)
                {
                    entity.Fields.Insert(0, new Field("ParentID", "UInt64", FieldFlags.None));
                }

                foreach (var field in entity.Fields)
                {
                    string name, type;

                    if (Entities.Any(x => x.Name == field.Type))
                    {
                        name = field.Name.CapLower() + "ID";
                        type = "UInt64";
                    }
                    else
                    {
                        type = field.Type;
                        name = field.Name;
                    }

                    var decl = new FieldDecl(name, type);
                    entity.Decls[field] = decl;

                    AppendLine($"\tpublic {type} {name} " + " { get; internal set;}");
                }

                AppendLine("}");
                TabOut();
            }

            AppendLine("}");

            EndDoc("Model.cs");
        }

        private void GenerateSerialization()
        {
            BeginDoc();

            AppendLine("using LunarLabs.Parser;");
            AppendLine();

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            TabIn();
            AppendLine($"public class {serializationClass}");
            AppendLine("{");

            foreach (var entity in this.Entities)
            {
                AppendLine();
                TabIn();

                AppendLine($"public static {entity.Name} {entity.Name}FromNode(DataNode node)");
                AppendLine("{");
                var varName = entity.Name.CapLower();
                TabIn();
                AppendLine($"var {varName} = new {entity.Name}();");
                foreach (var entry in entity.Decls)
                {
                    string getStr;

                    if (Enums.Any(x => x.Name == entry.Value.Type))
                    {
                        getStr = $"Enum<{entry.Value.Type.CapUpper()}>";
                    }
                    else
                    {
                        getStr = entry.Value.Type.CapUpper();
                    }

                    AppendLine($"{varName}.{entry.Value.Name} = node.Get{getStr}(\"{entry.Value.Name}\");");
                }
                AppendLine($"return {varName};");
                TabOut();
                AppendLine("}");

                AppendLine();
                AppendLine($"public static DataNode {entity.Name}ToNode({entity.Name} {varName})");
                AppendLine("{");

                TabIn();
                AppendLine($"var node = DataNode.CreateObject(\"{entity.Name}\");");

                foreach (var entry in entity.Decls)
                {
                    AppendLine($"node.AddField(\"{entry.Value.Name}\", {varName}.{entry.Value.Name});");
                }
                AppendLine($"return node;");
                TabOut();
                AppendLine("}");

                TabOut();
            }

            AppendLine("}");
            TabOut();
            AppendLine("}");

            EndDoc($"Serialization.cs");
        }

        private void GenerateDatabase()
        {
            BeginDoc();

            AppendLine("using System;");
            generator.Namespaces(_sb);

            AppendLine($"namespace {Name}.Model");
            AppendLine("{");

            AppendLine("");
            AppendLine($"\tpublic class {databaseClass}");
            TabIn();
            AppendLine("{");
            generator.Declarations(_sb, this.Entities);

            foreach (var entity in this.Entities)
            {
                AppendLine();

                TabIn();
                Append($"public {entity.Name} Create{entity.Name}(");

                int declIndex = 0;
                foreach (var entry in entity.Decls)
                {
                    if (declIndex > 0)
                    {
                        Append(", ", false);
                    }

                    Append($"{entry.Value.Type} {entry.Value.Name.CapLower()}", false);

                    declIndex++;
                }

                AppendLine(")", false);
                AppendLine("{");
                generator.Create(_sb, entity);
                AppendLine("}");

                var visibility = IsAbstract(entity) ? "private" : "public";

                AppendLine("");
                AppendLine($"{visibility} bool Delete{entity.Name}(UInt64 {entity.Name.CapLower()}ID)");
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
                generator.Delete(_sb, entity);
                AppendLine("}");

                var searchableFields = new List<KeyValuePair<string, string>>();
                searchableFields.Add(new KeyValuePair<string, string>("ID", "UInt64"));
                foreach (var field in entity.Fields) 
                {
                    if (field.Flags.HasFlag(FieldFlags.Searchable))
                    {
                        var decl = entity.Decls[field];

                        searchableFields.Add(new KeyValuePair<string, string>(field.Name, decl.Type));
                    }
                }

                foreach (var field in searchableFields)
                {
                    var name = field.Key;
                    var type = field.Value;

                    AppendLine("");
                    AppendLine($"public {entity.Name} Find{entity.Name}By{name}({type} {name})");
                    AppendLine("{");
                    TabIn();
                    generator.Find(_sb, entity, name);
                    TabOut();
                    AppendLine("}");
                }

                AppendLine();
                AppendLine($"public int Count{entity.Name.Pluralize()}()");
                AppendLine("{");
                generator.Count(_sb, entity);
                AppendLine("}");

                AppendLine("");
                AppendLine($"public {entity.Name}[] List{entity.Name.Pluralize()}(int page, int count)");
                AppendLine("{");
                AppendLine("\tvar offset = page * count;");
                generator.List(_sb, entity);
                AppendLine("}");

                AppendLine("");
                AppendLine($"public {entity.Name}[] Get{entity.Name.Pluralize()}(UInt64[] IDs)");
                AppendLine("{");
                generator.Get(_sb, entity);
                AppendLine("}");


                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var fieldName = $"{entity.Name.CapLower()}ID";

                        AppendLine();
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";
                        AppendLine();
                        AppendLine($"public {reference.Name}[] {methodName}(UInt64 {fieldName})");
                        AppendLine("{");
                        generator.Aggregate(_sb, reference, entity, fieldName);
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

        private void DoWebRequest(string url, string nullValue, Action callback, bool requireAuthCheck = true)
        {
            if (requireAuthCheck)
            {
                AppendLine("if (string.IsNullOrEmpty(_authToken))");
                AppendLine("{");
                TabIn();
                AppendLine("callback(" + nullValue + ", \"Authentication required.\");");
                TabOut();
                AppendLine("}");
                AppendLine();
            }

            AppendLine("StartCoroutine(");
            TabIn();
            AppendLine($"WebClient.RESTRequest(URL + {url.Replace('\'', '"')}, 0, (error, desc) =>");
            AppendLine("{");
            TabIn();
            AppendLine("callback(" + nullValue + ", desc);");
            AppendLine("}, (root) =>");
            AppendLine("{");
            callback();
            TabOut();
            AppendLine("}));");
            TabOut();
        }

        private void GenerateClient()
        {
            BeginDoc();

            AppendLine($"using System;");
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

            AppendLine($"public string URL = \"localhost\";");
            AppendLine("public User User { get; private set;}");
            AppendLine();
            AppendLine("private string _authToken = null;");

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
            DoWebRequest($"'/login/' + creds", "null", () => {
                AppendLine("this._authToken = root.GetString(\"token\");");
                AppendLine("var node = root.GetNode(\"user\");");
                AppendLine($"this.User = {serializationClass}.UserFromNode(node);");
                AppendLine($"callback(this.User, null);");
            }, false);
            TabOut();
            AppendLine("}");

            AppendLine("");
            AppendLine($"public void LogOut()");
            AppendLine("{");
            TabIn();
            AppendLine("this._authToken = null;");
            AppendLine("this.User = null;");
            TabOut();
            AppendLine("}");

            foreach (var entity in this.Entities)
            {
                if (IsAbstract(entity))
                {
                    continue;
                }

                BeginRegion(entity.Name);

                AppendLine();
                Append($"public void Create{entity.Name}(");

                int declIndex = 0;
                foreach (var entry in entity.Decls)
                {
                    if (declIndex > 0)
                    {
                        Append(", ", false);
                    }

                    Append($"{entry.Value.Type} {entry.Value.Name.CapLower()}", false);

                    declIndex++;
                }

                if (declIndex > 0)
                {
                    Append(", ", false);
                }

                Append($"Action<{entity.Name}, string> callback", false);

                AppendLine(")", false);
                AppendLine("{");
                TabIn();
                DoWebRequest($"'/create/{entity.Name}'", "null", () => {
                    AppendLine($"var data = root[\"{entity.Name.CapLower()}\"];");
                    AppendLine($"var {entity.Name.CapLower()} = {serializationClass}.{entity.Name}FromNode(data);");
                    AppendLine($"callback({entity.Name.CapLower()}, null);");
                });
                TabOut();
                AppendLine("}");

                var idName = $"{entity.Name.CapLower()}ID";
                AppendLine("");
                AppendLine($"public void Delete{entity.Name}(UInt64 {idName}, Action<bool, string> callback)");
                AppendLine("{");
                TabIn();
                DoWebRequest($"'/delete/{entity.Name}/' + {idName}", "false", () => {
                    AppendLine($"var result = root.GetBool(\"{entity.Name.CapLower()}\");");
                    AppendLine($"callback(result, null);");
                });
                TabOut();
                AppendLine("}");

                AppendLine("");
                AppendLine($"public void Count{entity.Name.Pluralize()}(Action<int, string> callback)");
                AppendLine("{");
                TabIn();
                DoWebRequest($"'/count/{entity.Name}'", "-1", () => {
                    AppendLine($"var count = root.GetInt32(\"{entity.Name.CapLower()}\");");
                    AppendLine($"callback(count, null);");
                });
                TabOut();
                AppendLine("}");

                AppendLine("");
                AppendLine($"public void List{entity.Name.Pluralize()}(int page, int count, Action<{entity.Name}[], string> callback)");
                AppendLine("{");
                TabIn();

                var plural = entity.Name.CapLower().Pluralize();
                DoWebRequest($"'/list/{entity.Name}/'+ page + '/'+ count", "null", () => {
                    AppendLine($"var data = root[\"{plural}\"];");
                    AppendLine($"var {plural} = new {entity.Name}[data.ChildCount];");
                    AppendLine("for (int i=0; i<data.ChildCount; i++) {");
                    AppendLine("var child = data.GetNodeByIndex(i);");
                    AppendLine($"{plural}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                    AppendLine("}");
                    AppendLine($"callback({plural}, null);");
                });
                TabOut();
                AppendLine("}");

                AppendLine("");
                AppendLine($"public void Get{entity.Name.Pluralize()}(IEnumerable<UInt64> IDs, Action<{entity.Name}[], string> callback)");
                AppendLine("{");
                TabIn();

                DoWebRequest($"'/post/{entity.Name}'", "null", () => {
                    AppendLine($"var data = root[\"{plural}\"];");
                    AppendLine($"var {plural} = new {entity.Name}[data.ChildCount];");
                    AppendLine("for (int i=0; i<data.ChildCount; i++) {");
                    AppendLine("var child = data.GetNodeByIndex(i);");
                    AppendLine($"{plural}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                    AppendLine("}");
                    AppendLine($"callback({plural}, null);");
                });
                TabOut();
                AppendLine("}");

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var fieldName = $"{entity.Name.CapLower()}ID";

                        AppendLine("");
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";
                        AppendLine("");
                        AppendLine($"public void {methodName}(UInt64 {fieldName}, Action<{reference.Name}[], string> callback)");
                        AppendLine("{");
                        TabIn();

                        var targetName = reference.Name.CapLower().Pluralize();
                        DoWebRequest($"'/get/{entity.Name}/{targetName}'", "null", () => {
                            AppendLine($"var data = root[\"{targetName}\"];");
                            AppendLine($"var {targetName} = new {reference.Name}[data.ChildCount];");
                            AppendLine("for (int i=0; i<data.ChildCount; i++) {");
                            TabIn();
                            AppendLine("var child = data.GetNodeByIndex(i);");
                            AppendLine($"{targetName}[i] = {serializationClass}.{reference.Name}FromNode(child);");
                            TabOut();
                            AppendLine("}");
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

        private void GenerateServer()
        {
            BeginDoc();

            AppendLine("using System;");
            AppendLine("using System.Collections.Generic;");
            AppendLine("using LunarLabs.Parser;");
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
            AppendLine($"public abstract class {serverClass}: HTTPServer");
            AppendLine("{");
            TabIn();

            AppendLine($"public readonly {databaseClass} Database;");
            
            AppendLine();
            AppendLine($"private Dictionary<string, User> _auths = new Dictionary<string, User>();");

            AppendLine();
            AppendLine($"public {serverClass}({databaseClass} database, ServerSettings settings, LoggerCallback log = null, SessionStorage sessionStorage = null): base(settings, log, sessionStorage)");
            AppendLine("{");
            TabIn();

            AppendLine("this.Database = database;");

            /*AppendLine();
            AppendLine($"this.OnNotFound = (request) =>");
            AppendLine("{");
            AppendLine("\treturn Error(\"URL not found\");");
            AppendLine("};");*/


            AppendLine();
            AppendLine($"this.Post(\"/login\", (request) =>");
            AppendLine("{");
            TabIn();
            AppendLine($"var creds = request.GetVariable(\"creds\");");
            AppendLine($"var user = Authenticate(creds);");
            AppendLine($"if (user == null)");
            AppendLine("{");
            AppendLine($"\treturn Error(\"invalid login details\");");
            AppendLine("}");

            AppendLine();
            AppendLine("var token = DateTime.UtcNow.Ticks.ToString();");
            AppendLine($"_auths[token] = user;");
            AppendLine($"return token;");
            TabOut();
            AppendLine("});");

            foreach (var entity in this.Entities)
            {
                if (IsAbstract(entity))
                {
                    continue;
                }

                BeginRegion(entity.Name);

                var varName = entity.Name.CapLower();

                AppendLine();
                AppendLine($"this.Post(\"/{varName}/create/\", (request) =>");
                AppendLine("{");
                TabIn();
                CheckPermissions(varName, "0", "Create");
                string fields = "";
                foreach (var entry in entity.Decls)
                {
                    if (!string.IsNullOrEmpty(fields))
                    {
                        fields = fields + ", ";
                    }

                    var fieldName = entry.Value.Name.CapLower();
                    fields += fieldName;

                    ReadRequestVariable(fieldName, entry.Value.Type);
                }
                AppendLine($"var {varName} = Database.Create{entity.Name}({fields});");
                AppendLine($"if ({varName}  == null)");
                AppendLine("{");
                AppendLine($"\treturn Error(\"{entity.Name} creation failed\");");
                AppendLine("}");
                AppendLine($"return {serializationClass}.{entity.Name}ToNode({varName});");
                TabOut();
                AppendLine("});");

                AppendLine();
                AppendLine($"this.Post(\"/{varName}/delete/\", (request) =>");
                AppendLine("{");
                TabIn();
                ReadRequestVariable("id", "UInt64");
                CheckPermissions(varName, "id", "Delete");
                AppendLine($"var result = Database.Delete{entity.Name}(id);");
                AppendLine($"return result.ToString();");
                TabOut();
                AppendLine("});");

                AppendLine();
                AppendLine($"this.Get(\"/{varName}/count/\", (request) =>");
                AppendLine("{");
                TabIn();
                CheckPermissions(varName, "0", "List");
                AppendLine($"var result = Database.Count{entity.Name.Pluralize()}();");
                AppendLine($"return result.ToString();");
                TabOut();
                AppendLine("});");

                AppendLine();
                AppendLine($"this.Get(\"/{varName}/list/" + "{page}/{count}" + "\", (request) =>");
                AppendLine("{");
                TabIn();
                CheckPermissions(varName, "0", "List");
                ReadRequestVariable("page", "int");
                ReadRequestVariable("count", "int");
                AppendLine($"var result = Database.List{entity.Name.Pluralize()}(page, count);");
                AppendLine($"return result.ToDataNode();");
                TabOut();
                AppendLine("});");

                AppendLine();
                AppendLine($"this.Post(\"/{varName}/edit/\", (request) =>");
                AppendLine("{");
                TabIn();

                ReadRequestVariable("id", "UInt64");
                ReadRequestVariable("fields", "string");
                ReadRequestVariable("values", "string");
                CheckPermissions(varName, "id", "Write");
                AppendLine($"var {varName} = Database.Find{entity.Name}ByID(id);");

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
                foreach (var field in entity.Fields)
                {
                    if (!field.Flags.HasFlag(FieldFlags.Editable))
                    {
                        continue;
                    }

                    var entry = entity.Decls[field];

                    AppendLine($"case \"{entry.Name}\":");
                    TabIn();
                    ParseVariable("value", $"{varName}.{entry.Name}", entry.Type, true);
                    AppendLine($"break;");
                    AppendLine();
                    TabOut();
                }

                AppendLine("default:");
                AppendLine("\treturn Error(\"Invalid field: \" + field);");
                TabOut();
                AppendLine("}");

                TabOut();
                AppendLine("}");

                AppendLine($"return \"true\";");
                TabOut();
                AppendLine("});");

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";

                        AppendLine();
                        AppendLine($"this.Get(\"/{varName}/{reference.Name.CapLower().Pluralize()}/" + "{id}" + "\",  (request) =>");
                        AppendLine("{");
                        TabIn();
                        ReadRequestVariable("id", "UInt64");
                        CheckPermissions(varName, "id", "Read");
                        AppendLine($"var result = Database.{methodName}(id);");
                        AppendLine($"return result.ToDataNode();");
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
            AppendLine("var result = DataNode.CreateObject(\"result\");");
            AppendLine("result.AddField(\"error\", msg);");
            AppendLine("return result;");
            TabOut();
            AppendLine("}");

            AppendLine();
            AppendLine("private User GetAuthUser(string token)");
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
            AppendLine("public abstract User Authenticate(string creds);");
            AppendLine("public abstract Permissions GetPermissions(User user, UInt64 targetID, string scheme);");

            AppendLine("}");
            TabOut();

            AppendLine("}");

            EndDoc("Server.cs");
        }

    }
}
