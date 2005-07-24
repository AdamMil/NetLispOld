/*
NetLisp is the reference implementation for a language similar to
Scheme, also called NetLisp. This implementation is both interpreted
and compiled, targetting the Microsoft .NET Framework.

http://www.adammil.net/
Copyright (C) 2005 Adam Milazzo

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

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
      if(list==null) list=tail=next;
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

  object ParseInt(string str, int radix)
  { if(str=="") return 0;
    try { return Convert.ToInt32(str, radix); }
    catch(OverflowException)
    { try { return Convert.ToInt64(str, radix); }
      catch(OverflowException) { return new Integer(str, radix); }
    }
  }

  double ParseNum(string str, int radix)
  { if(radix==10) return double.Parse(str);
    else
    { int pos = str.IndexOf('.');
      if(pos==-1) return Convert.ToDouble(ParseInt(str, radix));
      double whole=Convert.ToDouble(ParseInt(str.Substring(0, pos), radix));
      double  part=Convert.ToDouble(ParseInt(str.Substring(pos+1), radix));
      return whole + part/radix;
    }
  }

  object ParseNum(string str, string exp, int radix, char exact)
  { double num = ParseNum(str, radix);
    if(exp!="") num *= Math.Pow(10, ParseNum(exp, radix));

    if(Math.IEEERemainder(num, 1)==0) // integer
    { if(exact=='i') return num;
      try { return checked((int)num); }
      catch(OverflowException)
      { try { return checked((long)num); }
        catch(OverflowException) { return new Integer(num); }
      }
    }
    else 
    { if(exact=='e') throw new NotImplementedException("rationals");
      return num;
    }
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
    sb.Append(c);
    while(!IsDelimiter(c=ReadChar())) sb.Append(c);
    lastChar = c;

    if(sb.Length==1)
    { if(sb[0]=='.') return Token.Period;
      if(sb[0]=='-') return "-";
      if(sb[0]=='+') return "+";
    }

    string str = sb.ToString();
    int radix  = 10;
    char exact = '\0';
    bool hasFlags=false;

    if(str[0]=='#')
    { int start;
      for(start=1; start<str.Length; start++)
        switch(str[start])
        { case 'b': radix=2; break;
          case 'o': radix=8; break;
          case 'd': radix=10; break;
          case 'x': radix=16; break;
          case 'e': case 'i': exact=str[start]; break;
          default: goto done;
        }
      done:
      str = str.Substring(start);
      hasFlags = true;
    }

    Match m = (radix==10 ? decNum : radix==16 ? hexNum : radix==8 ? octNum : binNum).Match(str);
    if(!m.Success)
    { if(hasFlags) throw SyntaxError("invalid number: "+sb.ToString());
      else return str;
    }

    if(m.Groups["den"].Success)
    { if(exact!='i') throw new NotImplementedException("rationals");
      return Convert.ToDouble(ParseInt(m.Groups["num"].Value, radix)) /
             Convert.ToDouble(ParseInt(m.Groups["den"].Value, radix));
    }

    object num = ParseNum(m.Groups["num"].Value, m.Groups["exp"].Value, radix, exact);

    if(m.Groups["imag"].Success)
    { if(exact=='e') throw new NotImplementedException("exact complexes");
      return new Complex(Convert.ToDouble(num),
                         Convert.ToDouble(ParseNum(m.Groups["imag"].Value, m.Groups["imagexp"].Value, radix, exact)));
    }
    
    return num;
  }

  Token ReadToken()
  { char c;

    while(true)
    { do c=ReadChar(); while(c!=0 && char.IsWhiteSpace(c));

      if(char.IsDigit(c) || c=='.' || c=='-' || c=='+')
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
          case '%': case '_':
          { StringBuilder sb = new StringBuilder();
            sb.Append('#').Append(c);
            while(true)
            { c = ReadChar();
              if(IsDelimiter(c)) { lastChar=c; break; }
              sb.Append(c);
            }
            value = sb.ToString();
            return Token.Symbol;
          }
          case '"': case '\'':
          { char delim = c;
            StringBuilder sb = new StringBuilder();
            while(true)
            { c = ReadChar();
              if(c==delim)
              { c = ReadChar();
                if(c!=delim) { lastChar=c; break; }
              }
              sb.Append(c);
            }
            value = sb.ToString();
            return Token.Literal;
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
            lastChar=c; value=ReadNumber('#'); return Token.Literal;
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

  static bool IsDelimiter(char c) { return char.IsWhiteSpace(c) || c=='(' || c==')' || c=='#' || c=='`' || c==',' || c=='\'' || c=='\0'; }

  static readonly Regex binNum =
    new Regex(@"^(:?(?<num>[+-]?(?:[01]+(?:\.[01]*)?|\.[01]+))(?:e(?<exp>[+-]?(?:[01]+(?:\.[01]*)?|\.[01]+)))?
                   (?:(?<imag>[+-]?(?:[01]+(?:\.[01]*)?|\.[01]+))(?:e(?<imagexp>[+-]?(?:[01]+(?:\.[01]*)?|\.[01]+)))?i)?
                   |
                   (?<num>[+-]?[01]+)/(?<den>[+-]?[01]+)
                 )$",
              RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.IgnorePatternWhitespace|RegexOptions.Singleline);

  static readonly Regex octNum =
    new Regex(@"^(?:(?<num>[+-]?(?:[0-7]+(?:\.[0-7]*)?|\.[0-7]+))(?:e(?<exp>[+-]?(?:[0-7]+(?:\.[0-7]*)?|\.[0-7]+)))?
                    (?:(?<imag>[+-]?(?:[0-7]+(?:\.[0-7]*)?|\.[0-7]+))(?:e(?<imagexp>[+-]?(?:[0-7]+(?:\.[0-7]*)?|\.[0-7]+)))?i)?
                    |
                    (?<num>[+-]?[0-7]+)/(?<den>[+-]?[0-7]+)
                 )$",
              RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.IgnorePatternWhitespace|RegexOptions.Singleline);

  static readonly Regex decNum =
    new Regex(@"^(?:(?<num>[+-]?(?:\d+(?:\.\d*)?|\.\d+))(?:e(?<exp>[+-]?(?:\d+(?:\.\d*)?|\.\d+)))?
                    (?:(?<imag>[+-]?(?:\d+(?:\.\d*)?|\.\d+))(?:e(?<imagexp>[+-]?(?:\d+(?:\.\d*)?|\.\d+)))?i)?
                    |
                    (?<num>[+-]?\d+)/(?<den>[+-]?\d+)
                 )$",
              RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.IgnorePatternWhitespace|RegexOptions.Singleline);

  static readonly Regex hexNum =
    new Regex(@"^(?:(?<num>[+-]?(?:[\da-f]+(?:\.[\da-f]*)?|\.[\da-f]+))(?:e(?<exp>[+-]?(?:[\da-f]+(?:\.[\da-f]*)?|\.[\da-f]+)))?
                    (?:(?<imag>[+-]?(?:[\da-f]+(?:\.[\da-f]*)?|\.[\da-f]+))(?:e(?<imagexp>[+-]?(?:[\da-f]+(?:\.[\da-f]*)?|\.[\da-f]+)))?i)?
                    |
                    (?<num>[+-]?[\da-f]+)/(?<den>[+-]?[\da-f]+)
                 )$",
              RegexOptions.Compiled|RegexOptions.IgnoreCase|RegexOptions.IgnorePatternWhitespace|RegexOptions.Singleline);
}

} // namespace NetLisp.Backend