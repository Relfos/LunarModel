﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LunarModel
{
    public enum TokenKind
    {
        Separator,
        Operator,
        Selector,
        Identifier,
        String,
        Number,
        Decimal,
        Bool,
    }

    public struct LexerToken
    {
        public readonly int column;
        public readonly int line;
        public readonly string value;
        public readonly TokenKind kind;

        public LexerToken(int column, int line, string value)
        {
            this.column = column;
            this.line = line;
            this.value = value;

            decimal decval;
            ulong longval;

            if (value == "true" || value == "false")
            {
                this.kind = TokenKind.Bool;
            }
            else
            if (value == ".")
            {
                this.kind = TokenKind.Selector;
            }
            else
            if (value == ":=" || value == "and" || value == "or" || value == "xor")
            {
                this.kind = TokenKind.Operator;
            }
            else
            if (ulong.TryParse(value, out longval))
            {
                this.kind = TokenKind.Number;
            }
            else
            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out decval))
            {
                this.kind = TokenKind.Decimal;
            }
            else
            {
                var first = value.Length > 0 ? value[0] : '\0';

                switch (first)
                {
                    case '\"':
                        this.kind = TokenKind.String;
                        break;

                    default:
                        if (Lexer.IsSeparatorSymbol(first))
                        {
                            this.kind = TokenKind.Separator;
                        }
                        else
                        if (Lexer.IsOperatorSymbol(first))
                        {
                            this.kind = TokenKind.Operator;
                        }
                        else
                        {
                            this.kind = TokenKind.Identifier;
                        }
                        break;
                }
            }

        }

        public override string ToString()
        {
            return $"{kind} => {value}";
        }
    }

    public static class Lexer
    {
        public readonly static string[] VarTypeNames = new string[]
        {
            "String",
            "Bool",
            "Bytes",
            "Decimal",
            "Int16",
            "UInt16",
            "Int32",
            "UInt32",
            "Int64",
            "UInt64",
        };

        internal static bool IsOperatorSymbol(char ch)
        {
            switch (ch)
            {
                case '=':
                case '+':
                case '-':
                case '*':
                case '/':
                case '%':
                case '<':
                case '>':
                case '!':
                case ':':
                //case '&': TODO offset of method
                case '^':
                    return true;

                default:
                    return false;
            }
        }

        internal static bool IsSeparatorSymbol(char ch)
        {
            switch (ch)
            {
                case ';':
                case ',':
                case '{':
                case '}':
                case '(':
                case ')':
                case '[':
                case ']':
                case '.':
                    return true;

                default:
                    return false;
            }
        }

        public enum CommentMode
        {
            None,
            Single,
            Multi
        }

        public static List<LexerToken> Process(string sourceCode)
        {
            var tokens = new List<LexerToken>();
            int i = 0;

            int tokenX = 0;
            int tokenY = 0;

            int col = 0;
            int line = 1;
            var sb = new StringBuilder();

            bool insideString = false;
            bool insideNumber = false;
            var insideComment = CommentMode.None;

            LexerToken prevToken = new LexerToken(0, 0, "");
            LexerToken curToken = prevToken;

            int lastType = -1;
            char lastChar = '\0';
            while (i < sourceCode.Length)
            {
                var ch = sourceCode[i];
                i++;
                col++;

                if (insideString)
                {
                    sb.Append(ch);

                    if (ch == '"')
                    {
                        insideString = false;
                    }

                    continue;
                }
                else
                if (insideComment == CommentMode.Single)
                {
                    if (ch == '\n')
                    {
                        insideComment = CommentMode.None;
                    }
                    else
                    {
                        continue;
                    }
                }
                else
                if (insideComment == CommentMode.Multi)
                {
                    if (ch == '/' && lastChar == '*')
                    {
                        insideComment = CommentMode.None;
                    }
                    else
                    {
                        if (ch == '\n')
                        {
                            line++;
                        }
                        lastChar = ch;
                    }
                    continue;
                }

                if (ch == '/' && lastChar == ch)
                {
                    sb.Length--;
                    insideComment = CommentMode.Single;
                    continue;
                }

                if (ch == '*' && lastChar == '/')
                {
                    sb.Length--;
                    insideComment = CommentMode.Multi;
                    continue;
                }

                switch (ch)
                {
                    case '\t':
                    case ' ':
                        {
                            lastType = -1;
                            break;
                        }


                    case '\r':
                        break;

                    case '\n':
                        col = 0;
                        line++;
                        break;

                    default:
                        int curType;

                        if (ch == '/')
                        {
                            curType = 0;
                        }

                        if (ch == '\"')
                        {
                            insideString = true;
                            curType = 0;
                        }
                        else
                        if (insideString)
                        {
                            curType = 0;
                        }
                        else
                        if (IsSeparatorSymbol(ch) && (ch != '.' || !insideNumber))
                        {
                            curType = 2;
                        }
                        else
                        if (IsOperatorSymbol(ch))
                        {
                            curType = 1;
                        }
                        else
                        {
                            curType = 0;
                        }

                        if (sb.Length > 0 && curType != lastType)
                        {
                            var val = sb.ToString();

                            prevToken = curToken;
                            curToken = new LexerToken(tokenX, tokenY, val);
                            tokens.Add(curToken);
                            sb.Clear();

                            insideNumber = false;
                        }

                        if (sb.Length == 0)
                        {
                            tokenX = col;
                            tokenY = line;
                        }


                        if (sb.Length == 0 && char.IsDigit(ch))
                        {
                            insideNumber = true;
                        }

                        sb.Append(ch);

                        if (curType == 2)
                        {
                            curType = -1;
                        }

                        lastType = curType;
                        break;
                }

                lastChar = ch;
            }

            if (sb.Length > 0)
            {
                var val = sb.ToString();
                tokens.Add(new LexerToken(tokenX, tokenY, val));
            }

            return tokens;
        }
    }
}
