using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NetLisp.Backend
{

public enum Token { None, EOF, Literal, Symbol, Vector, LParen, RParen, Quote, BackQuote, Comma, Splice, Period }

public sealed class Parser
{ public Parser(Stream data) : this("<unknown>", data) { }
  public Parser(string source, Stream data) : this(source, new StreamReader(data), false) { }
  public Parser(string source, TextReader data) : this(source, data, false) { }
  public Parser(string source, TextReader data, bool autoclose)
  { sourceFile = source; this.data = data.ReadToEnd();
    if(autoclose) data.Close();
    NextToken();
  }
  public Parser(string source, string data)
  { sourceFile=source; this.data=data;
    NextToken();
  }
  
  public static Parser FromFile(string filename) { return new Parser(filename, new StreamReader(filename), true); }
  public static Parser FromStream(Stream stream) { return new Parser("<stream>", new StreamReader(stream)); }
  public static Parser FromString(string text) { return new Parser("<string>", text); }

  public object Parse()
  { Pair list=null, tail=null;

    while(token!=Token.EOF)
    { object item = ParseOne();
      Pair next = new Pair(item, null);
      if(tail==null) list=tail=next;
      else { tail.Cdr=next; tail=next; }
    }
    if(list==null) return null;
    if(list.Cdr==null) return list.Car;
    else return new Pair(Symbol.Get("begin"), list);
  }

  public object ParseOne()
  { switch(token)
    { case Token.LParen:
        if(NextToken()==Token.RParen) { NextToken(); return null; }
        else
        { ArrayList items = new ArrayList();
          object dot = null;
          do
          { items.Add(ParseOne());
            if(TryEat(Token.Period)) { dot=ParseOne(); break; }
          }
          while(token!=Token.RParen && token!=Token.EOF);
          if(items.Count==0 && dot!=null) throw SyntaxError("malformed dotted list");
          Eat(Token.RParen);
          return Ops.DottedList(dot, (object[])items.ToArray(typeof(object)));
        }
      case Token.Symbol:
      { object val=value;
        NextToken();
        return Symbol.Get((string)val);
      }
      case Token.Literal:
      { object val=value;
        NextToken();
        return val;
      }
      case Token.BackQuote: NextToken(); return Ops.List(Symbol.Get("quasiquote"), ParseOne());
      case Token.Comma: NextToken(); return Ops.List(Symbol.Get("unquote"), ParseOne());
      case Token.Quote: NextToken(); return Ops.List(Symbol.Get("quote"), ParseOne());
      case Token.Splice: NextToken(); return Ops.List(Symbol.Get("unquote-splicing"), ParseOne());
      case Token.Vector:
      { ArrayList items = new ArrayList();
        NextToken();
        while(!TryEat(Token.RParen)) items.Add(ParseOne());
        return Ops.List2(Symbol.Get("vector"), (object[])items.ToArray(typeof(object)));
      }
      case Token.EOF: return EOF;
      default: throw SyntaxError("unexpected token: "+token);
    }
  }
  
  public static readonly object EOF = new Singleton("<EOF>");

  void Eat(Token type) { if(token!=type) Unexpected(token, type); NextToken(); }
  void Expect(Token type) { if(token!=type) Unexpected(token); }

  /*
  \newline  Ignored
  \\        Backslash
  \"        Double quotation mark
  \'        Single quotation mark
  \n        Newline
  \t        Tab
  \r        Carriage return
  \b        Backspace
  \e        Escape
  \a        Bell
  \f        Form feed
  \v        Vertical tab
  \xHH      Up to 2 hex digits -> byte value
  \uHHHH    Up to 4 hex digits -> 16-bit unicode value
  \cC       Control code (eg, \cC is ctrl-c)
  \OOO      Up to 3 octal digits -> byte value
  */
  char GetEscapeChar()
  { char c = ReadChar();
    if(char.IsDigit(c))
    { int num = (c-'0');
      for(int i=1; i<3; i++)
      { c = ReadChar();
        if(!char.IsDigit(c)) { lastChar=c; break; }
        num = num*10 + (c-'0');
      }
      if(num>255) throw SyntaxError("character value out of range");
      return (char)num;
    }
    else switch(c)
    { case '\"': return '\"';
      case '\'': return '\'';
      case 'n':  return '\n';
      case 't':  return '\t';
      case 'r':  return '\r';
      case 'b':  return '\b';
      case 'e':  return (char)27;
      case 'a':  return '\a';
      case 'f':  return '\f';
      case 'v':  return '\v';
      case '\\': return '\\';
      case 'x': case 'u':
      { int num = 0;
        for(int i=0,limit=(c=='x'?2:4); i<limit; i++)
        { c = ReadChar();
          if(char.IsDigit(c)) num = (num<<4) | (c-'0');
          else if((c<'A' || c>'F') || (c<'a' || c>'f'))
          { if(i==0) throw SyntaxError("expected hex digit");
            lastChar = c;
            break;
          }
          else num = (num<<4) | (char.ToUpper(c)-'A'+10);
        }
        return (char)num;
      }
      case 'c':
        c = char.ToUpper(ReadChar());
        if(c>96 || c<65) throw SyntaxError("expected control character, but received '{0}'", c);
        return (char)(c-64);
      default: throw SyntaxError("unknown escape character '{0}' (0x{1})", c, Ops.ToHex((uint)c, 2));
    }
  }

  Token NextToken()
  { if(nextToken!=Token.None)
    { token = nextToken;
      nextToken = Token.None;
    }
    else token = ReadToken();
    return token;
  }

  char ReadChar()
  { char c;
    if(lastChar!=0) { c=lastChar; lastChar='\0'; return c; }
    else if(pos>=data.Length) return '\0';
    c = data[pos++]; column++;
    if(c=='\n') { line++; column=1; }
    else if(c=='\r')
    { if(pos<data.Length && data[pos]=='\n') pos++;
      c='\n'; line++; column=1;
    }
    else if(c==0) c = ' ';
    return c;
  }

  object ReadNumber(char c)
  { StringBuilder sb = new StringBuilder();
    int radix=0;
    char exact='\0';
    bool ident=false, pastFlags=!char.IsLetter(c);

    do
    { if(pastFlags)
      { sb.Append(c);
        if(radix==0 && !char.IsDigit(c) && c!='.' && (c!='-' || sb.Length>1)) ident=true;
      }
      else
        switch(c)
        { case 'b': radix=2; break;
          case 'o': radix=8; break;
          case 'd': radix=10; break;
          case 'x': radix=16; break;
          case 'e': case 'i': exact=c; break;
          default: pastFlags=true; continue;
        }
      c = ReadChar();
    } while(c!=0 && !IsDelimiter(c));
    lastChar = c;

    if(sb.Length==1)
    { if(sb[0]=='.') return Token.Period;
      if(sb[0]=='-') return "-";
    }

    string str = sb.ToString();
    Match m = numRegex.Match(str);
    if(ident)
    { if(!m.Success) return str;
      if(m.Groups[1].Success) throw new NotImplementedException("complex numbers"); // TODO: add this
    }
    else if(!m.Success) throw SyntaxError("invalid number");

    if(str.IndexOf('.')!=-1) return exact=='e' ? Builtins.inexactToExact.core(double.Parse(str)) : double.Parse(str);
    else
    { m = fracRegex.Match(str);
      if(m.Success) throw new NotImplementedException("fractions");

      if(radix==0) radix=10;
      try { return Convert.ToInt32(str, radix); }
      catch(OverflowException)
      { try { return Convert.ToInt64(str, radix); }
        catch(OverflowException) { return new Integer(str, radix); }
      }
    }
  }

  Token ReadToken()
  { char c;

    while(true)
    { do c=ReadChar(); while(c!=0 && char.IsWhiteSpace(c));
      
      if(char.IsDigit(c) || c=='.' || c=='-')
      { value = ReadNumber(c);
        return value is Token ? (Token)value : value is string ? Token.Symbol : Token.Literal;
      }
      else if(c=='#')
      { c = ReadChar();
        switch(c)
        { case 't': value=true; return Token.Literal;
          case 'f': value=false; return Token.Literal;
          case '\\':
          { value = c = ReadChar();
            if(char.IsLetter(c))
            { StringBuilder sb = new StringBuilder();
              do
              { sb.Append(c);
                c = ReadChar();
              } while(c!=0 && !IsDelimiter(c));
              lastChar=c;

              try { value = Builtins.nameToChar.core(sb.ToString()); }
              catch(ValueErrorException e) { throw SyntaxError(e.Message); }
            }
            return Token.Literal;
          }
          case '%':
          { StringBuilder sb = new StringBuilder();
            sb.Append("#%");
            while(true)
            { c = ReadChar();
              if(IsDelimiter(c)) { lastChar=c; break; }
              sb.Append(c);
            }
            value = sb.ToString();
            return Token.Symbol;
          }
          case '(': return Token.Vector;
          case '|':
            while(true)
            { c = ReadChar();
              if(c=='|')
              { c = ReadChar();
                if(c=='#') break;
              }
              if(c==0) throw SyntaxError("unterminated extended comment");
            }
            break;
          case 'b': case 'o': case 'd': case 'x': case 'i': case 'e':
            value=ReadNumber(c); return Token.Literal;
          case '<': throw SyntaxError("unable to read: #<...");
          default: throw SyntaxError("unknown notation: #"+c);
        }
      }
      else if(c=='"')
      { StringBuilder sb = new StringBuilder();
        while(true)
        { c = ReadChar();
          if(c=='"') break;
          if(c==0) throw SyntaxError("unterminated string constant");
          if(c=='\\') c=GetEscapeChar();
          sb.Append(c);
        }
        value = sb.ToString();
        return Token.Literal;
      }
      else switch(c)
      { case '`': return Token.BackQuote;
        case ',':
        { c = ReadChar();
          if(c=='@') return Token.Splice;
          else { lastChar=c; return Token.Comma; }
        }
        case '(': return Token.LParen;
        case ')': return Token.RParen;
        case '\'': return Token.Quote;
        case '\0': return Token.EOF;
        case ';': while((c=ReadChar())!='\n' && c!=0); break;
        default:
        { StringBuilder sb = new StringBuilder();
          do
          { sb.Append(c);
            c = ReadChar();
          } while(c!=0 && !IsDelimiter(c));
          lastChar = c;
          string str = sb.ToString();
          if(str=="nil") { value=null; return Token.Literal; }
          else { value=str; return Token.Symbol; }
        }
      }
    }
  }

  SyntaxErrorException SyntaxError(string message)
  { return new SyntaxErrorException(string.Format("{0}({1},{2}): {3}", sourceFile, line, column, message));
  }
  SyntaxErrorException SyntaxError(string format, params object[] args)
  { return SyntaxError(string.Format(format, args));
  }

  bool TryEat(Token type)
  { if(token==type) { NextToken(); return true; }
    return false;
  }
  
  void Unexpected(Token token) { SyntaxError("unexpected token {0}", token, sourceFile, line, column); }
  void Unexpected(Token got, Token expect)
  { SyntaxError("unexpected token {0} (expecting {1})", got, expect, sourceFile, line, column);
  }

  string sourceFile, data;
  Token  token=Token.None, nextToken=Token.None;
  object value;
  int    line=1, column=1, pos;
  char   lastChar;

  static bool IsDelimiter(char c) { return char.IsWhiteSpace(c) || c=='(' || c==')' || c=='#' || c=='`' || c==',' || c=='\''; }
  
  static readonly Regex numRegex = new Regex(@"^(-?\d*(?:\.\d+)?)(?:\+(-?\d*(?:\.\d+)?)j)?$", RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.Singleline);
  static readonly Regex fracRegex = new Regex(@"^(-?\d+)/(-?\d+)$", RegexOptions.Compiled|RegexOptions.Singleline);
}

} // namespace NetLisp.Backend