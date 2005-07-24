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
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using NetLisp.Backend;
using ICSharpCode.TextEditor;
using ICSharpCode.TextEditor.Gui.CompletionWindow;

namespace NetLisp.IDE
{

#region LispBox
public class LispBox : TextEditorControl
{ public LispBox()
  { ConvertTabsToSpaces = true;
    EnableFolding = ShowEOLMarkers = ShowInvalidLines = ShowLineNumbers = ShowSpaces = ShowTabs = ShowVRuler = false;
    TabIndent = 2;

    ActiveTextAreaControl.TextArea.DoProcessDialogKey += new DialogKeyProcessor(TextArea_DialogKey);
    ActiveTextAreaControl.TextArea.KeyEventHandler += new ICSharpCode.TextEditor.KeyEventHandler(TextArea_KeyEventHandler);

    Document.HighlightingStrategy =
      ICSharpCode.TextEditor.Document.HighlightingStrategyFactory.CreateHighlightingStrategy("Lisp");
  }

  public void AppendLine(string format, params object[] args) { AppendLine(string.Format(format, args)); }
  public void AppendLine(string text)
  { int end = Document.TextLength;
    if(end!=0 && Document.GetCharAt(end-1) != '\n') Document.Insert(end++, "\n");
    ActiveTextAreaControl.TextArea.Document.Insert(end, text+"\n");
  }

  public void InsertLine(string format, params object[] args) { InsertLine(string.Format(format, args)); }
  public void InsertLine(string text)
  { Caret caret = ActiveTextAreaControl.Caret;
    int line = caret.Line+1;
    if(line==Document.TotalNumberOfLines) Document.Insert(Document.TextLength, "\n");
    caret.Position = new Point(0, line);
    ActiveTextAreaControl.TextArea.Document.Insert(caret.Offset, text+"\n");
  }

  protected AutoCompleteBox AcBox { get { return EditForm.acbox; } }
  protected Module LispModule { get { return EditForm.lispModule; } }
  protected EditForm EditForm { get { return (EditForm)ParentForm; } }
  protected ImmediateBox Immediate { get { return EditForm.immediate; } }

  #region Event handlers
  bool TextArea_DialogKey(Keys key)
  { bool alt=(key&Keys.Alt)!=0, control=(key&Keys.Control)!=0, shift=(key&Keys.Shift)!=0;
    Keys code=key&Keys.KeyCode;

    if(code==Keys.Back)
    { if(typed.Length==0) HideCompletionBox();
      else
      { typed = typed.Substring(0, typed.Length-1);
        int curPos = ActiveTextAreaControl.Caret.Offset;
        if(curPos>0)
        { int index = AcBox.FindString(typed);
          if(index!=ListBox.NoMatches) AcBox.SelectedIndex = index;
        }
      }
      return false;
    }
    else if(!AcBox.Visible)
    { if(code==Keys.I && control && !alt && !shift) // ctrl-I
      { string ident = ActiveTextAreaControl.SelectionManager.SelectedText.Trim();
        if(ident=="") ident = PreviousIdentifier();
        if(ident!="")
        { object obj;
          if(GetObject(ident, out obj)) Immediate.Document.Insert(0, "SORRY NO HELP RIGHT NOW");
          else Immediate.Document.Insert(0, "No such object.\n");
        }
        return true;
      }
      else if(code==Keys.Return && alt && !control && !shift) // Alt-enter
      { TextAreaControl txt = ActiveTextAreaControl;
        Caret caret = txt.Caret;
        string source = txt.SelectionManager.SelectedText;
        int nextline;
        if(source=="")
        { source = Document.GetText(Document.GetLineSegmentForOffset(caret.Offset)).Trim();
          ICSharpCode.TextEditor.Document.LineSegment seg = Document.GetLineSegmentForOffset(caret.Offset);
          nextline = caret.Offset==seg.Offset+seg.TotalLength ? 2 : 1;
          if(source=="") goto move;
        }
        else nextline=0;
        EditForm.Run(source, true);

        move:
        if(nextline==2) return false;

        int line = 1 + (nextline==0 ? txt.SelectionManager.GetSelectionAt(caret.Offset).EndPosition.Y : caret.Line);
        if(line==Document.TotalNumberOfLines) Document.Insert(Document.TextLength, "\n");
        caret.Position = new Point(0, line);
        txt.SelectionManager.ClearSelection();
        return true;
      }
      else if(code==Keys.OemSemicolon && shift || code==Keys.Space && control && !shift && !alt) // colon or ctrl-space
      { if(key==Keys.OemSemicolon) PopulatePartial(typed=":");
        else typed = PopulatePartial();

        if(AcBox.Items.Count==1) { AcBox.SelectedIndex=0; SelectItem(); typed=""; }
        else if(AcBox.Items.Count!=0) { ShowCompletionBox(); AcBox.SelectedIndex=0; }
        else typed="";
        return code!=Keys.OemSemicolon;
      }
      else if(code==Keys.OemCloseBrackets && control && !shift && !alt) // ctrl-]
      { int index=ActiveTextAreaControl.Caret.Offset;
        if(index!=Document.TextLength)
        { char c = Document.GetCharAt(index), other;
          if(c==')' || c==']' || c=='}')
          { other = c==')' ? '(' : c==']' ? '[' : '{';
            index = Document.FormattingStrategy.SearchBracketBackward(Document, index-1, other, c);
          }
          else if(c=='(' || c=='[' || c=='{')
          { other = c=='(' ? ')' : c=='[' ? ']' : '}';
            index = Document.FormattingStrategy.SearchBracketForward(Document, index+1, c, other);
          }
          if(index != -1) ActiveTextAreaControl.Caret.Position = Document.OffsetToPosition(index);
        }
        return true;
      }
      return false;
    }
    else if((char)code=='\t' || (char)code=='\r' || (char)code=='\n') return TextArea_KeyEventHandler((char)code);
    else if(code==Keys.Up)
    { if(AcBox.SelectedIndex>0) AcBox.SelectedIndex--;
    }
    else if(code==Keys.Down)
    { if(AcBox.SelectedIndex<AcBox.Items.Count-1) AcBox.SelectedIndex++;
    }
    else if(code==Keys.PageUp)
    { int items = AcBox.ClientSize.Height / AcBox.GetItemHeight(0);
      if(AcBox.SelectedIndex>0) AcBox.SelectedIndex = Math.Max(0, AcBox.SelectedIndex-items);
    }
    else if(code==Keys.PageDown)
    { int items = AcBox.ClientSize.Height / AcBox.GetItemHeight(0);
      if(AcBox.SelectedIndex!=AcBox.Items.Count-1)
        AcBox.SelectedIndex = Math.Min(AcBox.Items.Count-1, AcBox.SelectedIndex+items);
    }
    else if(code==Keys.Home)
    { AcBox.SelectedIndex=0;
    }
    else if(code==Keys.End)
    { AcBox.SelectedIndex = AcBox.Items.Count-1;
    }
    else if(code==Keys.Escape) HideCompletionBox();
    else return false;
    
    return true;
  }

  bool TextArea_KeyEventHandler(char ch)
  { if(!AcBox.Visible || ch=='\b') return false;
    else if(ch<=32 || ch=='(' || ch==')')
    { bool handled=false;
      if(AcBox.SelectedIndex!=-1) SelectItem();
      if(ch=='\n' || ch=='\r' || ch=='\t') handled=true;
      HideCompletionBox();
      return handled;
    }
    else if(ch>32 && ch<127)
    { typed += ch;
      int index = AcBox.FindString(typed);
      if(index!=ListBox.NoMatches) AcBox.SelectedIndex = index;
    }
    return false;
  }
  #endregion

  enum Get { Normal, IgnoreLast, RawSlot }

  object GetObject(string ident)
  { object ret;
    return GetObject(ident, out ret) ? ret : null;
  }

  bool GetObject(string ident, out object ret)
  { ret=null;
    if(ident==null || ident=="") return false;
    return LispModule.TopLevel.Get(ident, out ret);
  }

  void HideCompletionBox()
  { AcBox.Hide();
    typed="";
  }

  string PopulatePartial()
  { string ident = PreviousIdentifier();
    PopulatePartial(ident);
    return ident;
  }

  void PopulatePartial(string ident)
  { AutoCompleteBox acbox = AcBox;
    acbox.Items.Clear();
    string[] keys = new string[LispModule.TopLevel.Globals.Count];
    LispModule.TopLevel.Globals.Keys.CopyTo(keys, 0);
    Array.Sort(keys);
    foreach(string name in keys)
    { Backend.Binding bind = (Backend.Binding)LispModule.TopLevel.Globals[name];
      if(bind!=Backend.Binding.Unbound && string.Compare(name, 0, ident, 0, ident.Length, true)==0)
        acbox.Items.Add(new AutoCompleteItem(bind.Value, name));
    }
  }

  string PreviousIdentifier()
  { int pos=ActiveTextAreaControl.Caret.Offset, end=pos;
    char c;
    while(--pos>=0 && !char.IsWhiteSpace(c=Document.GetCharAt(pos)) && c!='(' && c!=')');
    return end<=0 ? "" : Document.GetText(pos+1, end-pos-1);
  }

  internal void SelectItem()
  { string item = AcBox.GetItemText(AcBox.SelectedItem);
    Caret caret = ActiveTextAreaControl.Caret;
    if(typed.Length==0) Document.Insert(caret.Offset, item);
    else Document.Replace(caret.Offset-typed.Length, typed.Length, item);
    caret.Column += item.Length-typed.Length;
  }

  void ShowCompletionBox()
  { AcBox.Tag = this;
    Point cpt = ActiveTextAreaControl.Caret.ScreenPosition;
    int y = cpt.Y, xoff=0, yoff=0;
    Control ctl=this, form=ParentForm;
    while(ctl != form) { xoff += ctl.Left; yoff += ctl.Top; ctl=ctl.Parent; }

    cpt.X += xoff+2;
    cpt.Y += yoff+(int)Math.Ceiling(Font.GetHeight())+2;

    if(cpt.Y+AcBox.Height > Parent.ClientSize.Height)
    { y = yoff+y-AcBox.Height+2;
      if(y>=0) cpt.Y=y;
    }

    AcBox.Location = cpt;
    AcBox.BringToFront();
    AcBox.Show();
  }

  string typed="";
}
#endregion

// TODO: hook printing
#region ImmediateBox
public class ImmediateBox : LispBox
{ 
}
#endregion

} // namespace NetLisp.IDE