using System;
using System.Collections.Generic;
using System.Linq;

namespace LunarModel
{
    public class CompilerException : Exception
    {
        public CompilerException(string msg) : base($"line {Compiler.Instance.CurrentLine}: {msg}")
        {

        }

        public CompilerException(Node node, string msg) : base($"line {node.LineNumber}: {msg}")
        {

        }
    }

    public abstract class Node
    {
        public int LineNumber;
        public int Column;
        public string NodeID;

        public Node()
        {
            if (Compiler.Instance != null)
            {
                this.LineNumber = Compiler.Instance.CurrentLine;
                this.Column = Compiler.Instance.CurrentColumn;
                this.NodeID = this.GetType().Name.ToLower() + Compiler.Instance.AllocateLabel();
            }
            else
            {
                this.LineNumber = -1;
                this.Column = -1;
                this.NodeID = this.GetType().Name.ToLower();
            }
        }
    }

    public class Compiler
    {
        private List<LexerToken> tokens;
        private int tokenIndex = 0;

        public int CurrentLine { get; private set; }
        public int CurrentColumn { get; private set; }

        private string[] lines;

        public static Compiler Instance { get; private set; }

        public Compiler()
        {
            Instance = this;
        }

        private void InitTypes()
        {
            _types.Clear();

            foreach (var type in Lexer.VarTypeNames)
            {
                CreateType(type);
            }
        }
        private void CreateType(string name)
        {
            _types[name] = new TypeDeclaration(name);
        }

        private void InitEnums()
        {
            _enums.Clear();
            //CreateEnum<TokenFlags>("TokenFlags");
        }

        private void CreateEnum<T>(string name)
        {
            CreateEnum(name, typeof(T));
        }

        private void CreateEnum(string enumName, Type enumType)
        {
            var tokenFlagsNames = Enum.GetNames(enumType).Cast<string>().ToArray();
            var tokenFlagsEntries = new List<EnumEntry>();
            foreach (var name in tokenFlagsNames)
            {
                var temp = Enum.Parse(enumType, name);
                var value = Convert.ToUInt32(temp);
                tokenFlagsEntries.Add(new EnumEntry(name, value));
            }
            var tokenFlagsDecl = new EnumDeclaration(enumName, tokenFlagsEntries);
            _enums[tokenFlagsDecl.Name] = tokenFlagsDecl;
        }

        private void InitStructs()
        {
            _entities.Clear();

/*            CreateStruct("NFT", new[]
            {
                new StructField("chain", VarKind.Address),
                new StructField("owner", VarKind.Address),
                new StructField("creator", VarKind.Address),
                new StructField("ROM", VarKind.Bytes),
                new StructField("RAM", VarKind.Bytes),
                new StructField("seriesID", VarKind.Number),
                new StructField("mintID", VarKind.Number),
            });*/
        }

        private void CreateEntity(string entityName, string parentName, IEnumerable<EntityField> fields)
        {
            var decl = new EntityDeclaration(entityName, parentName, fields);
            _entities[entityName] = decl;
        }

        private void Rewind(int steps = 1)
        {
            tokenIndex -= steps;
            if (tokenIndex < 0)
            {
                throw new CompilerException("unexpected rewind");
            }
        }

        private int currentLabel = 0;

        public int AllocateLabel()
        {
            currentLabel++;
            return currentLabel;
        }

        private bool HasTokens()
        {
            return tokenIndex < tokens.Count;
        }

        private LexerToken FetchToken()
        {
            if (tokenIndex >= tokens.Count)
            {
                throw new CompilerException("unexpected end of file");
            }

            var token = tokens[tokenIndex];
            tokenIndex++;

            this.CurrentLine = token.line;
            this.CurrentColumn = token.column;

            //Console.WriteLine(token);
            return token;
        }

        private void ExpectToken(string val, string msg = null)
        {
            var token = FetchToken();

            if (token.value != val)
            {
                throw new CompilerException(msg != null ? msg : ("expected " + val));
            }
        }

        private string ExpectKind(TokenKind expectedKind)
        {
            var token = FetchToken();

            if (token.kind != expectedKind)
            {
                throw new CompilerException($"expected {expectedKind}, got {token.kind} instead");
            }

            return token.value;
        }

        private string ExpectIdentifier()
        {
            return ExpectKind(TokenKind.Identifier);
        }

        private string ExpectString()
        {
            return ExpectKind(TokenKind.String);
        }

        private string ExpectNumber()
        {
            return ExpectKind(TokenKind.Number);
        }

        private string ExpectBool()
        {
            return ExpectKind(TokenKind.Bool);
        }

        private TypeDeclaration ExpectType()
        {
            var token = FetchToken();

            if (token.kind != TokenKind.Identifier)
            {
                throw new CompilerException("expected type, got " + token.kind);
            }

            if (_entities.ContainsKey(token.value))
            {
                return _entities[token.value];
            }

            if (_enums.ContainsKey(token.value))
            {
                return _enums[token.value];
            }

            if (_types.ContainsKey(token.value))
            {
                return _types[token.value];
            }

            throw new CompilerException("unknown type: " + token.value);
        }

        public string GetLine(int index)
        {
            if (index <= 0 || index > lines.Length)
            {
                return "";
            }

            return lines[index - 1];
        }

        private Dictionary<string, EntityDeclaration> _entities = new Dictionary<string, EntityDeclaration>();
        private Dictionary<string, EnumDeclaration> _enums = new Dictionary<string, EnumDeclaration>();
        private Dictionary<string, TypeDeclaration> _types = new Dictionary<string, TypeDeclaration>();

        public Model Process(string modelName, string sourceCode, Generator generator)
        {
            this.tokens = Lexer.Process(sourceCode);

            /*foreach (var token in tokens)
            {
                Console.WriteLine(token);
            }*/

            this.lines = sourceCode.Replace("\r", "").Split('\n');

            InitTypes();
            InitEnums();
            InitStructs();

            var model = new Model(modelName, generator);

            var parents = new Dictionary<string, EnumDeclaration>();

            while (HasTokens())
            {
                var firstToken = FetchToken();

                switch (firstToken.value)
                {
                    case "entity":
                        {
                            var structName = ExpectIdentifier();

                            string parentName = null;

                            var nextToken = FetchToken();
                            if (nextToken.value == ":")
                            {
                                parentName = ExpectIdentifier();

                                EnumDeclaration parentDecl;
                                var key = parentName + "Kind";
                                if (_enums.ContainsKey(key))
                                {
                                    parentDecl = _enums[key];
                                }
                                else
                                {
                                    parentDecl = new EnumDeclaration(key, Enumerable.Empty<EnumEntry>());
                                    _enums[parentDecl.Name] = parentDecl;
                                }

                                parentDecl.entryNames[structName] = (uint)parentDecl.entryNames.Count;
                            }
                            else
                            {
                                Rewind();
                            }

                            var fields = new List<EntityField>();

                            ExpectToken("{");
                            do
                            {
                                var next = FetchToken();
                                if (next.value == "}")
                                {
                                    break;
                                }

                                Rewind();

                                var fieldName = ExpectIdentifier();
                                ExpectToken(":");

                                var fieldType = ExpectType();

                                var flags = FieldFlags.None;
                                int flagCount = 0;

                                var sep = FetchToken();
                                if (sep.value == "[")
                                {
                                    do
                                    {
                                        var temp = FetchToken();
                                        if (temp.value == "]")
                                        {
                                            break;
                                        }
                                        else
                                        {
                                            if (flagCount > 0)
                                            {
                                                Rewind();
                                                ExpectToken(",");
                                                temp = FetchToken();
                                            }
                                        }

                                        FieldFlags flag;

                                        if (!Enum.TryParse<FieldFlags>(temp.value, out flag))
                                        {
                                            throw new CompilerException("Invalid field flag: " + temp.value);
                                        }

                                        flags |= flag;
                                        flagCount++;
                                    } while (true);
                                    
                                }
                                else
                                {
                                    Rewind();
                                }

                                ExpectToken(";");


                                /*if ((fieldType is EntityDeclaration) && flags.HasFlag(FieldFlags.Editable))
                                {
                                    throw new CompilerException($"field {fieldName} can't be editable");
                                }*/

                                fields.Add(new EntityField(fieldName, fieldType, flags));
                            } while (true);

                            CreateEntity(structName, parentName, fields);
                            break;
                        }

                    case "enum":
                        {
                            var enumName = ExpectIdentifier();

                            var entries = new List<EnumEntry>();

                            ExpectToken("{");
                            do
                            {
                                var next = FetchToken();
                                if (next.value == "}")
                                {
                                    break;
                                }

                                Rewind();

                                if (entries.Count > 0)
                                {
                                    ExpectToken(",");
                                }

                                var entryName = ExpectIdentifier();

                                next = FetchToken();

                                string enumValueStr;

                                if (next.value == "=")
                                {
                                    enumValueStr = ExpectNumber();
                                }
                                else
                                {
                                    enumValueStr = entries.Count.ToString();
                                    Rewind();
                                }


                                uint entryValue;
                                if (!uint.TryParse(enumValueStr, out entryValue))
                                {
                                    throw new CompilerException($"Invalid enum value for {entryName} => {enumValueStr}");
                                }

                                entries.Add(new EnumEntry(entryName, entryValue));
                            } while (true);


                            var decl = new EnumDeclaration(enumName, entries);
                            _enums[enumName] = decl;

                            break;
                        }

                    default:
                        throw new CompilerException("Unexpected token: " + firstToken.value);
                }
            }

            foreach (var entry in _enums.Values)
            {
                model.Enums.Add(new Enumerate(entry.Name, entry.entryNames.Keys));
            }

            foreach (var entry in _entities.Values)
            {
                model.Entities.Add(new Entity(entry.Name, entry.Parent != null ? model.Entities.Where(x => x.Name == entry.Parent).FirstOrDefault() : null, entry.fields.Select(x => new Field(x.name, x.type.Name, x.flags))));
            }

            return model;
        }

    }
}
