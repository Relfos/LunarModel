using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using LunarLabs.Parser;

namespace LunarModel
{
    public class Field
    {
        public string Name;
        public string Type;

        public Field(string name, string type)
        {
            Name = name;
            Type = type;
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
            GenerateClasses();
            GenerateSerialization();
            GenerateDatabase();
            GenerateClient();
            GenerateServer();
        }

        private void GenerateClasses()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"namespace {Name}.Model");
            sb.AppendLine("{");


            foreach (var enumm in this.Enums)
            {
                sb.AppendLine("");
                sb.AppendLine("\tpublic enum " + enumm.Name);
                sb.AppendLine("\t{");
                foreach (var entry in enumm.Values)
                {
                    sb.AppendLine("\t\t"+entry + ", ");
                }
                sb.AppendLine("\t}");
            }

            sb.AppendLine("\tpublic class Entity");
            sb.AppendLine("\t{");
            sb.AppendLine("\t\tpublic ulong ID { get; internal set;} ");
            sb.AppendLine($"\t\tpublic static implicit operator ulong(Entity obj) => obj.ID;");
            sb.AppendLine("\t}");

            foreach (var entity in this.Entities)
            {
                sb.AppendLine("");
                sb.AppendLine($"\tpublic class {entity.Name} : Entity");
                sb.AppendLine("\t{");

                if (IsAbstract(entity))
                {
                    var enumName = $"{entity.Name}Kind";
                    entity.Fields.Insert(0, new Field(enumName, enumName));
                }
                else
                if (entity.Parent != null)
                {
                    entity.Fields.Insert(0, new Field("ParentID", "ulong"));
                }

                foreach (var field in entity.Fields)
                {
                    string name, type;

                    if (Entities.Any(x => x.Name == field.Type))
                    {
                        name = field.Name.CapLower() + "ID";
                        type = "ulong";
                    }
                    else
                    {
                        type = field.Type;
                        name = field.Name;
                    }

                    var decl = new FieldDecl(name, type);
                    entity.Decls[field] = decl;

                    sb.AppendLine($"\t\tpublic {type} {name} "+ " { get; internal set;}");
                }

                sb.AppendLine("\t}");
            }

            sb.AppendLine("}");

            File.WriteAllText($"Model.cs", sb.ToString());
        }

        private void GenerateSerialization()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"namespace {Name}.Model");
            sb.AppendLine("{");

            sb.AppendLine($"\tpublic class {serializationClass}");
            sb.AppendLine("\t{");

            foreach (var entity in this.Entities)
            {
                sb.AppendLine("");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic static {entity.Name} {entity.Name}FromNode(DataNode node)");
                sb.AppendLine("\t\t{");
                var varName = entity.Name.CapLower();
                sb.AppendLine($"\t\t\tvar {varName} = new {entity.Name}();");
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

                    sb.AppendLine($"\t\t\t{varName}.{entry.Value.Name} = node.Get{getStr}(\"{entry.Value.Name}\");");
                }
                sb.AppendLine($"\t\t\treturn {varName};");
                sb.AppendLine("\t\t}");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic static DataNode {entity.Name}ToNode({entity.Name} {varName})");
                sb.AppendLine("\t\t{");
                
                sb.AppendLine($"\t\t\tvar node = DataNode.CreateObject(\"{entity.Name}\");");
                
                foreach (var entry in entity.Decls)
                {
                    sb.AppendLine($"\t\t\tnode.AddField(\"{entry.Value.Name}\", {varName}.{entry.Value.Name});");
                }
                sb.AppendLine($"\t\t\treturn node;");
                sb.AppendLine("\t\t}");
            }

            sb.AppendLine("}");

            File.WriteAllText($"Serialization.cs", sb.ToString());
        }

        private void GenerateDatabase()
        {
            var sb = new StringBuilder();

            generator.Namespaces(sb);

            sb.AppendLine($"namespace {Name}.Model");
            sb.AppendLine("{");

            sb.AppendLine("");
            sb.AppendLine($"\tpublic class {Name}Database");
            sb.AppendLine("\t{");
            generator.Declarations(sb, this.Entities);

            foreach (var entity in this.Entities)
            {
                sb.AppendLine("");
                sb.Append($"\t\tpublic {entity.Name} Create{entity.Name}(");

                int declIndex = 0;
                foreach (var entry in entity.Decls)
                {
                    if (declIndex > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{entry.Value.Type} {entry.Value.Name.CapLower()}");

                    declIndex++;
                }

                sb.AppendLine(")");
                sb.AppendLine("\t\t{");
                generator.Create(sb, entity);
                sb.AppendLine("\t\t}");

                var visibility = IsAbstract(entity) ? "private":   "public";

                sb.AppendLine("");
                sb.AppendLine($"\t\t{visibility} bool Delete{entity.Name}(ulong {entity.Name.CapLower()}ID)");
                sb.AppendLine("\t\t{");
                if (entity.Parent != null)
                {
                    sb.AppendLine($"\t\t\tif (!Delete{entity.Parent.Name}({entity.Name.CapLower()}ID))");
                    sb.AppendLine("\t\t\t{");
                    sb.AppendLine("\t\t\t\treturn false;");
                    sb.AppendLine("\t\t\t}");
                }
                generator.Delete(sb, entity);
                sb.AppendLine("\t\t}");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic {entity.Name}[] Get{entity.Name}Count()");
                sb.AppendLine("\t\t{");
                generator.Count(sb, entity);
                sb.AppendLine("\t\t}");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic {entity.Name}[] Get{entity.Name.Pluralize()}(int page, int count)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tvar offset = page * count;");
                generator.List(sb, entity);
                sb.AppendLine("\t\t}");

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var fieldName = $"{entity.Name.CapLower()}ID";

                        sb.AppendLine("");
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";
                        sb.AppendLine("");
                        sb.AppendLine($"\t\tpublic {reference.Name}[] {methodName}(ulong {fieldName})");
                        sb.AppendLine("\t\t{");
                        generator.Aggregate(sb, reference, entity, fieldName);
                        sb.AppendLine("\t\t}");
                    }
                }
            }

            sb.AppendLine("\t}");

            sb.AppendLine("}");

            File.WriteAllText($"Database.cs", sb.ToString());
        }

        private void DoWebRequest(StringBuilder sb, string url, Action callback)
        {
            sb.AppendLine("\t\t\tStartCoroutine(");
            sb.AppendLine($"\t\t\t\tWebClient.RESTRequest(URL + {url.Replace('\'', '"')}, 0, (error, desc) =>");
            sb.AppendLine("\t\t\t\t{");
            sb.AppendLine("\t\t\t\t\tcallback(null, desc);");
            sb.AppendLine("\t\t\t\t}, (root) =>");
            sb.AppendLine("\t\t\t\t{");
            callback();
            sb.AppendLine("\t\t\t\t});");
        }

        private void DoAuthCheck(StringBuilder sb)
        {
            sb.AppendLine("\t\t\tif (string.IsNullOrEmpty(_authToken))");
            sb.AppendLine("\t\t\t{");
            sb.AppendLine("\t\t\t\tcallback(null, \"Authentication required.\");");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine();
        }

        private void GenerateClient()
        {
            var sb = new StringBuilder();

            generator.Namespaces(sb);

            sb.AppendLine($"namespace {Name}.Model");
            sb.AppendLine("{");

            sb.AppendLine("");
            sb.AppendLine($"\tpublic class {Name}Client");
            sb.AppendLine("\t{");

            sb.AppendLine("");
            sb.AppendLine($"\t\tpublic readonly string URL;");
            sb.AppendLine("\t\tpublic User { get; private set;}");
            sb.AppendLine($"\t\tprivate string _authToken = null;");

            sb.AppendLine("");
            sb.AppendLine($"\t\tpublic {Name}Client(string URL)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tthis.URL = URL;");
            sb.AppendLine("\t\t}");

            sb.AppendLine("");
            sb.AppendLine($"\t\tpublic void LogIn(string creds, Action<User, string> callback)");
            sb.AppendLine("\t\t{");
            DoWebRequest(sb, $"'/login/' + creds", () => {
                sb.AppendLine("\t\t\t\t\tthis._authToken =root.GetString(\"token\");");
                sb.AppendLine("\t\t\t\t\tvar node = root.GetNode(\"user\");");
                sb.AppendLine($"\t\t\t\t\tthis.User = {serializationClass}.UserFromNode(node);");
                sb.AppendLine($"\t\t\t\t\tcallback(this.User, null);");
            });
            
            sb.AppendLine("\t\t}");

            sb.AppendLine("");
            sb.AppendLine($"\t\tpublic void LogOut()");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tthis._authToken = null;");
            sb.AppendLine("\t\t\tthis.User = null;");
            sb.AppendLine("\t\t}");

            foreach (var entity in this.Entities)
            {
                sb.AppendLine("");
                sb.Append($"\t\tpublic void Create{entity.Name}(");

                int declIndex = 0;
                foreach (var entry in entity.Decls)
                {
                    if (declIndex > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append($"{entry.Value.Type} {entry.Value.Name.CapLower()}");

                    declIndex++;
                }

                if (declIndex > 0)
                {
                    sb.Append(", ");
                }

                sb.Append($"Action<{entity.Name}, string> callback");

                sb.AppendLine(")");
                sb.AppendLine("\t\t{");
                DoAuthCheck(sb);
                DoWebRequest(sb, $"'/create/{entity.Name}'", () => {
                    sb.AppendLine($"\t\t\t\t\tvar data = root[{entity.Name.CapLower()}];");
                    sb.AppendLine($"\t\t\t\t\tvar {entity.Name.CapLower()} = {serializationClass}.{entity.Name}FromNode(data);");
                    sb.AppendLine($"\t\t\t\t\tcallback({entity.Name.CapLower()}, null);");
                });
                sb.AppendLine("\t\t}");

                var idName = $"{entity.Name.CapLower()}ID";
                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic void Delete{entity.Name}(ulong {idName}, Action<bool, string> callback)");
                sb.AppendLine("\t\t{");
                DoAuthCheck(sb);
                DoWebRequest(sb, $"'/delete/{entity.Name}/' + {idName}", () => {
                    sb.AppendLine($"\t\t\t\t\tvar result = root.GetBool({entity.Name.CapLower()});");
                    sb.AppendLine($"\t\t\t\t\tcallback(result, null);");
                });
                sb.AppendLine("\t\t}");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic void Count{entity.Name.Pluralize()}(Action<int, string> callback)");
                sb.AppendLine("\t\t{");
                DoAuthCheck(sb);
                DoWebRequest(sb, $"'/count/{entity.Name}'", () => {
                    sb.AppendLine($"\t\t\t\t\tvar count = root.GetInt32({entity.Name.CapLower()});");
                    sb.AppendLine($"\t\t\t\t\tcallback(count, null);");
                });
                sb.AppendLine("\t\t}");

                sb.AppendLine("");
                sb.AppendLine($"\t\tpublic void List{entity.Name.Pluralize()}(int page, int count, Action<{entity.Name}[], string> callback)");
                sb.AppendLine("\t\t{");
                var plural = entity.Name.CapLower().Pluralize();
                DoAuthCheck(sb);
                DoWebRequest(sb, $"'/list/{entity.Name}/'+ page + '/'+ count" , () => {
                    sb.AppendLine($"\t\t\t\t\tvar data = root[{entity.Name.CapLower()}];");
                    sb.AppendLine($"\t\t\t\t\tvar {plural} = new {entity.Name}[data.ChildCount];");
                    sb.AppendLine("\t\t\t\t\tfor (int i=0; i<data.ChildCount; i++) {");
                    sb.AppendLine("\t\t\t\t\t\tvar child = data.GetNodeByIndex(i);");
                    sb.AppendLine($"\t\t\t\t\t\t{plural}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                    sb.AppendLine("\t\t\t\t\t}");
                    sb.AppendLine($"\t\t\t\t\tcallback({plural}, null);");
                });
                sb.AppendLine("\t\t}");

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var fieldName = $"{entity.Name.CapLower()}ID";

                        sb.AppendLine("");
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";
                        sb.AppendLine("");
                        sb.AppendLine($"\t\tpublic {reference.Name}[] {methodName}(ulong {fieldName}, Action<{reference.Name}[], string> callback)");
                        sb.AppendLine("\t\t{");
                        DoAuthCheck(sb);
                        DoWebRequest(sb, $"'/get/{entity.Name}/{reference.Name}'", () => {
                            sb.AppendLine($"\t\t\t\t\tvar data = root[{entity.Name.CapLower().Pluralize()}];");
                            sb.AppendLine($"\t\t\t\t\tvar {plural} = new {entity.Name}[data.ChildCount];");
                            sb.AppendLine("\t\t\t\t\tfor (int i=0; i<data.ChildCount; i++) {");
                            sb.AppendLine("\t\t\t\t\t\tvar child = data.GetNodeByIndex(i);");
                            sb.AppendLine($"\t\t\t\t\t\t{plural}[i] = {serializationClass}.{entity.Name}FromNode(child);");
                            sb.AppendLine("\t\t\t\t\t}");
                            sb.AppendLine($"\t\t\t\t\tcallback({plural}, null);");
                        });
                        sb.AppendLine("\t\t}");
                    }
                }
            }

            sb.AppendLine("\t}");

            sb.AppendLine("}");

            File.WriteAllText($"Client.cs", sb.ToString());

        }

        private void CheckPermissions(StringBuilder sb, string varName, string permission)
        {
            sb.AppendLine($"\t\t\t\tvar permissions = GetPermissions(user, id, \"{varName}\");");
            sb.AppendLine($"\t\t\t\tif (!permissions.HasFlag(Permissions.{permission}))");
            sb.AppendLine("\t\t\t\t{");
            sb.AppendLine($"\t\t\t\t\treturn Error(\"Cannot {permission.ToLower()} {varName}, permission denied\");");
            sb.AppendLine("\t\t\t\t}");
        }

        private void GenerateServer()
        {
            var sb = new StringBuilder();

            sb.AppendLine($"using System;");
            sb.AppendLine();

            sb.AppendLine($"namespace {Name}.Model");
            sb.AppendLine("{");

            sb.AppendLine("");
            sb.AppendLine("\tpublic enum Permissions");
            sb.AppendLine("\t{");
            sb.AppendLine("\t\tNone = 0,");
            sb.AppendLine("\t\tRead = 1,");
            sb.AppendLine("\t\tWrite = 2,");
            sb.AppendLine("\t\tCreate = 4,");
            sb.AppendLine("\t\tDelete = 8,");
            sb.AppendLine("\t}");

            sb.AppendLine("");
            sb.AppendLine($"\tpublic abstract class Base{Name}Server: HTTPServer");
            sb.AppendLine("\t{");

            sb.AppendLine($"\tpublic readonly {Name}Database Database;");

            sb.AppendLine("");
            sb.AppendLine($"\t\tpublic {Name}Server({Name}Database database, ServerSettings settings, LoggerCallback log = null, SessionStorage sessionStorage = null): base(setings, log, sessionStorage)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tthis.Database = database;");

            foreach (var entity in this.Entities)
            {
                var varName = entity.Name.CapLower();

                sb.AppendLine("");
                sb.AppendLine($"\t\t\tthis.Post(\"/{varName}/create/\", (request) =>");
                sb.AppendLine("\t\t\t{");
                CheckPermissions(sb, varName, "Create");
                string fields = "";
                foreach (var entry in entity.Decls)
                {
                    if (!string.IsNullOrEmpty(fields))
                    {
                        fields = fields + ',';
                    }

                    var fieldName = entry.Value.Name.CapLower();
                    fields += fieldName;
                    sb.AppendLine($"\t\t\t\tvar {fieldName} = request.GetVariable(\"{entry.Value.Name}\");");
                }
                sb.AppendLine($"\t\t\t\tvar {varName} = Database.Create{entity.Name}({fields});");
                sb.AppendLine($"\t\t\t\tif ({varName}  == null)");
                sb.AppendLine("\t\t\t\t{");
                sb.AppendLine($"\t\t\t\t\treturn Error(\"{entity.Name} creation failed\");");
                sb.AppendLine("\t\t\t\t}");
                sb.AppendLine($"\t\t\t\treturn {serializationClass}.{entity.Name}ToNode({varName});");
                sb.AppendLine("\t\t\t});");

                sb.AppendLine("");
                sb.AppendLine($"\t\t\tthis.Post(\"/{varName}/delete/\", (request) =>");
                sb.AppendLine("\t\t\t{");
                sb.AppendLine($"\t\t\t\tvar id = request.GetVariable(\"id\");");
                CheckPermissions(sb, varName, "Delete");
                sb.AppendLine($"\t\t\t\tvar result = Database.Delete{entity.Name}(id);");
                sb.AppendLine($"\t\t\t\treturn result.ToString();");
                sb.AppendLine("\t\t\t});");

                sb.AppendLine("");
                sb.AppendLine($"\t\t\tthis.Get(\"/{varName}/count/\", (request) =>");
                sb.AppendLine("\t\t\t{");
                CheckPermissions(sb, varName, "Read");
                sb.AppendLine($"\t\t\t\tvar result = Database.Count{entity.Name.Pluralize()}(id);");
                sb.AppendLine($"\t\t\t\treturn result.ToString();");
                sb.AppendLine("\t\t\t});");

                sb.AppendLine("");
                sb.AppendLine($"\t\t\tthis.Get(\"/{varName}/list/" + "{page}/{count}" + "\", (request) =>");
                sb.AppendLine("\t\t\t{");
                CheckPermissions(sb, varName, "Read");
                sb.AppendLine($"\t\t\t\tvar page = request.GetVariable(\"page\");");
                sb.AppendLine($"\t\t\t\tvar count  = request.GetVariable(\"count\");");
                sb.AppendLine($"\t\t\t\tvar result = Database.List{entity.Name.Pluralize()}(page, count);");
                sb.AppendLine($"\t\t\t\treturn result.ToDataNode();");
                sb.AppendLine("\t\t\t});");

                var refs = GetReferences(entity);
                if (refs.Any())
                {
                    foreach (var reference in refs)
                    {
                        var methodName = $"Get{reference.Name.Pluralize()}Of{entity.Name}";

                        sb.AppendLine("");
                        sb.AppendLine($"\t\t\tthis.Get(\"/{varName}/{reference.Name.CapLower().Pluralize()}/" + "{id}" + "\",  (request) =>");
                        sb.AppendLine("\t\t\t{");
                        CheckPermissions(sb, varName, "Read");
                        sb.AppendLine($"\t\t\t\tvar id = request.GetVariable(\"id\");");
                        sb.AppendLine($"\t\t\t\tvar result = Database.{methodName}(id);");
                        sb.AppendLine($"\t\t\t\treturn result.ToDataNode();");
                        sb.AppendLine("\t\t\t});");
                    }
                }
            }

            sb.AppendLine("\t\t}");

            sb.AppendLine("");
            sb.AppendLine("\t\tpublic DataNode Error(string msg)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tvar result = DataNode.CreateObject(\"result\");");
            sb.AppendLine("\t\t\tresult.AddField(\"error\", msg);");
            sb.AppendLine("\t\t\treturn result;");
            sb.AppendLine("\t\t}");

            sb.AppendLine("");
            sb.AppendLine("\t\tpublic abstract User Authenticate(string creds);");
            sb.AppendLine("\t\tpublic abstract Permissions GetPermissions(User user, ulong targetID, string scheme);");

            sb.AppendLine("\t}");
            sb.AppendLine("}");

            File.WriteAllText($"Server.cs", sb.ToString());

        }

    }
}
